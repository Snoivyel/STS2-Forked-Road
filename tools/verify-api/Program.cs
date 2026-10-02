using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.PortableExecutable;

class Program
{
    static string GameDir = Environment.GetEnvironmentVariable("STS2_GAME_DIR") ?? @"D:\SteamLibrary\steamapps\common\Slay the Spire 2\data_sts2_windows_x86_64";
    static string ModDll = Environment.GetEnvironmentVariable("STS2_MOD_DLL") ?? @"C:\Users\ASUS\STS2-Forked-Road\bin\Release\net9.0\ForkedRoad.dll";
    static MetadataLoadContext _ctx;
    static Assembly _sts2;
    static Assembly _mod;

    static int _ok;
    static int _fail;
    static readonly List<string> _failures = new();

    static void Main()
    {
        var resolver = new PathAssemblyResolver(Directory.GetFiles(GameDir, "*.dll").Concat(new[] { ModDll }));
        _ctx = new MetadataLoadContext(resolver);
        _sts2 = _ctx.LoadFromAssemblyPath(Path.Combine(GameDir, "sts2.dll"));
        _mod = _ctx.LoadFromAssemblyPath(ModDll);

        Console.WriteLine("=== PART 1: HarmonyPatch targets from mod DLL ===");
        var allTypes = _mod.GetTypes();
        Console.WriteLine($"  (mod types: {allTypes.Length})");
        foreach (Type t in allTypes)
        {
            var attrs = t.GetCustomAttributesData().Where(a => a.AttributeType.FullName == "HarmonyLib.HarmonyPatch").ToList();
            if (attrs.Count == 0) continue;
            foreach (var attr in attrs)
            {
                VerifyPatchAttribute(t, attr);
            }
        }

        Console.WriteLine();
        Console.WriteLine("=== PART 2: Reflection (AccessTools) targets ===");
        VerifyAccessToolsTargets();

        Console.WriteLine();
        Console.WriteLine($"=== RESULT: OK={_ok} FAIL={_fail} ===");
        foreach (string f in _failures) Console.WriteLine("FAIL: " + f);
        Environment.Exit(_fail == 0 ? 0 : 1);
    }

    static string NormalizeType(Type t)
    {
        if (t == null) return "<null>";
        if (t.IsGenericType)
        {
            return t.GetGenericTypeDefinition().FullName + "[" + string.Join(",", t.GetGenericArguments().Select(NormalizeType)) + "]";
        }
        return t.FullName ?? t.Name;
    }

    static string Sig(params string[] parts) => string.Join(",", parts);

    static void Record(string label, bool ok, string detail = "")
    {
        if (ok) { _ok++; }
        else { _fail++; _failures.Add(label + (detail.Length > 0 ? " :: " + detail : "")); }
    }

    static Type? ResolveType(object? arg)
    {
        if (arg is Type t) return t;
        if (arg is CustomAttributeTypedArgument typed && typed.Value is Type tv) return tv;
        return null;
    }

    static string? GetMethodNameArg(CustomAttributeData attr)
    {
        foreach (var n in attr.NamedArguments)
        {
            if (n.MemberName == "methodName" && n.TypedValue.Value is string s) return s;
        }
        if (attr.ConstructorArguments.Count >= 2)
        {
            var v = attr.ConstructorArguments[1].Value;
            if (v is string s) return s;
        }
        return null;
    }

    static int? GetMethodTypeArg(CustomAttributeData attr)
    {
        foreach (var n in attr.NamedArguments)
        {
            if (n.MemberName == "methodType" && n.TypedValue.Value is int i) return i;
        }
        if (attr.ConstructorArguments.Count >= 2 && attr.ConstructorArguments[1].Value is int i2)
        {
            if (i2 != 0 && i2 != 3 && i2 != 4 && i2 != 5 && i2 != 6) return null;
            return i2;
        }
        return null;
    }

    static Type[]? GetArgTypes(CustomAttributeData attr)
    {
        foreach (var n in attr.NamedArguments)
        {
            if (n.MemberName == "argumentTypes" && n.TypedValue.Value is IList<CustomAttributeTypedArgument> list)
            {
                return list.Select(x => (Type)x.Value!).ToArray();
            }
        }
        if (attr.ConstructorArguments.Count >= 3 && attr.ConstructorArguments[2].Value is IList<CustomAttributeTypedArgument> ctorList)
        {
            return ctorList.Select(x => (Type)x.Value!).ToArray();
        }
        return null;
    }

    static void VerifyPatchAttribute(Type patchClass, CustomAttributeData attr)
    {
        Type? targetType = null;
        foreach (var c in attr.ConstructorArguments)
        {
            if (c.Value is Type tv) { targetType = tv; break; }
        }
        foreach (var n in attr.NamedArguments)
        {
            if (n.MemberName == "type" && n.TypedValue.Value is Type ntv) targetType = ntv;
        }
        if (targetType == null)
        {
            if (attr.ConstructorArguments.Count == 0 && attr.NamedArguments.Count == 0)
            {
                return;
            }
            Record($"HarmonyPatch on {patchClass.FullName}: no target type", false, "missing type argument");
            return;
        }

        string? methodName = GetMethodNameArg(attr);
        int? methodType = GetMethodTypeArg(attr);
        Type[]? argTypes = GetArgTypes(attr);
        string label = $"Patch {patchClass.FullName} -> {targetType.FullName}";

        if (methodType == 3) // Constructor
        {
            var ctors = targetType.GetConstructors(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance);
            var matching = ctors.Where(c => argTypes == null || CtorMatches(c, argTypes)).ToList();
            if (argTypes != null)
            {
                Record($"{label} ctor({Sig(argTypes.Select(NormalizeType).ToArray())})", matching.Count == 1, $"found={matching.Count} ctors={ctors.Length}");
            }
            else
            {
                Record($"{label} ctor", ctors.Length >= 1, $"found={ctors.Length}");
            }
            VerifyPatchParams(patchClass, matching.Cast<MethodBase>());
            return;
        }

        if (methodName == null)
        {
            if (targetType == null && methodType == null && attr.ConstructorArguments.Count == 0 && attr.NamedArguments.Count == 0)
            {
                return;
            }
            Record(label, false, "no method name");
            return;
        }

        var methods = targetType.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
            .Where(m => m.Name == methodName).ToList();
        if (argTypes != null)
        {
            var matching = methods.Where(m => MethodMatches(m, argTypes)).ToList();
            Record($"{label} {methodName}({Sig(argTypes.Select(NormalizeType).ToArray())})", matching.Count == 1, $"found={matching.Count} total={methods.Count}");
            VerifyPatchParams(patchClass, matching);
        }
        else
        {
            Record($"{label} {methodName}", methods.Count >= 1, $"total={methods.Count}");
            if (methods.Count > 1)
            {
                Record($"{label} {methodName}: overloads", false, "multiple overloads without argumentTypes (Harmony ambiguous)");
                return;
            }
            VerifyPatchParams(patchClass, methods);
        }
    }

    static Type StripByRef(Type t) => t.IsByRef ? t.GetElementType()! : t;

    static void VerifyPatchParams(Type patchClass, IEnumerable<MethodBase> targets)
    {
        if (!targets.Any()) return;
        foreach (var pm in patchClass.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static))
        {
            string n = pm.Name;
            if (n != "Prefix" && n != "Postfix" && n != "Finalizer") continue;
            foreach (var p in pm.GetParameters())
            {
                string pn = p.Name ?? "";
                if (pn.StartsWith("__") || pn.StartsWith("___")) continue;
                var target = targets.FirstOrDefault(t => t.GetParameters().Any(tp => tp.Name == pn));
                if (target == null)
                {
                    Record($"PatchParam {patchClass.FullName}.{n}({pn})", false, $"no parameter named '{pn}' in {targets.First()}");
                    continue;
                }
                var tp = target.GetParameters().First(tp2 => tp2.Name == pn);
                string patchType = NormalizeType(StripByRef(p.ParameterType));
                string targetParamType = NormalizeType(StripByRef(tp.ParameterType));
                if (patchType != targetParamType)
                {
                    Record($"PatchParam {patchClass.FullName}.{n}({pn})", false, $"type {patchType} != target {targetParamType}");
                }
            }
        }
    }

    static bool CtorMatches(ConstructorInfo c, Type[] argTypes)
    {
        var ps = c.GetParameters();
        if (ps.Length != argTypes.Length) return false;
        for (int i = 0; i < ps.Length; i++)
        {
            if (NormalizeType(ps[i].ParameterType) != NormalizeType(argTypes[i])) return false;
        }
        return true;
    }

    static bool MethodMatches(MethodInfo m, Type[] argTypes)
    {
        var ps = m.GetParameters();
        if (ps.Length != argTypes.Length) return false;
        for (int i = 0; i < ps.Length; i++)
        {
            if (NormalizeType(ps[i].ParameterType) != NormalizeType(argTypes[i])) return false;
        }
        return true;
    }

    // ---------------- PART 2: AccessTools targets ----------------

    static Type T(string fullName)
    {
        string[] parts = fullName.Split('/');
        Type t = _sts2.GetType(parts[0], false)!;
        for (int i = 1; i < parts.Length; i++)
        {
            t = t!.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault(x => x.Name == parts[i]);
        }
        if (t == null) throw new Exception("Cannot resolve type " + fullName);
        return t;
    }

    static void CheckField(string owner, string field, string? expectedType)
    {
        var t = T(owner);
        var f = t.GetField(field, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);
        if (f == null) { Record($"field {owner}.{field}", false, "not found"); return; }
        if (expectedType != null)
        {
            string actual = NormalizeType(f.FieldType);
            Record($"field {owner}.{field} : {expectedType}", actual == expectedType, $"actual={actual}");
        }
        else
        {
            Record($"field {owner}.{field}", true);
        }
    }

    static void CheckMethod(string owner, string method, string[]? argTypes)
    {
        var t = T(owner);
        var ms = t.GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
            .Where(m => m.Name == method).ToList();
        if (ms.Count == 0) { Record($"method {owner}.{method}", false, "not found"); return; }
        if (argTypes == null)
        {
            Record($"method {owner}.{method}", true, $"overloads={ms.Count}");
            return;
        }
        var matching = ms.Where(m => m.GetParameters().Length == argTypes.Length &&
            m.GetParameters().Select(p => NormalizeType(p.ParameterType)).SequenceEqual(argTypes)).ToList();
        Record($"method {owner}.{method}({string.Join(",", argTypes)})", matching.Count == 1, $"found={matching.Count} total={ms.Count}");
    }

    static void CheckInner(string owner, string inner)
    {
        var t = T(owner);
        var n = t.GetNestedTypes(BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault(x => x.Name == inner);
        Record($"inner {owner}.{inner}", n != null, n == null ? "not found" : n.FullName);
    }

    static void VerifyAccessToolsTargets()
    {
        // ForkedRoadPatches.cs
        CheckField("MegaCrit.Sts2.Core.Nodes.Multiplayer.NMultiplayerPlayerState", "_energyContainer", "Godot.Control");
        CheckField("MegaCrit.Sts2.Core.Nodes.Multiplayer.NMultiplayerPlayerState", "_starContainer", "Godot.Control");
        CheckField("MegaCrit.Sts2.Core.Nodes.Multiplayer.NMultiplayerPlayerState", "_cardContainer", "Godot.Control");
        CheckField("MegaCrit.Sts2.Core.Nodes.Combat.NPowerContainer", "_powerNodes", "System.Collections.Generic.List`1[MegaCrit.Sts2.Core.Nodes.Combat.NPower]");
        CheckField("MegaCrit.Sts2.Core.Multiplayer.Game.EventSynchronizer", "_events", "System.Collections.Generic.List`1[MegaCrit.Sts2.Core.Models.EventModel]");
        CheckField("MegaCrit.Sts2.Core.Multiplayer.Game.EventSynchronizer", "_playerVotes", "System.Collections.Generic.List`1[System.Nullable`1[System.UInt32]]");
        CheckField("MegaCrit.Sts2.Core.Nodes.Rooms.NMerchantRoom", "_players", "System.Collections.Generic.List`1[MegaCrit.Sts2.Core.Entities.Players.Player]");
        CheckField("MegaCrit.Sts2.Core.Nodes.Rooms.NRestSiteRoom", "_runState", "MegaCrit.Sts2.Core.Runs.IRunState");
        CheckField("MegaCrit.Sts2.Core.Nodes.Rooms.NTreasureRoom", "_runState", "MegaCrit.Sts2.Core.Runs.IRunState");
        CheckField("MegaCrit.Sts2.Core.Nodes.Rooms.NEventRoom", "_event", "MegaCrit.Sts2.Core.Models.EventModel");
        CheckField("MegaCrit.Sts2.Core.Nodes.Rooms.NTreasureRoom", "_isRelicCollectionOpen", "System.Boolean");
        CheckField("MegaCrit.Sts2.Core.Nodes.Rooms.NTreasureRoom", "_hasChestBeenOpened", "System.Boolean");
        CheckMethod("MegaCrit.Sts2.Core.Nodes.Combat.NPowerContainer", "UpdatePositions", null);
        CheckMethod("MegaCrit.Sts2.Core.Nodes.Events.NEventLayout", "SetEvent", null);

        // ForkedRoadManager.cs
        CheckField("MegaCrit.Sts2.Core.Multiplayer.Game.MapSelectionSynchronizer", "_votes", "System.Collections.Generic.List`1[System.Nullable`1[MegaCrit.Sts2.Core.Multiplayer.Game.MapVote]]");
        CheckField("MegaCrit.Sts2.Core.Multiplayer.Game.EventSynchronizer", "_playerCollection", "MegaCrit.Sts2.Core.Runs.IPlayerCollection");
        CheckField("MegaCrit.Sts2.Core.Multiplayer.Game.EventCombatSynchronizer", "_runState", "MegaCrit.Sts2.Core.Runs.IRunState");
        CheckField("MegaCrit.Sts2.Core.Multiplayer.Game.EventSynchronizer", "_combatSynchronizer", "MegaCrit.Sts2.Core.Multiplayer.Game.EventCombatSynchronizer");
        CheckMethod("MegaCrit.Sts2.Core.Combat.CombatManager", "EndCombatInternal", new[] { "MegaCrit.Sts2.Core.Combat.CombatTurnState" });
        CheckField("MegaCrit.Sts2.Core.Multiplayer.Game.EventSynchronizer", "_multiplayerOptionSelectionRng", "MegaCrit.Sts2.Core.Random.Rng");
        CheckField("MegaCrit.Sts2.Core.Multiplayer.Game.RestSiteSynchronizer", "_playerCollection", "MegaCrit.Sts2.Core.Runs.IPlayerCollection");
        CheckField("MegaCrit.Sts2.Core.Multiplayer.Game.TreasureRoomRelicSynchronizer", "_playerCollection", "MegaCrit.Sts2.Core.Runs.IPlayerCollection");
        CheckField("MegaCrit.Sts2.Core.Multiplayer.Game.OneOffSynchronizer", "_playerCollection", "MegaCrit.Sts2.Core.Runs.IPlayerCollection");
        CheckField("MegaCrit.Sts2.Core.Multiplayer.Game.MapSelectionSynchronizer", "_acceptingVotesFromSource", "MegaCrit.Sts2.Core.Runs.MapLocation");
        CheckField("MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceSynchronizer", "_choiceIds", "System.Collections.Generic.List`1[System.UInt32]");
        CheckField("MegaCrit.Sts2.Core.Multiplayer.CombatStateSynchronizer", "_syncData", "System.Collections.Generic.Dictionary`2[System.UInt64,MegaCrit.Sts2.Core.Saves.Runs.SerializablePlayer]");
        CheckField("MegaCrit.Sts2.Core.Multiplayer.CombatStateSynchronizer", "_rngSet", "MegaCrit.Sts2.Core.Saves.Runs.SerializableRunRngSet");
        CheckField("MegaCrit.Sts2.Core.Multiplayer.CombatStateSynchronizer", "_syncCompletionSource", "System.Threading.Tasks.TaskCompletionSource");
        CheckField("MegaCrit.Sts2.Core.GameActions.Multiplayer.ActionQueueSynchronizer", "_actionQueueSet", "MegaCrit.Sts2.Core.GameActions.Multiplayer.ActionQueueSet");
        CheckField("MegaCrit.Sts2.Core.Entities.Players.PlayerCombatState", "_energy", "System.Int32");
        CheckField("MegaCrit.Sts2.Core.Entities.Players.PlayerCombatState", "_stars", "System.Int32");
        CheckField("MegaCrit.Sts2.Core.Multiplayer.Game.RunLocationTargetedMessageBuffer", "_visitedLocations", "System.Collections.Generic.HashSet`1[MegaCrit.Sts2.Core.Runs.RunLocation]");
        CheckField("MegaCrit.Sts2.Core.GameActions.Multiplayer.ActionQueueSet", "_actionQueues", null);
        CheckInner("MegaCrit.Sts2.Core.GameActions.Multiplayer.ActionQueueSet", "ActionQueue");
        CheckField("MegaCrit.Sts2.Core.GameActions.Multiplayer.ActionQueueSet/ActionQueue", "ownerId", null);
        CheckField("MegaCrit.Sts2.Core.GameActions.Multiplayer.ActionQueueSet/ActionQueue", "actions", null);
        CheckMethod("MegaCrit.Sts2.Core.Runs.RunManager", "RollRoomTypeFor", null);
        CheckMethod("MegaCrit.Sts2.Core.Multiplayer.Game.EventSynchronizer", "ChooseOptionForSharedEvent", null);
        CheckMethod("MegaCrit.Sts2.Core.Runs.RunManager", "AfterMapLocationChanged", null);
        CheckMethod("MegaCrit.Sts2.Core.Nodes.Multiplayer.NMultiplayerPlayerState", "OnCombatSetUp", null);
        CheckMethod("MegaCrit.Sts2.Core.Nodes.Multiplayer.NMultiplayerPlayerState", "RefreshCombatValues", null);
        CheckMethod("MegaCrit.Sts2.Core.Nodes.Combat.NCreature", "UpdateBounds", new[] { "Godot.Node" });
        CheckMethod("MegaCrit.Sts2.Core.Models.EventModel", "SetEventState", null);
        CheckField("MegaCrit.Sts2.Core.Models.EventModel", "<Owner>k__BackingField", null);
        CheckField("MegaCrit.Sts2.Core.Rooms.MerchantRoom", "_runState", "MegaCrit.Sts2.Core.Runs.IRunState");
        CheckField("MegaCrit.Sts2.Core.GameActions.Multiplayer.PlayerChoiceSynchronizer", "_receivedChoices", null);
        CheckField("MegaCrit.Sts2.Core.Multiplayer.Game.RunLocationTargetedMessageBuffer", "_messagesWaitingOnLocationChange", null);

        // ForkedRoadManager.SaveRestore.cs
        CheckMethod("MegaCrit.Sts2.Core.Runs.RunManager", "EnterMapCoordInternal", null);
        CheckMethod("MegaCrit.Sts2.Core.Runs.RunManager", "EnterRoomInternal", null);
    }
}
