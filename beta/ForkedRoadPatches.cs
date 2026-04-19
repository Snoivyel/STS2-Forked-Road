using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Singleton;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.PeerInput;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Game;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Game.Sync;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.Overlays;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.ValueProps;

namespace ForkedRoad;

[HarmonyPatch]
internal static class ForkedRoadPatches
{

    private static readonly AccessTools.FieldRef<NMultiplayerPlayerState, Control> MultiplayerPlayerStateEnergyContainerRef =
        AccessTools.FieldRefAccess<NMultiplayerPlayerState, Control>("_energyContainer");

    private static readonly AccessTools.FieldRef<NMultiplayerPlayerState, Control> MultiplayerPlayerStateStarContainerRef =
        AccessTools.FieldRefAccess<NMultiplayerPlayerState, Control>("_starContainer");

    private static readonly AccessTools.FieldRef<NMultiplayerPlayerState, Control> MultiplayerPlayerStateCardContainerRef =
        AccessTools.FieldRefAccess<NMultiplayerPlayerState, Control>("_cardContainer");

    private static readonly AccessTools.FieldRef<NPowerContainer, System.Collections.Generic.List<NPower>> PowerContainerPowerNodesRef =
        AccessTools.FieldRefAccess<NPowerContainer, System.Collections.Generic.List<NPower>>("_powerNodes");

    private static readonly AccessTools.FieldRef<EventSynchronizer, System.Collections.Generic.List<EventModel>> EventSynchronizerEventsRef =
        AccessTools.FieldRefAccess<EventSynchronizer, System.Collections.Generic.List<EventModel>>("_events");

    private static readonly AccessTools.FieldRef<EventSynchronizer, System.Collections.Generic.List<uint?>> EventSynchronizerPlayerVotesRef =
        AccessTools.FieldRefAccess<EventSynchronizer, System.Collections.Generic.List<uint?>>("_playerVotes");

    private static readonly AccessTools.FieldRef<EventSynchronizer, EventModel> EventSynchronizerCanonicalEventRef =
        AccessTools.FieldRefAccess<EventSynchronizer, EventModel>("_canonicalEvent");

    private static readonly AccessTools.FieldRef<EventSynchronizer, uint> EventSynchronizerPageIndexRef =
        AccessTools.FieldRefAccess<EventSynchronizer, uint>("_pageIndex");

    private static readonly AccessTools.FieldRef<NMerchantRoom, System.Collections.Generic.List<Player>> MerchantRoomPlayersRef =
        AccessTools.FieldRefAccess<NMerchantRoom, System.Collections.Generic.List<Player>>("_players");

    private static readonly AccessTools.FieldRef<NRestSiteRoom, IRunState> RestSiteRoomRunStateRef =
        AccessTools.FieldRefAccess<NRestSiteRoom, IRunState>("_runState");

    private static readonly AccessTools.FieldRef<NTreasureRoom, IRunState> TreasureRoomRunStateRef =
        AccessTools.FieldRefAccess<NTreasureRoom, IRunState>("_runState");

    private static readonly AccessTools.FieldRef<NEventRoom, EventModel> EventRoomEventRef =
        AccessTools.FieldRefAccess<NEventRoom, EventModel>("_event");

    private static readonly AccessTools.FieldRef<NTreasureRoom, bool> TreasureRoomIsRelicCollectionOpenRef =
        AccessTools.FieldRefAccess<NTreasureRoom, bool>("_isRelicCollectionOpen");

    private static readonly System.Reflection.FieldInfo? TreasureRoomHasChestBeenOpenedField =
        AccessTools.Field(typeof(NTreasureRoom), "_hasChestBeenOpened");

    private static readonly System.Reflection.MethodInfo? PowerContainerUpdatePositionsMethod =
        AccessTools.Method(typeof(NPowerContainer), "UpdatePositions");

    private static bool HasTreasureRoomChestBeenOpened(NTreasureRoom room)
    {
        return TreasureRoomHasChestBeenOpenedField?.GetValue(room) is bool isOpen && isOpen;
    }


    [HarmonyPatch(typeof(RunManager), nameof(RunManager.Launch))]
    private static class RunManager_Launch_Patch
    {
        private static void Postfix(RunManager __instance, RunState __result)
        {
            ForkedRoadManager.InitializeForRun(__result, __instance.NetService);
        }
    }

    [HarmonyPatch(typeof(RunManager), nameof(RunManager.CleanUp))]
    private static class RunManager_CleanUp_Patch
    {
        private static void Prefix()
        {
            ForkedRoadManager.CleanUp();
        }
    }

    [HarmonyPatch(typeof(SaveManager), nameof(SaveManager.SaveRun))]
    private static class SaveManager_SaveRun_Patch
    {
        private static void Prefix(AbstractRoom? preFinishedRoom, ref bool saveProgress)
        {
            ForkedRoadManager.CaptureSaveRestoreSnapshotForCurrentRun();
            if (ForkedRoadManager.ShouldSuppressLegacyProgressSaveDuringAutosave())
            {
                saveProgress = false;
            }
        }
    }

    [HarmonyPatch(typeof(SaveManager), nameof(SaveManager.SaveProgressFile))]
    private static class SaveManager_SaveProgressFile_Patch
    {
        private static bool Prefix()
        {
            return !ForkedRoadManager.ShouldSuppressLegacyProgressSaveDuringAutosave();
        }
    }

    [HarmonyPatch(typeof(SaveManager), nameof(SaveManager.DeleteCurrentMultiplayerRun))]
    private static class SaveManager_DeleteCurrentMultiplayerRun_Patch
    {
        private static void Prefix()
        {
            ForkedRoadManager.DeleteSaveRestoreSnapshotFile();
        }
    }

    [HarmonyPatch(typeof(SaveManager), nameof(SaveManager.LoadAndCanonicalizeMultiplayerRunSave))]
    private static class SaveManager_LoadAndCanonicalizeMultiplayerRunSave_Patch
    {
        private static void Postfix(ReadSaveResult<SerializableRun> __result)
        {
            ForkedRoadManager.LoadSaveRestoreSnapshotFromDisk(__result.Success ? __result.SaveData : null);
        }
    }

    [HarmonyPatch(typeof(RunManager), nameof(RunManager.SetUpSavedMultiPlayer))]
    private static class RunManager_SetUpSavedMultiPlayer_Patch
    {
        private static void Prefix(LoadRunLobby lobby)
        {
            ForkedRoadManager.PrepareForSavedMultiplayerLoad(lobby.Run, lobby.NetService.Type, lobby.NetService);
        }
    }

    [HarmonyPatch(typeof(LoadRunLobby), MethodType.Constructor, new[] { typeof(INetGameService), typeof(ILoadRunLobbyListener), typeof(SerializableRun) })]
    private static class LoadRunLobby_Ctor_SaveRestore_Patch
    {
        private static void Postfix(LoadRunLobby __instance)
        {
            ForkedRoadManager.RegisterEarlySavedRestoreHandlers(__instance.NetService);
        }
    }

    [HarmonyPatch(typeof(LoadRunLobby), MethodType.Constructor, new[] { typeof(INetGameService), typeof(ILoadRunLobbyListener), typeof(ClientLoadJoinResponseMessage) })]
    private static class LoadRunLobby_ClientCtor_SaveRestore_Patch
    {
        private static void Postfix(LoadRunLobby __instance)
        {
            ForkedRoadManager.RegisterEarlySavedRestoreHandlers(__instance.NetService);
        }
    }

    [HarmonyPatch(typeof(LoadRunLobby), nameof(LoadRunLobby.CleanUp))]
    private static class LoadRunLobby_CleanUp_SaveRestore_Patch
    {
        private static void Postfix(LoadRunLobby __instance)
        {
            ForkedRoadManager.UnregisterEarlySavedRestoreHandlers(__instance.NetService);
        }
    }

    [HarmonyPatch(typeof(RunManager), nameof(RunManager.EnterMapCoord))]
    private static class RunManager_EnterMapCoord_Patch
    {
        private static void Prefix(MapCoord coord)
        {
            ForkedRoadManager.OnLocalMapCoordEntered(coord);
        }
    }

    [HarmonyPatch(typeof(RunManager), nameof(RunManager.LoadIntoLatestMapCoord))]
    private static class RunManager_LoadIntoLatestMapCoord_Patch
    {
        private static bool Prefix(RunManager __instance, AbstractRoom? preFinishedRoom, ref System.Threading.Tasks.Task __result)
        {
            return !ForkedRoadManager.TryHandleSavedSplitLoad(__instance, preFinishedRoom, ref __result);
        }
    }

    [HarmonyPatch(typeof(RunManager), nameof(RunManager.SetActInternal))]
    private static class RunManager_SetActInternal_Patch
    {
        private static void Prefix(int actIndex)
        {
            ForkedRoadManager.HandleActTransition(actIndex);
        }
    }

    [HarmonyPatch(typeof(MapSelectionSynchronizer), "PlayerVotedForMapCoord")]
    private static class MapSelectionSynchronizer_PlayerVotedForMapCoord_Patch
    {
        private static bool Prefix(MapSelectionSynchronizer __instance, Player player, MapLocation source, MapVote? destination)
        {
            return ForkedRoadManager.BeforePlayerVotedForMapCoord(__instance, player, source, destination);
        }
    }

    [HarmonyPatch(typeof(MapSelectionSynchronizer), "MoveToMapCoord")]
    private static class MapSelectionSynchronizer_MoveToMapCoord_Patch
    {
        private static bool Prefix(MapSelectionSynchronizer __instance)
        {
            return !ForkedRoadManager.TryHandleSplitMapResolution(__instance);
        }
    }



    [HarmonyPatch(typeof(EventRoom), nameof(EventRoom.EnterInternal))]
    private static class EventRoom_Enter_Patch
    {
        private static void Prefix(ref IRunState? runState)
        {
            ForkedRoadManager.PrepareEventSynchronizerScopeForLocalBranch();
            if (runState != null)
            {
                runState = ForkedRoadManager.ScopeRunStateToLocalBranch(runState);
            }
        }
    }

    [HarmonyPatch(typeof(NEventRoom), "_Ready")]
    private static class NEventRoom_Ready_Patch
    {
        private static void Postfix(NEventRoom __instance)
        {
            if (!ForkedRoadManager.ShouldPublishLocalEventSpectatorState(__instance))
            {
                return;
            }

            EventModel eventModel = EventRoomEventRef(__instance);
            ForkedRoadManager.PublishLocalEventSpectatorState(
                ForkedRoadManager.GetSafeSpectatorRawText(eventModel.Title, eventModel.Id.Entry),
                ForkedRoadManager.GetSafeSpectatorRawText(eventModel.Description, eventModel.Id.Entry),
                ForkedRoadManager.GetDisplayedEventSpectatorOptions(eventModel));
        }
    }

    [HarmonyPatch(typeof(NEventRoom), "RefreshEventState")]
    private static class NEventRoom_RefreshEventState_Patch
    {
        private static void Postfix(NEventRoom __instance, EventModel eventModel)
        {
            if (!ForkedRoadManager.ShouldPublishLocalEventSpectatorState(__instance))
            {
                return;
            }

            ForkedRoadManager.PublishLocalEventSpectatorState(
                ForkedRoadManager.GetSafeSpectatorRawText(eventModel.Title, eventModel.Id.Entry),
                ForkedRoadManager.GetSafeSpectatorRawText(eventModel.Description, eventModel.Id.Entry),
                ForkedRoadManager.GetDisplayedEventSpectatorOptions(eventModel));
        }
    }

    [HarmonyPatch(typeof(NEventRoom), "OptionButtonClicked")]
    private static class NEventRoom_OptionButtonClicked_Patch
    {
        private static bool Prefix()
        {
            return !ForkedRoadManager.IsReadOnlySpectatingRemoteBranch();
        }
    }

    [HarmonyPatch(typeof(MerchantRoom), nameof(MerchantRoom.EnterInternal))]
    private static class MerchantRoom_Enter_Patch
    {
        private static void Prefix()
        {
            ForkedRoadManager.PrepareOneOffSynchronizerScopeForLocalBranch();
        }
    }

    [HarmonyPatch(typeof(RestSiteRoom), nameof(RestSiteRoom.EnterInternal))]
    private static class RestSiteRoom_Enter_Patch
    {
        private static void Prefix()
        {
            ForkedRoadManager.PrepareRestSiteSynchronizerScopeForLocalBranch();
        }
    }

    [HarmonyPatch(typeof(TreasureRoom), nameof(TreasureRoom.EnterInternal))]
    private static class TreasureRoom_Enter_Patch
    {
        private static void Prefix()
        {
            ForkedRoadManager.PrepareTreasureSynchronizerScopeForLocalBranch();
        }
    }

    [HarmonyPatch(typeof(NTreasureRoom), "_Ready")]
    private static class NTreasureRoom_Ready_Spectator_Patch
    {
        private static void Postfix()
        {
            ForkedRoadManager.PublishLocalTreasureSpectatorState(
                "Treasure Room",
                "Chest unopened.",
                new[] { "Observe only" });
        }
    }

    [HarmonyPatch(typeof(NTreasureRoom), "OnChestButtonReleased")]
    private static class NTreasureRoom_OnChestButtonReleased_Spectator_Patch
    {
        private static bool Prefix()
        {
            if (ForkedRoadManager.IsReadOnlySpectatingRemoteBranch())
            {
                return false;
            }

            ForkedRoadManager.PublishLocalTreasureSpectatorState(
                "Treasure Room",
                "Opening chest...",
                new[] { "Opening", "Observe only" });
            return true;
        }
    }

    [HarmonyPatch(typeof(NTreasureRoom), "OnActiveScreenChanged")]
    private static class NTreasureRoom_OnActiveScreenChanged_Patch
    {
        private static void Postfix(NTreasureRoom __instance)
        {
            bool hasChestBeenOpened = HasTreasureRoomChestBeenOpened(__instance);
            bool isRelicCollectionOpen = TreasureRoomIsRelicCollectionOpenRef(__instance);
            string description = hasChestBeenOpened
                ? (isRelicCollectionOpen ? "Relic selection open." : "Proceed available.")
                : "Chest unopened.";
            ForkedRoadManager.PublishLocalTreasureSpectatorState(
                "Treasure Room",
                description,
                hasChestBeenOpened
                    ? (isRelicCollectionOpen ? new[] { "Choosing relic", "Observe only" } : new[] { "Proceed", "Observe only" })
                    : new[] { "Open chest", "Observe only" });
        }
    }

    [HarmonyPatch(typeof(CombatRoom), nameof(CombatRoom.EnterInternal))]
    private static class CombatRoom_Enter_Patch
    {
        private static void Prefix(CombatRoom __instance, IRunState? runState)
        {
            if (runState == null || __instance.CombatState.Players.Count > 0 || !ForkedRoadManager.ShouldScopeCombatParticipants(runState))
            {
                return;
            }

            foreach (Player player in ForkedRoadManager.GetCombatParticipantsForEntry(runState))
            {
                __instance.CombatState.AddPlayer(player);
            }
        }
    }

    [HarmonyPatch(typeof(CombatManager), nameof(CombatManager.SetUpCombat))]
    private static class CombatManager_SetUpCombat_Patch
    {
        private static void Postfix()
        {
            ForkedRoadManager.OnLocalCombatSetUp();
        }
    }

    [HarmonyPatch(typeof(NPowerContainer), "Remove")]
    private static class NPowerContainer_Remove_Patch
    {
        private static bool Prefix(NPowerContainer __instance, PowerModel power)
        {
            if (CombatManager.Instance.IsInProgress)
            {
                return true;
            }

            System.Collections.Generic.List<NPower> nodes = PowerContainerPowerNodesRef(__instance);
            NPower? node = nodes.FirstOrDefault(n => n.Model == power);
            if (node != null)
            {
                nodes.Remove(node);
                PowerContainerUpdatePositionsMethod?.Invoke(__instance, Array.Empty<object>());
                node.QueueFreeSafely();
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(NCombatRoom), "UpdateCreatureNavigation")]
    private static class NCombatRoom_UpdateCreatureNavigation_Spectator_Patch
    {
        private static bool Prefix(NCombatRoom __instance)
        {
            return !ForkedRoadManager.TryHandleSpectatorCombatRoomNavigation(__instance);
        }
    }

    [HarmonyPatch(typeof(CombatManager), nameof(CombatManager.EndCombatInternal))]
    private static class CombatManager_EndCombatInternal_Patch
    {
        private static void Prefix()
        {
            ForkedRoadManager.OnLocalCombatEnded();
        }
    }

    [HarmonyPatch(typeof(CombatManager), nameof(CombatManager.HandlePlayerDeath))]
    private static class CombatManager_HandlePlayerDeath_Patch
    {
        private static void Postfix(Player player)
        {
            ForkedRoadManager.OnCombatPlayerDied(player);
        }
    }

    [HarmonyPatch(typeof(CombatManager), "StartTurn")]
    private static class CombatManager_StartTurn_Patch
    {
        private static bool Prefix(ref System.Threading.Tasks.Task __result)
        {
            if (!ForkedRoadManager.TryTriggerDeathClearForCurrentCombat())
            {
                return true;
            }

            __result = System.Threading.Tasks.Task.CompletedTask;
            return false;
        }
    }

    [HarmonyPatch(typeof(CombatStateSynchronizer), "OnSyncPlayerMessageReceived")]
    private static class CombatStateSynchronizer_OnSyncPlayerMessageReceived_Patch
    {
        private static bool Prefix(SyncPlayerDataMessage syncMessage, ulong senderId)
        {
            return !ForkedRoadManager.ShouldIgnoreCombatSyncSender(senderId);
        }
    }

    [HarmonyPatch(typeof(CombatStateSynchronizer), "CheckSyncCompleted")]
    private static class CombatStateSynchronizer_CheckSyncCompleted_Patch
    {
        private static bool Prefix(CombatStateSynchronizer __instance)
        {
            return !ForkedRoadManager.TryHandleCombatSyncCompletion(__instance);
        }
    }

    [HarmonyPatch(typeof(ChecksumTracker), nameof(ChecksumTracker.GenerateChecksum), new[] { typeof(string), typeof(GameAction) })]
    private static class ChecksumTracker_GenerateChecksum_Patch
    {
        private static bool Prefix(string context, GameAction? action, ref NetChecksumData __result)
        {
            return !ForkedRoadManager.TrySuppressChecksum(context, action, ref __result);
        }
    }

    [HarmonyPatch(typeof(MultiplayerScalingModel), nameof(MultiplayerScalingModel.ModifyBlockMultiplicative))]
    private static class MultiplayerScalingModel_ModifyBlockMultiplicative_Patch
    {
        private static bool Prefix(Creature target, decimal block, ValueProp props, CardModel? cardSource, CardPlay? cardPlay, ref decimal __result)
        {
            return !ForkedRoadManager.TryOverrideLegacyMultiplayerBlockScaling(target, props, ref __result);
        }
    }

    [HarmonyPatch(typeof(MultiplayerScalingModel), nameof(MultiplayerScalingModel.ModifyPowerAmountGiven))]
    private static class MultiplayerScalingModel_ModifyPowerAmountGiven_Patch
    {
        private static bool Prefix(PowerModel power, Creature giver, decimal amount, Creature? target, CardModel? cardSource, ref decimal __result)
        {
            return !ForkedRoadManager.TryOverrideLegacyMultiplayerPowerScaling(power, amount, target, ref __result);
        }
    }

    [HarmonyPatch(typeof(RunManager), nameof(RunManager.ProceedFromTerminalRewardsScreen))]
    private static class RunManager_ProceedFromTerminalRewardsScreen_Patch
    {
        private static void Prefix()
        {
            if (ForkedRoadManager.ShouldDelayBranchCompletionForEmbeddedEventCombat())
            {
                return;
            }

            ForkedRoadManager.NotifyLocalBranchCompleted("combat_terminal");
        }
    }

    [HarmonyPatch(typeof(NEventRoom), nameof(NEventRoom.Proceed))]
    private static class NEventRoom_Proceed_Patch
    {
        private static void Prefix()
        {
            ForkedRoadManager.NotifyLocalBranchCompleted("event");
        }
    }

    [HarmonyPatch(typeof(NRestSiteRoom), "OnProceedButtonReleased")]
    private static class NRestSiteRoom_OnProceedButtonReleased_Patch
    {
        private static void Prefix()
        {
            ForkedRoadManager.NotifyLocalBranchCompleted("rest_site");
        }
    }

    [HarmonyPatch(typeof(NMerchantRoom), nameof(NMerchantRoom.Create))]
    private static class NMerchantRoom_Create_Patch
    {
        private static void Postfix(ref NMerchantRoom? __result)
        {
            if (__result == null)
            {
                return;
            }

            System.Collections.Generic.List<Player> players = MerchantRoomPlayersRef(__result);
            System.Collections.Generic.IReadOnlyList<Player> scopedPlayers = ForkedRoadManager.GetScopedPlayers(players);
            if (scopedPlayers.Count == players.Count)
            {
                return;
            }

            players.Clear();
            players.AddRange(scopedPlayers);
        }
    }

    [HarmonyPatch(typeof(NRestSiteRoom), nameof(NRestSiteRoom.Create))]
    private static class NRestSiteRoom_Create_Patch
    {
        private static void Postfix(ref NRestSiteRoom? __result)
        {
            if (__result == null)
            {
                return;
            }

            RestSiteRoomRunStateRef(__result) = ForkedRoadManager.ScopeRunStateToLocalBranch(RestSiteRoomRunStateRef(__result));
        }
    }

    [HarmonyPatch(typeof(NTreasureRoom), nameof(NTreasureRoom.Create))]
    private static class NTreasureRoom_Create_Patch
    {
        private static void Postfix(ref NTreasureRoom? __result)
        {
            if (__result == null)
            {
                return;
            }

            TreasureRoomRunStateRef(__result) = ForkedRoadManager.ScopeRunStateToLocalBranch(TreasureRoomRunStateRef(__result));
        }
    }

    [HarmonyPatch(typeof(NMerchantRoom), "HideScreen")]
    private static class NMerchantRoom_HideScreen_Patch
    {
        private static void Prefix()
        {
            ForkedRoadManager.NotifyLocalBranchCompleted("merchant");
        }
    }

    [HarmonyPatch(typeof(NTreasureRoom), "OnProceedButtonPressed")]
    private static class NTreasureRoom_OnProceedButtonPressed_Patch
    {
        private static void Prefix()
        {
            ForkedRoadManager.NotifyLocalBranchCompleted("treasure");
        }
    }

    [HarmonyPatch(typeof(NMapScreen), nameof(NMapScreen.Open))]
    private static class NMapScreen_Open_Patch
    {
        private static bool Prefix(NMapScreen __instance, ref NMapScreen __result)
        {
            if (!ForkedRoadManager.ShouldSuppressMapOpenForBarrier())
            {
                return true;
            }

            __result = __instance;
            return false;
        }
    }


    [HarmonyPatch(typeof(NMultiplayerPlayerState), "OnCombatSetUp")]
    private static class NMultiplayerPlayerState_OnCombatSetUp_Patch
    {
        private static bool Prefix(NMultiplayerPlayerState __instance)
        {
            if (!ForkedRoadManager.IsSplitBatchInProgress || LocalContext.IsMe(__instance.Player) || __instance.Player.PlayerCombatState != null)
            {
                return true;
            }

            MultiplayerPlayerStateEnergyContainerRef(__instance).Visible = false;
            MultiplayerPlayerStateStarContainerRef(__instance).Visible = false;
            MultiplayerPlayerStateCardContainerRef(__instance).Visible = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(NMultiplayerPlayerState), "RefreshCombatValues")]
    private static class NMultiplayerPlayerState_RefreshCombatValues_Patch
    {
        private static bool Prefix(NMultiplayerPlayerState __instance)
        {
            if (!ForkedRoadManager.IsSplitBatchInProgress || LocalContext.IsMe(__instance.Player) || __instance.Player.PlayerCombatState != null)
            {
                return true;
            }

            MultiplayerPlayerStateEnergyContainerRef(__instance).Visible = false;
            MultiplayerPlayerStateStarContainerRef(__instance).Visible = false;
            MultiplayerPlayerStateCardContainerRef(__instance).Visible = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(NMultiplayerPlayerState), "RefreshPlayerReadyIndicator", new[] { typeof(Player) })]
    private static class NMultiplayerPlayerState_RefreshPlayerReadyIndicator_Player_Safe_Patch
    {
        private static bool Prefix(NMultiplayerPlayerState __instance)
        {
            return ShouldRunMultiplayerPlayerStateReadyIndicator(__instance);
        }
    }

    [HarmonyPatch(typeof(NMultiplayerPlayerState), "RefreshPlayerReadyIndicator", new[] { typeof(Player), typeof(bool) })]
    private static class NMultiplayerPlayerState_RefreshPlayerReadyIndicator_PlayerBool_Safe_Patch
    {
        private static bool Prefix(NMultiplayerPlayerState __instance)
        {
            return ShouldRunMultiplayerPlayerStateReadyIndicator(__instance);
        }
    }

    private static bool ShouldRunMultiplayerPlayerStateReadyIndicator(NMultiplayerPlayerState instance)
    {
        RunState? runState = RunManager.Instance.DebugOnlyGetState();
        if (runState == null)
        {
            return true;
        }

        return runState.Players.Contains(instance.Player);
    }





    [HarmonyPatch(typeof(RewardSynchronizer), "HandleRewardObtainedMessage")]
    private static class RewardSynchronizer_HandleRewardObtainedMessage_Patch
    {
        private static bool Prefix(MegaCrit.Sts2.Core.Multiplayer.Messages.Game.Sync.RewardObtainedMessage message)
        {
            return !ForkedRoadManager.ShouldIgnoreRewardMessage(message.location);
        }
    }

    [HarmonyPatch(typeof(RewardSynchronizer), "HandleGoldLostMessage")]
    private static class RewardSynchronizer_HandleGoldLostMessage_Patch
    {
        private static bool Prefix(MegaCrit.Sts2.Core.Multiplayer.Messages.Game.Sync.GoldLostMessage message)
        {
            return !ForkedRoadManager.ShouldIgnoreRewardMessage(message.location);
        }
    }

    [HarmonyPatch(typeof(RewardSynchronizer), "HandleCardRemovedMessage")]
    private static class RewardSynchronizer_HandleCardRemovedMessage_Patch
    {
        private static bool Prefix(MegaCrit.Sts2.Core.Multiplayer.Messages.Game.CardRemovedMessage message)
        {
            return !ForkedRoadManager.ShouldIgnoreRewardMessage(message.Location);
        }
    }

    [HarmonyPatch(typeof(RewardSynchronizer), "HandlePaelsWingSacrifice")]
    private static class RewardSynchronizer_HandlePaelsWingSacrifice_Patch
    {
        private static bool Prefix(MegaCrit.Sts2.Core.Multiplayer.Messages.Game.PaelsWingSacrificeMessage message)
        {
            return !ForkedRoadManager.ShouldIgnoreRewardMessage(message.Location);
        }
    }

    [HarmonyPatch(typeof(RewardsCmd), nameof(RewardsCmd.OfferForRoomEnd))]
    private static class RewardsCmd_OfferForRoomEnd_Patch
    {
        private static bool Prefix(Player player, AbstractRoom room, ref System.Threading.Tasks.Task __result)
        {
            if (!ForkedRoadManager.ShouldSuppressRoomEndRewards(player, room))
            {
                return true;
            }

            __result = System.Threading.Tasks.Task.CompletedTask;
            return false;
        }
    }

    [HarmonyPatch(typeof(RestSiteSynchronizer), "HandleRestSiteOptionChosenMessage")]
    private static class RestSiteSynchronizer_HandleRestSiteOptionChosenMessage_Patch
    {
        private static bool Prefix(OptionIndexChosenMessage message)
        {
            return !ForkedRoadManager.ShouldIgnoreRestSiteMessage(message.location);
        }
    }

    [HarmonyPatch(typeof(RestSiteSynchronizer), "HandleRestSiteOptionHoveredMessage")]
    private static class RestSiteSynchronizer_HandleRestSiteOptionHoveredMessage_Patch
    {
        private static bool Prefix(MegaCrit.Sts2.Core.Multiplayer.Messages.Game.Flavor.RestSiteOptionHoveredMessage message)
        {
            return !ForkedRoadManager.ShouldIgnoreRestSiteMessage(message.Location);
        }
    }

    [HarmonyPatch(typeof(Player), nameof(Player.ReviveBeforeCombatEnd))]
    private static class Player_ReviveBeforeCombatEnd_Patch
    {
        private static bool Prefix(Player __instance, ref System.Threading.Tasks.Task __result)
        {
            if (!ForkedRoadManager.ShouldSkipReviveBeforeCombatEnd(__instance))
            {
                return true;
            }

            __result = System.Threading.Tasks.Task.CompletedTask;
            return false;
        }
    }

    [HarmonyPatch(typeof(NCardPlayQueue), "OnActionEnqueued")]
    private static class NCardPlayQueue_OnActionEnqueued_Patch
    {
        private static bool Prefix(GameAction action)
        {
            return ForkedRoadManager.ShouldDisplayActionInLocalBranchUi(action);
        }
    }

    [HarmonyPatch(typeof(NMultiplayerPlayerIntentHandler), "OnActionEnqueued")]
    private static class NMultiplayerPlayerIntentHandler_OnActionEnqueued_Patch
    {
        private static bool Prefix(GameAction action)
        {
            return ForkedRoadManager.ShouldDisplayActionInLocalBranchUi(action);
        }
    }



    [HarmonyPatch(typeof(ActionQueueSynchronizer), "HandleRequestEnqueueActionMessage")]
    private static class ActionQueueSynchronizer_HandleRequestEnqueueActionMessage_MapVote_Patch
    {
        private static bool Prefix(RequestEnqueueActionMessage message, ulong senderId)
        {
            if (!ForkedRoadManager.ShouldInterceptForeignMapVoteRequest(message))
            {
                return true;
            }

            ForkedRoadManager.HandleForeignMapVoteRequest(message, senderId);
            return false;
        }
    }

    [HarmonyPatch(typeof(ActionQueueSynchronizer), nameof(ActionQueueSynchronizer.RequestEnqueue))]
    private static class ActionQueueSynchronizer_RequestEnqueue_Patch
    {
        private static bool Prefix()
        {
            return !ForkedRoadManager.IsReadOnlySpectatingRemoteBranch();
        }
    }

    [HarmonyPatch(typeof(ActionQueueSynchronizer), nameof(ActionQueueSynchronizer.RequestEnqueueHookAction))]
    private static class ActionQueueSynchronizer_RequestEnqueueHookAction_Patch
    {
        private static bool Prefix()
        {
            return !ForkedRoadManager.IsReadOnlySpectatingRemoteBranch();
        }
    }

    [HarmonyPatch(typeof(ActionQueueSynchronizer), nameof(ActionQueueSynchronizer.RequestResumeActionAfterPlayerChoice))]
    private static class ActionQueueSynchronizer_RequestResumeActionAfterPlayerChoice_Block_Patch
    {
        private static bool Prefix()
        {
            return !ForkedRoadManager.IsReadOnlySpectatingRemoteBranch();
        }
    }

    [HarmonyPatch(typeof(ActionQueueSynchronizer), "HandleRequestEnqueueActionMessage")]
    private static class ActionQueueSynchronizer_HandleRequestEnqueueActionMessage_Patch
    {
        private static bool Prefix(RequestEnqueueActionMessage message, ulong senderId)
        {
            if (!ForkedRoadManager.ShouldRelayForeignBranchAction(message.location))
            {
                return true;
            }

            ForkedRoadManager.RelayForeignBranchAction(message, senderId);
            return false;
        }
    }

    [HarmonyPatch(typeof(ActionQueueSynchronizer), "HandleRequestEnqueueHookActionMessage")]
    private static class ActionQueueSynchronizer_HandleRequestEnqueueHookActionMessage_Patch
    {
        private static bool Prefix(RequestEnqueueHookActionMessage message, ulong senderId)
        {
            if (!ForkedRoadManager.ShouldRelayForeignBranchAction(message.location))
            {
                return true;
            }

            ForkedRoadManager.RelayForeignBranchHookAction(message, senderId);
            return false;
        }
    }

    [HarmonyPatch(typeof(ActionQueueSynchronizer), "HandleRequestResumeActionAfterPlayerChoiceMessage")]
    private static class ActionQueueSynchronizer_HandleRequestResumeActionAfterPlayerChoiceMessage_Patch
    {
        private static bool Prefix(ActionQueueSynchronizer __instance, RequestResumeActionAfterPlayerChoiceMessage afterPlayerChoiceMessage, ulong senderId)
        {
            return !ForkedRoadManager.TryHandleResumeActionAfterPlayerChoiceRequest(__instance, afterPlayerChoiceMessage, senderId);
        }
    }

    [HarmonyPatch(typeof(MapSplitVoteAnimation), nameof(MapSplitVoteAnimation.TryPlay))]
    private static class MapSplitVoteAnimation_TryPlay_Patch
    {
        private static bool Prefix(ref System.Threading.Tasks.Task __result)
        {
            if (!ForkedRoadManager.ShouldSuppressSplitVoteAnimation())
            {
                return true;
            }

            __result = System.Threading.Tasks.Task.CompletedTask;
            return false;
        }
    }

    [HarmonyPatch(typeof(NMapScreen), nameof(NMapScreen.RefreshAllMapPointVotes))]
    private static class NMapScreen_RefreshAllMapPointVotes_Patch
    {
        private static void Prefix(NMapScreen __instance)
        {
            ForkedRoadManager.SyncMapPlayerMarkers(__instance);
        }
    }



    [HarmonyPatch(typeof(ActionQueueSynchronizer), "HandleActionEnqueuedMessage")]
    private static class ActionQueueSynchronizer_HandleActionEnqueuedMessage_Patch
    {
        private static bool Prefix(ActionEnqueuedMessage message)
        {
            return !ForkedRoadManager.ShouldIgnoreBufferedAction(message);
        }
    }

    [HarmonyPatch(typeof(ActionQueueSynchronizer), "HandleHookActionEnqueuedMessage")]
    private static class ActionQueueSynchronizer_HandleHookActionEnqueuedMessage_Patch
    {
        private static bool Prefix(HookActionEnqueuedMessage message)
        {
            return !ForkedRoadManager.ShouldIgnoreBufferedHookLocation(message.location);
        }
    }

    [HarmonyPatch(typeof(ActionQueueSynchronizer), "HandleResumeActionAfterPlayerChoiceMessage")]
    private static class ActionQueueSynchronizer_HandleResumeActionAfterPlayerChoiceMessage_Patch
    {
        private static bool Prefix(ActionQueueSynchronizer __instance, ResumeActionAfterPlayerChoiceMessage afterPlayerChoiceMessage)
        {
            return !ForkedRoadManager.TryHandleResumeActionAfterPlayerChoiceMessage(__instance, afterPlayerChoiceMessage);
        }
    }

    [HarmonyPatch(typeof(NMapScreen), nameof(NMapScreen.Open))]
    private static class NMapScreen_Open_PostRefresh_Patch
    {
        private static void Postfix(NMapScreen __instance)
        {
            ForkedRoadManager.SyncMapPlayerMarkers(__instance);
            __instance.RefreshAllMapPointVotes();
        }
    }



    [HarmonyPatch(typeof(EventModel), nameof(EventModel.GenerateInternalCombatState))]
    private static class EventModel_GenerateInternalCombatState_Patch
    {
        private static void Prefix(ref IRunState runState)
        {
            runState = ForkedRoadManager.ScopeRunStateToLocalBranch(runState);
        }
    }

    [HarmonyPatch(typeof(NCombatEventLayout), nameof(NCombatEventLayout.SetEvent))]
    private static class NCombatEventLayout_SetEvent_Patch
    {
        private static bool Prefix(NCombatEventLayout __instance, EventModel eventModel)
        {
            if (!ForkedRoadManager.IsSplitBatchInProgress)
            {
                return true;
            }

            if (eventModel.Owner == null)
            {
                return true;
            }

            IRunState runState = ForkedRoadManager.ScopeRunStateToLocalBranch(eventModel.Owner.RunState);
            ICombatRoomVisuals visuals = eventModel.CreateCombatRoomVisuals(runState.Players, runState.Act);
            NCombatRoom? nCombatRoom = NCombatRoom.Create(visuals, CombatRoomMode.VisualOnly);
            __instance.SetCombatRoomNode(nCombatRoom);
            nCombatRoom?.SetUpBackground(runState);
            AccessTools.Method(typeof(NEventLayout), nameof(NEventLayout.SetEvent))?.Invoke(__instance, new object[] { eventModel });
            return false;
        }
    }



    [HarmonyPatch(typeof(EventSynchronizer), nameof(EventSynchronizer.BeginEvent))]
    private static class EventSynchronizer_BeginEvent_Patch
    {
        private static bool Prefix(EventSynchronizer __instance, EventModel canonicalEvent, bool isPrefinished, System.Action<EventModel>? debugOnStart)
        {
            if (!ForkedRoadManager.IsSplitBatchInProgress)
            {
                return true;
            }

            ForkedRoadManager.PrepareEventSynchronizerScopeForLocalBranch();
            IRunState? currentRunState = RunManager.Instance?.DebugOnlyGetState();
            if (currentRunState == null)
            {
                return true;
            }

            IRunState scopedRunState = ForkedRoadManager.ScopeRunStateToLocalBranch(currentRunState);
            System.Collections.Generic.IReadOnlyList<Player> players = scopedRunState.Players;
            Log.Debug($"ForkedRoad beginning scoped event {canonicalEvent.Id}: shared={canonicalEvent.IsShared} players=[{string.Join(",", players.Select(static player => player.NetId))}]");
            System.Collections.Generic.List<uint?> votes = EventSynchronizerPlayerVotesRef(__instance);
            if (votes.Count > players.Count)
            {
                votes.RemoveRange(players.Count, votes.Count - players.Count);
            }
            while (votes.Count < players.Count)
            {
                votes.Add(null);
            }
            for (int i = 0; i < votes.Count; i++)
            {
                votes[i] = null;
            }

            System.Collections.Generic.List<EventModel> events = EventSynchronizerEventsRef(__instance);
            foreach (EventModel existingEvent in events)
            {
                if (!existingEvent.IsFinished)
                {
                    existingEvent.EnsureCleanup();
                }
            }
            events.Clear();
            EventSynchronizerPageIndexRef(__instance) = 0u;
            EventSynchronizerCanonicalEventRef(__instance) = canonicalEvent;
            foreach (Player player in players)
            {
                EventModel eventModel = canonicalEvent.ToMutable();
                debugOnStart?.Invoke(eventModel);
                events.Add(eventModel);
                TaskHelper.RunSafely(eventModel.BeginEvent(player, isPrefinished));
                Log.Debug($"ForkedRoad began scoped event instance for player {player.NetId}: event={eventModel.Id} options={eventModel.CurrentOptions.Count}");
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(EventSynchronizer), nameof(EventSynchronizer.GetPlayerVote))]
    private static class EventSynchronizer_GetPlayerVote_Patch
    {
        private static bool Prefix(Player player, ref uint? __result)
        {
            if (!ForkedRoadManager.IsSplitBatchInProgress)
            {
                return true;
            }

            IRunState? currentRunState = RunManager.Instance?.DebugOnlyGetState();
            if (currentRunState == null)
            {
                return true;
            }

            if (!ForkedRoadManager.ScopeRunStateToLocalBranch(currentRunState).Players.Contains(player))
            {
                __result = null;
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(EventSynchronizer), "HandleEventOptionChosenMessage")]
    private static class EventSynchronizer_HandleEventOptionChosenMessage_Patch
    {
        private static bool Prefix(OptionIndexChosenMessage message)
        {
            return !ForkedRoadManager.ShouldIgnoreEventMessage(message.location);
        }
    }

    [HarmonyPatch(typeof(EventSynchronizer), "HandleVotedForSharedEventOptionMessage")]
    private static class EventSynchronizer_HandleVotedForSharedEventOptionMessage_Patch
    {
        private static bool Prefix(VotedForSharedEventOptionMessage message)
        {
            return !ForkedRoadManager.ShouldIgnoreEventMessage(message.location);
        }

        private static void Postfix(EventSynchronizer __instance, VotedForSharedEventOptionMessage message)
        {
            if (ForkedRoadManager.ShouldIgnoreEventMessage(message.location))
            {
                return;
            }

            ForkedRoadManager.TryResolveSharedEventChoiceAsBranchAuthority(__instance);
        }
    }

    [HarmonyPatch(typeof(EventSynchronizer), "HandleSharedEventOptionChosenMessage")]
    private static class EventSynchronizer_HandleSharedEventOptionChosenMessage_Patch
    {
        private static bool Prefix(SharedEventOptionChosenMessage message)
        {
            return !ForkedRoadManager.ShouldIgnoreEventMessage(message.location);
        }
    }

    [HarmonyPatch(typeof(EventSynchronizer), nameof(EventSynchronizer.ChooseLocalOption))]
    private static class EventSynchronizer_ChooseLocalOption_Patch
    {
        private static void Postfix(EventSynchronizer __instance)
        {
            ForkedRoadManager.TryResolveSharedEventChoiceAsBranchAuthority(__instance);
        }
    }

    [HarmonyPatch(typeof(ScreenStateTracker), "OnOverlayStackChanged")]
    private static class ScreenStateTracker_OnOverlayStackChanged_Patch
    {
        private static bool Prefix()
        {
            // Latest game-beta already handles rewards-screen callback wiring internally.
            // Let the original implementation run so this patch does not depend on stale private fields.
            return true;
        }
    }



    [HarmonyPatch(typeof(RunLocationTargetedMessageBuffer), nameof(RunLocationTargetedMessageBuffer.OnLocationChanged))]
    private static class RunLocationTargetedMessageBuffer_OnLocationChanged_Patch
    {
        private static void Prefix(RunLocation location)
        {
            ForkedRoadManager.NormalizeRunLocationBuffer(location);
        }
    }

}

