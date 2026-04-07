using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Game.Lobby;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Game;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Game.Sync;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;

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


    private static readonly AccessTools.FieldRef<EventModel, CombatState> EventModelCombatStateRef =
        AccessTools.FieldRefAccess<EventModel, CombatState>("_combatStateForCombatLayout");



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
        private static void Prefix(AbstractRoom? preFinishedRoom)
        {
            ForkedRoadManager.CaptureSaveRestoreSnapshotForCurrentRun();
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
            ForkedRoadManager.PrepareForSavedMultiplayerLoad(lobby.Run, lobby.NetService.Type);
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
    private static class EventRoom_EnterInternal_Patch
    {
        private static void Prefix()
        {
            ForkedRoadManager.PrepareEventSynchronizerScopeForLocalBranch();
        }
    }

    [HarmonyPatch(typeof(MerchantRoom), nameof(MerchantRoom.EnterInternal))]
    private static class MerchantRoom_EnterInternal_Patch
    {
        private static void Prefix()
        {
            ForkedRoadManager.PrepareOneOffSynchronizerScopeForLocalBranch();
        }
    }

    [HarmonyPatch(typeof(RestSiteRoom), nameof(RestSiteRoom.EnterInternal))]
    private static class RestSiteRoom_EnterInternal_Patch
    {
        private static void Prefix()
        {
            ForkedRoadManager.PrepareRestSiteSynchronizerScopeForLocalBranch();
        }
    }

    [HarmonyPatch(typeof(TreasureRoom), nameof(TreasureRoom.EnterInternal))]
    private static class TreasureRoom_EnterInternal_Patch
    {
        private static void Prefix()
        {
            ForkedRoadManager.PrepareTreasureSynchronizerScopeForLocalBranch();
        }
    }

    [HarmonyPatch(typeof(CombatRoom), nameof(CombatRoom.EnterInternal))]
    private static class CombatRoom_EnterInternal_Patch
    {
        private static void Prefix(CombatRoom __instance, IRunState? runState)
        {
            if (runState == null || __instance.CombatState.Players.Count > 0 || !ForkedRoadManager.ShouldScopeCombatParticipants(runState))
            {
                return;
            }

            foreach (Player player in ForkedRoadManager.GetActivePlayers(runState))
            {
                if (player.Creature.IsDead)
                {
                    player.Creature.HealInternal(1m);
                }
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
        private static void Postfix(EventModel __instance)
        {
            if (!ForkedRoadManager.IsSplitBatchInProgress)
            {
                return;
            }

            CombatState? combatState = EventModelCombatStateRef(__instance);
            if (combatState == null)
            {
                return;
            }

            IReadOnlyList<Player> activePlayers = ForkedRoadManager.GetActivePlayers(combatState.RunState);
            HashSet<ulong> activeIds = activePlayers.Select(player => player.NetId).ToHashSet();
            foreach (Player player in combatState.Players.Where(player => !activeIds.Contains(player.NetId)).ToList())
            {
                combatState.RemoveCreature(player.Creature, unattach: false);
            }
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
            System.Collections.Generic.IReadOnlyList<Player> players = ForkedRoadManager.GetActivePlayers(RunManager.Instance.DebugOnlyGetState()!);
            System.Collections.Generic.List<uint?> votes = EventSynchronizerPlayerVotesRef(__instance);
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

            if (!ForkedRoadManager.GetActivePlayers(RunManager.Instance.DebugOnlyGetState()!).Contains(player))
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
    }

    [HarmonyPatch(typeof(EventSynchronizer), "HandleSharedEventOptionChosenMessage")]
    private static class EventSynchronizer_HandleSharedEventOptionChosenMessage_Patch
    {
        private static bool Prefix(SharedEventOptionChosenMessage message)
        {
            return !ForkedRoadManager.ShouldIgnoreEventMessage(message.location);
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
