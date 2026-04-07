using Godot.Bridge;
using HarmonyLib;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Modding;

namespace ForkedRoad;

[ModInitializer("Init")]
public static class ForkedRoadEntry
{
    private static Harmony? _harmony;

    public static void Init()
    {
        _harmony ??= new Harmony("sts2.qilu.forkedroad.rebuild");
        _harmony.PatchAll(typeof(ForkedRoadEntry).Assembly);
        ScriptManagerBridge.LookupScriptsInAssembly(typeof(ForkedRoadEntry).Assembly);
        Log.Info("ForkedRoad rebuild initialized.");
    }
}
