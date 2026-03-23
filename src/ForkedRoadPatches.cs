using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Exceptions;
using MegaCrit.Sts2.Core.Events;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Hooks;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Singleton;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Game.Sync;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Audio;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Nodes.Screens.Shops;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Nodes.Screens.ScreenContext;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.Saves.Managers;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.TestSupport;
using MegaCrit.Sts2.Core.ValueProps;

namespace ForkedRoad;

internal static class ForkedRoadPatches
{
    private static readonly MegaCrit.Sts2.Core.Logging.Logger MerchantLog = new("ForkedRoadMerchant", LogType.Generic);
    private static readonly MegaCrit.Sts2.Core.Logging.Logger TreasureLog = new("ForkedRoadTreasure", LogType.Generic);
    private static readonly MegaCrit.Sts2.Core.Logging.Logger EventLog = new("ForkedRoadEvent", LogType.Generic);
    private static readonly MegaCrit.Sts2.Core.Logging.Logger SaveLog = new("ForkedRoadSave", LogType.Generic);

    private static readonly Dictionary<ulong, int> SharedEventVoteSnapshot = new();

    internal static readonly AccessTools.FieldRef<MapSelectionSynchronizer, RunState> MapSelectionRunStateRef =
        AccessTools.FieldRefAccess<MapSelectionSynchronizer, RunState>("_runState");

    internal static readonly AccessTools.FieldRef<MapSelectionSynchronizer, INetGameService> MapSelectionNetServiceRef =
        AccessTools.FieldRefAccess<MapSelectionSynchronizer, INetGameService>("_netService");

    internal static readonly AccessTools.FieldRef<MapSelectionSynchronizer, List<MapVote?>> MapSelectionVotesRef =
        AccessTools.FieldRefAccess<MapSelectionSynchronizer, List<MapVote?>>("_votes");

    private static readonly AccessTools.FieldRef<RunManager, RunState?> RunManagerStateRef =
        AccessTools.FieldRefAccess<RunManager, RunState?>("<State>k__BackingField");

    internal static readonly AccessTools.FieldRef<RunState, List<MapCoord>> RunStateVisitedCoordsRef =
        AccessTools.FieldRefAccess<RunState, List<MapCoord>>("_visitedMapCoords");

    private static readonly AccessTools.FieldRef<MegaCrit.Sts2.Core.Multiplayer.Game.PeerInput.ScreenStateTracker, MegaCrit.Sts2.Core.Nodes.Screens.NRewardsScreen?> ScreenStateTrackerRewardsRef =
        AccessTools.FieldRefAccess<MegaCrit.Sts2.Core.Multiplayer.Game.PeerInput.ScreenStateTracker, MegaCrit.Sts2.Core.Nodes.Screens.NRewardsScreen?>("_connectedRewardsScreen");

    private static readonly AccessTools.FieldRef<MegaCrit.Sts2.Core.Multiplayer.Game.PeerInput.ScreenStateTracker, NetScreenType> ScreenStateTrackerOverlayScreenRef =
        AccessTools.FieldRefAccess<MegaCrit.Sts2.Core.Multiplayer.Game.PeerInput.ScreenStateTracker, NetScreenType>("_overlayScreen");

    private static readonly MethodInfo ScreenStateTrackerSyncLocalScreenMethod =
        AccessTools.Method(typeof(MegaCrit.Sts2.Core.Multiplayer.Game.PeerInput.ScreenStateTracker), "SyncLocalScreen")!;

    private static readonly MethodInfo MapSelectionMoveToMapCoordMethod =
        AccessTools.Method(typeof(MapSelectionSynchronizer), "MoveToMapCoord")!;

    private static readonly MethodInfo RunManagerExitCurrentRoomMethod =
        AccessTools.Method(typeof(RunManager), "ExitCurrentRoom")!;

    private static readonly FieldInfo MapSelectionPlayerVoteChangedField =
        AccessTools.Field(typeof(MapSelectionSynchronizer), "PlayerVoteChanged")!;

    private static readonly AccessTools.FieldRef<NMapScreen, RunState> MapScreenRunStateRef =
        AccessTools.FieldRefAccess<NMapScreen, RunState>("_runState");

    private static readonly AccessTools.FieldRef<NMapScreen, ActMap> MapScreenMapRef =
        AccessTools.FieldRefAccess<NMapScreen, ActMap>("_map");

    private static readonly AccessTools.FieldRef<NMapScreen, Dictionary<MapCoord, NMapPoint>> MapScreenPointDictionaryRef =
        AccessTools.FieldRefAccess<NMapScreen, Dictionary<MapCoord, NMapPoint>>("_mapPointDictionary");

    private static readonly AccessTools.FieldRef<NMapScreen, NMapPoint> MapScreenStartingPointRef =
        AccessTools.FieldRefAccess<NMapScreen, NMapPoint>("_startingPointNode");

    private static readonly AccessTools.FieldRef<NMapScreen, NBossMapPoint> MapScreenBossPointRef =
        AccessTools.FieldRefAccess<NMapScreen, NBossMapPoint>("_bossPointNode");

    private static readonly AccessTools.FieldRef<NMapScreen, NBossMapPoint?> MapScreenSecondBossPointRef =
        AccessTools.FieldRefAccess<NMapScreen, NBossMapPoint?>("_secondBossPointNode");

    private static readonly AccessTools.FieldRef<NMapScreen, NMapMarker> MapScreenMarkerRef =
        AccessTools.FieldRefAccess<NMapScreen, NMapMarker>("_marker");

    private static readonly AccessTools.FieldRef<NMapScreen, Control> MapScreenMapContainerRef =
        AccessTools.FieldRefAccess<NMapScreen, Control>("_mapContainer");

    private static readonly AccessTools.FieldRef<NMapScreen, Vector2> MapScreenTargetDragPosRef =
        AccessTools.FieldRefAccess<NMapScreen, Vector2>("_targetDragPos");

    private static readonly AccessTools.FieldRef<NMapScreen, float> MapScreenDistYRef =
        AccessTools.FieldRefAccess<NMapScreen, float>("_distY");

    private static readonly MethodInfo NMapScreenOnPlayerVoteChangedInternalMethod =
        AccessTools.Method(typeof(NMapScreen), "OnPlayerVoteChangedInternal")!;

    private static readonly AccessTools.FieldRef<NMapPoint, NMapScreen> MapPointScreenRef =
        AccessTools.FieldRefAccess<NMapPoint, NMapScreen>("_screen");

    private static readonly AccessTools.FieldRef<NMapPoint, IRunState> MapPointRunStateRef =
        AccessTools.FieldRefAccess<NMapPoint, IRunState>("_runState");

    private static readonly MethodInfo CombatManagerWaitUntilQueueEmptyMethod =
        AccessTools.Method(typeof(CombatManager), "WaitUntilQueueIsEmptyOrWaitingOnNonPlayerDrivenAction")!;

    private static readonly MethodInfo CombatManagerEndPlayerTurnPhaseOneMethod =
        AccessTools.Method(typeof(CombatManager), "EndPlayerTurnPhaseOneInternal")!;

    private static readonly FieldInfo CombatManagerCombatWonField =
        AccessTools.Field(typeof(CombatManager), "CombatWon")!;

    private static readonly FieldInfo CombatManagerCombatEndedField =
        AccessTools.Field(typeof(CombatManager), "CombatEnded")!;

    private static readonly AccessTools.FieldRef<CombatManager, List<Player>> CombatManagerPlayersTakingExtraTurnRef =
        AccessTools.FieldRefAccess<CombatManager, List<Player>>("_playersTakingExtraTurn");

    private static readonly AccessTools.FieldRef<MerchantRoom, MegaCrit.Sts2.Core.Entities.Merchant.MerchantInventory> MerchantInventoryRef =
        AccessTools.FieldRefAccess<MerchantRoom, MegaCrit.Sts2.Core.Entities.Merchant.MerchantInventory>("<Inventory>k__BackingField");

    private static readonly AccessTools.FieldRef<MegaCrit.Sts2.Core.Nodes.Rooms.NMerchantRoom, List<Player>> NMerchantRoomPlayersRef =
        AccessTools.FieldRefAccess<MegaCrit.Sts2.Core.Nodes.Rooms.NMerchantRoom, List<Player>>("_players");

    private static readonly AccessTools.FieldRef<MegaCrit.Sts2.Core.Nodes.Rooms.NMerchantRoom, Control> NMerchantRoomCharacterContainerRef =
        AccessTools.FieldRefAccess<MegaCrit.Sts2.Core.Nodes.Rooms.NMerchantRoom, Control>("_characterContainer");

    private static readonly AccessTools.FieldRef<MegaCrit.Sts2.Core.Nodes.Rooms.NMerchantRoom, List<NMerchantCharacter>> NMerchantRoomPlayerVisualsRef =
        AccessTools.FieldRefAccess<MegaCrit.Sts2.Core.Nodes.Rooms.NMerchantRoom, List<NMerchantCharacter>>("_playerVisuals");

    private static readonly AccessTools.FieldRef<MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic.NTreasureRoomRelicCollection, List<MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic.NTreasureRoomRelicHolder>> TreasureHoldersInUseRef =
        AccessTools.FieldRefAccess<MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic.NTreasureRoomRelicCollection, List<MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic.NTreasureRoomRelicHolder>>("_holdersInUse");

    private static readonly AccessTools.FieldRef<MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic.NTreasureRoomRelicCollection, TaskCompletionSource?> TreasureRelicPickingTcsRef =
        AccessTools.FieldRefAccess<MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic.NTreasureRoomRelicCollection, TaskCompletionSource?>("_relicPickingTaskCompletionSource");

    private static readonly AccessTools.FieldRef<MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic.NTreasureRoomRelicCollection, IRunState> TreasureRelicCollectionRunStateRef =
        AccessTools.FieldRefAccess<MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic.NTreasureRoomRelicCollection, IRunState>("_runState");

    private static readonly AccessTools.FieldRef<NTreasureRoom, object> NTreasureRoomRelicCollectionRef =
        AccessTools.FieldRefAccess<NTreasureRoom, object>("_relicCollection");

    private static readonly AccessTools.FieldRef<NTreasureRoom, bool> NTreasureRoomIsRelicCollectionOpenRef =
        AccessTools.FieldRefAccess<NTreasureRoom, bool>("_isRelicCollectionOpen");

    private static readonly AccessTools.FieldRef<NTreasureRoom, bool> NTreasureRoomHasRelicBeenClaimedRef =
        AccessTools.FieldRefAccess<NTreasureRoom, bool>("_hasRelicBeenClaimed");

    private static readonly AccessTools.FieldRef<NTreasureRoom, Node2D> NTreasureRoomChestNodeRef =
        AccessTools.FieldRefAccess<NTreasureRoom, Node2D>("_chestNode");

    private static readonly AccessTools.FieldRef<NTreasureRoom, NProceedButton> NTreasureRoomProceedButtonRef =
        AccessTools.FieldRefAccess<NTreasureRoom, NProceedButton>("_proceedButton");

    private static readonly AccessTools.FieldRef<NTreasureRoom, NCommonBanner> NTreasureRoomBannerRef =
        AccessTools.FieldRefAccess<NTreasureRoom, NCommonBanner>("_banner");

    private static readonly AccessTools.FieldRef<MegaCrit.Sts2.Core.Multiplayer.Game.EventSynchronizer, List<EventModel>> EventSynchronizerEventsRef =
        AccessTools.FieldRefAccess<MegaCrit.Sts2.Core.Multiplayer.Game.EventSynchronizer, List<EventModel>>("_events");

    private static readonly AccessTools.FieldRef<MegaCrit.Sts2.Core.Multiplayer.Game.EventSynchronizer, EventModel?> EventSynchronizerCanonicalEventRef =
        AccessTools.FieldRefAccess<MegaCrit.Sts2.Core.Multiplayer.Game.EventSynchronizer, EventModel?>("_canonicalEvent");

    private static readonly AccessTools.FieldRef<MegaCrit.Sts2.Core.Multiplayer.Game.EventSynchronizer, List<uint?>> EventSynchronizerPlayerVotesRef =
        AccessTools.FieldRefAccess<MegaCrit.Sts2.Core.Multiplayer.Game.EventSynchronizer, List<uint?>>("_playerVotes");

    private static readonly AccessTools.FieldRef<MegaCrit.Sts2.Core.Multiplayer.Game.EventSynchronizer, uint> EventSynchronizerPageIndexRef =
        AccessTools.FieldRefAccess<MegaCrit.Sts2.Core.Multiplayer.Game.EventSynchronizer, uint>("_pageIndex");

    private static readonly AccessTools.FieldRef<MegaCrit.Sts2.Core.Multiplayer.Game.EventSynchronizer, IPlayerCollection> EventSynchronizerPlayerCollectionRef =
        AccessTools.FieldRefAccess<MegaCrit.Sts2.Core.Multiplayer.Game.EventSynchronizer, IPlayerCollection>("_playerCollection");

    private static readonly AccessTools.FieldRef<MegaCrit.Sts2.Core.Multiplayer.Game.EventSynchronizer, INetGameService> EventSynchronizerNetServiceRef =
        AccessTools.FieldRefAccess<MegaCrit.Sts2.Core.Multiplayer.Game.EventSynchronizer, INetGameService>("_netService");

    private static readonly AccessTools.FieldRef<MegaCrit.Sts2.Core.Multiplayer.Game.EventSynchronizer, MegaCrit.Sts2.Core.Random.Rng> EventSynchronizerSharedOptionRngRef =
        AccessTools.FieldRefAccess<MegaCrit.Sts2.Core.Multiplayer.Game.EventSynchronizer, MegaCrit.Sts2.Core.Random.Rng>("_multiplayerOptionSelectionRng");

    private static readonly FieldInfo EventSynchronizerPlayerVoteChangedField =
        AccessTools.Field(typeof(MegaCrit.Sts2.Core.Multiplayer.Game.EventSynchronizer), "PlayerVoteChanged")!;

    private static readonly AccessTools.FieldRef<EventModel, CombatState?> EventModelCombatStateForCombatLayoutRef =
        AccessTools.FieldRefAccess<EventModel, CombatState?>("_combatStateForCombatLayout");

    private static readonly AccessTools.FieldRef<EventModel, Control?> EventModelNodeRef =
        AccessTools.FieldRefAccess<EventModel, Control?>("<Node>k__BackingField");

    private static readonly FieldInfo EventModelEnteringEventCombatField =
        AccessTools.Field(typeof(EventModel), "EnteringEventCombat")!;

    private static readonly AccessTools.FieldRef<MegaCrit.Sts2.Core.Nodes.Events.NEventOptionButton, int> NEventOptionButtonIndexRef =
        AccessTools.FieldRefAccess<MegaCrit.Sts2.Core.Nodes.Events.NEventOptionButton, int>("<Index>k__BackingField");

    private static readonly AccessTools.FieldRef<MegaCrit.Sts2.Core.Nodes.Events.NEventLayout, EventModel> NEventLayoutEventRef =
        AccessTools.FieldRefAccess<MegaCrit.Sts2.Core.Nodes.Events.NEventLayout, EventModel>("_event");

    private static readonly AccessTools.FieldRef<MegaCrit.Sts2.Core.Nodes.Events.EventSplitVoteAnimation, List<Player>> EventSplitVoteAnimationSortedPlayersRef =
        AccessTools.FieldRefAccess<MegaCrit.Sts2.Core.Nodes.Events.EventSplitVoteAnimation, List<Player>>("_sortedPlayers");

    private static readonly AccessTools.FieldRef<MegaCrit.Sts2.Core.Multiplayer.Game.TreasureRoomRelicSynchronizer, List<RelicModel>?> TreasureCurrentRelicsRef =
        AccessTools.FieldRefAccess<MegaCrit.Sts2.Core.Multiplayer.Game.TreasureRoomRelicSynchronizer, List<RelicModel>?>("_currentRelics");

    private static readonly AccessTools.FieldRef<MegaCrit.Sts2.Core.Multiplayer.Game.TreasureRoomRelicSynchronizer, List<int?>> TreasureVotesRef =
        AccessTools.FieldRefAccess<MegaCrit.Sts2.Core.Multiplayer.Game.TreasureRoomRelicSynchronizer, List<int?>>("_votes");

    private static readonly AccessTools.FieldRef<MegaCrit.Sts2.Core.Multiplayer.Game.TreasureRoomRelicSynchronizer, int?> TreasurePredictedVoteRef =
        AccessTools.FieldRefAccess<MegaCrit.Sts2.Core.Multiplayer.Game.TreasureRoomRelicSynchronizer, int?>("_predictedVote");

    private static readonly AccessTools.FieldRef<MegaCrit.Sts2.Core.Multiplayer.Game.TreasureRoomRelicSynchronizer, IPlayerCollection> TreasurePlayerCollectionRef =
        AccessTools.FieldRefAccess<MegaCrit.Sts2.Core.Multiplayer.Game.TreasureRoomRelicSynchronizer, IPlayerCollection>("_playerCollection");

    private static readonly AccessTools.FieldRef<MegaCrit.Sts2.Core.Multiplayer.Game.TreasureRoomRelicSynchronizer, MegaCrit.Sts2.Core.Runs.RelicGrabBag> TreasureSharedGrabBagRef =
        AccessTools.FieldRefAccess<MegaCrit.Sts2.Core.Multiplayer.Game.TreasureRoomRelicSynchronizer, MegaCrit.Sts2.Core.Runs.RelicGrabBag>("_sharedGrabBag");

    private static readonly AccessTools.FieldRef<MegaCrit.Sts2.Core.Multiplayer.Game.TreasureRoomRelicSynchronizer, MegaCrit.Sts2.Core.Random.Rng> TreasureRngRef =
        AccessTools.FieldRefAccess<MegaCrit.Sts2.Core.Multiplayer.Game.TreasureRoomRelicSynchronizer, MegaCrit.Sts2.Core.Random.Rng>("_rng");

    private static readonly FieldInfo TreasureVotesChangedField =
        AccessTools.Field(typeof(MegaCrit.Sts2.Core.Multiplayer.Game.TreasureRoomRelicSynchronizer), "VotesChanged")!;

    private static readonly FieldInfo TreasureRelicsAwardedField =
        AccessTools.Field(typeof(MegaCrit.Sts2.Core.Multiplayer.Game.TreasureRoomRelicSynchronizer), "RelicsAwarded")!;

    private static readonly MethodInfo TreasureTryGetRelicForTutorialMethod =
        AccessTools.Method(typeof(MegaCrit.Sts2.Core.Multiplayer.Game.TreasureRoomRelicSynchronizer), "TryGetRelicForTutorial")!;

    private static readonly AccessTools.FieldRef<MegaCrit.Sts2.Core.Entities.Multiplayer.NetFullCombatState, List<uint>> NetFullCombatStateChoiceIdsRef =
        AccessTools.FieldRefAccess<MegaCrit.Sts2.Core.Entities.Multiplayer.NetFullCombatState, List<uint>>("nextChoiceIds");

    private static readonly AccessTools.FieldRef<MegaCrit.Sts2.Core.Entities.Multiplayer.NetFullCombatState, uint?> NetFullCombatStateLastHookRef =
        AccessTools.FieldRefAccess<MegaCrit.Sts2.Core.Entities.Multiplayer.NetFullCombatState, uint?>("lastExecutedHookId");

    private static readonly AccessTools.FieldRef<MegaCrit.Sts2.Core.Entities.Multiplayer.NetFullCombatState, uint?> NetFullCombatStateLastActionRef =
        AccessTools.FieldRefAccess<MegaCrit.Sts2.Core.Entities.Multiplayer.NetFullCombatState, uint?>("lastExecutedActionId");

    private static readonly AccessTools.FieldRef<MegaCrit.Sts2.Core.Entities.Multiplayer.NetFullCombatState, List<MegaCrit.Sts2.Core.Entities.Multiplayer.NetFullCombatState.CreatureState>> NetFullCombatStateCreaturesRef =
        AccessTools.FieldRefAccess<MegaCrit.Sts2.Core.Entities.Multiplayer.NetFullCombatState, List<MegaCrit.Sts2.Core.Entities.Multiplayer.NetFullCombatState.CreatureState>>("<Creatures>k__BackingField");

    private static readonly AccessTools.FieldRef<MegaCrit.Sts2.Core.Entities.Multiplayer.NetFullCombatState, List<MegaCrit.Sts2.Core.Entities.Multiplayer.NetFullCombatState.PlayerState>> NetFullCombatStatePlayersRef =
        AccessTools.FieldRefAccess<MegaCrit.Sts2.Core.Entities.Multiplayer.NetFullCombatState, List<MegaCrit.Sts2.Core.Entities.Multiplayer.NetFullCombatState.PlayerState>>("<Players>k__BackingField");

    private static readonly AccessTools.FieldRef<MegaCrit.Sts2.Core.Entities.Multiplayer.NetFullCombatState, MegaCrit.Sts2.Core.Saves.Runs.SerializableRunRngSet> NetFullCombatStateRngRef =
        AccessTools.FieldRefAccess<MegaCrit.Sts2.Core.Entities.Multiplayer.NetFullCombatState, MegaCrit.Sts2.Core.Saves.Runs.SerializableRunRngSet>("<Rng>k__BackingField");

    private static readonly AccessTools.FieldRef<NCombatUi, CombatState> NCombatUiStateRef =
        AccessTools.FieldRefAccess<NCombatUi, CombatState>("_state");

    private static readonly AccessTools.FieldRef<MegaCrit.Sts2.Core.Nodes.Combat.NEndTurnButton, CombatState?> NEndTurnButtonCombatStateRef =
        AccessTools.FieldRefAccess<MegaCrit.Sts2.Core.Nodes.Combat.NEndTurnButton, CombatState?>("_combatState");

    private static readonly AccessTools.FieldRef<MegaCrit.Sts2.Core.Nodes.Combat.NEndTurnButton, CardPile?> NEndTurnButtonPlayerHandRef =
        AccessTools.FieldRefAccess<MegaCrit.Sts2.Core.Nodes.Combat.NEndTurnButton, CardPile?>("_playerHand");

    private static readonly AccessTools.FieldRef<SaveManager, ProgressSaveManager> SaveManagerProgressManagerRef =
        AccessTools.FieldRefAccess<SaveManager, ProgressSaveManager>("_progressSaveManager");

    private static readonly AccessTools.FieldRef<RunSaveManager, ISaveStore> RunSaveManagerSaveStoreRef =
        AccessTools.FieldRefAccess<RunSaveManager, ISaveStore>("_saveStore");

    private static readonly AccessTools.FieldRef<RunSaveManager, bool> RunSaveManagerForceSynchronousRef =
        AccessTools.FieldRefAccess<RunSaveManager, bool>("_forceSynchronous");

    private static readonly AccessTools.FieldRef<RunSaveManager, IProfileIdProvider> RunSaveManagerProfileIdProviderRef =
        AccessTools.FieldRefAccess<RunSaveManager, IProfileIdProvider>("_profileIdProvider");

    private static readonly FieldInfo RunSaveManagerSavedField =
        AccessTools.Field(typeof(RunSaveManager), "Saved")!;

    internal static void InvokePlayerVoteChanged(MapSelectionSynchronizer synchronizer, Player player, MapVote? oldVote, MapVote? newVote)
    {
        (MapSelectionPlayerVoteChangedField.GetValue(synchronizer) as Action<Player, MapVote?, MapVote?>)?.Invoke(player, oldVote, newVote);
    }

    private static void InvokeEventPlayerVoteChanged(MegaCrit.Sts2.Core.Multiplayer.Game.EventSynchronizer synchronizer, Player player)
    {
        (EventSynchronizerPlayerVoteChangedField.GetValue(synchronizer) as Action<Player>)?.Invoke(player);
    }

    private static void InvokeEventEnteringCombat(EventModel eventModel)
    {
        (EventModelEnteringEventCombatField.GetValue(eventModel) as Action)?.Invoke();
    }

    private static bool IsRetriableSaveException(Exception exception)
    {
        if (exception is AggregateException aggregateException)
        {
            return aggregateException.InnerExceptions.Any(IsRetriableSaveException);
        }

        if (exception is SaveException)
        {
            return true;
        }

        if (exception.InnerException != null)
        {
            return IsRetriableSaveException(exception.InnerException);
        }

        return false;
    }

    private static void SaveProgressWithRetry(ProgressSaveManager progressSaveManager)
    {
        const int maxAttempts = 8;
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                progressSaveManager.SaveProgress();
                return;
            }
            catch (Exception ex) when (IsRetriableSaveException(ex) && attempt < maxAttempts)
            {
                SaveLog.Warn($"Retrying progress save after transient file lock. attempt={attempt} error={ex.Message}");
                Thread.Sleep(150 * attempt);
            }
        }
    }

    private static async Task SaveRunWithRetryAsync(RunSaveManager manager, AbstractRoom? preFinishedRoom)
    {
        if (!RunManager.Instance.ShouldSave || (RunManager.Instance.NetService.Type != NetGameType.Singleplayer && RunManager.Instance.NetService.Type != NetGameType.Host))
        {
            return;
        }

        SerializableRun serializableRun = RunManager.Instance.ToSave(preFinishedRoom);
        IProfileIdProvider profileIdProvider = RunSaveManagerProfileIdProviderRef(manager);
        string savePath = RunManager.Instance.NetService.Type == NetGameType.Singleplayer
            ? RunSaveManager.GetRunSavePath(profileIdProvider.CurrentProfileId, RunSaveManager.runSaveFileName)
            : RunSaveManager.GetRunSavePath(profileIdProvider.CurrentProfileId, RunSaveManager.multiplayerRunSaveFileName);

        ISaveStore saveStore = RunSaveManagerSaveStoreRef(manager);
        bool forceSynchronous = RunSaveManagerForceSynchronousRef(manager);
        const int maxAttempts = 8;

        using MemoryStream stream = new();
        if (!forceSynchronous)
        {
            await JsonSerializer.SerializeAsync(stream, serializableRun, JsonSerializationUtility.GetTypeInfo<SerializableRun>(), default);
            stream.Seek(0L, SeekOrigin.Begin);
            byte[] bytes = stream.ToArray();

            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    await saveStore.WriteFileAsync(savePath, bytes);
                    (RunSaveManagerSavedField.GetValue(manager) as Action)?.Invoke();
                    return;
                }
                catch (Exception ex) when (IsRetriableSaveException(ex) && attempt < maxAttempts)
                {
                    SaveLog.Warn($"Retrying run save after transient file lock. attempt={attempt} path={savePath} error={ex.Message}");
                    await Task.Delay(150 * attempt);
                }
            }
        }

        JsonSerializer.Serialize(stream, serializableRun, JsonSerializationUtility.GetTypeInfo<SerializableRun>());
        stream.Seek(0L, SeekOrigin.Begin);
        byte[] syncBytes = stream.ToArray();
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                saveStore.WriteFile(savePath, syncBytes);
                (RunSaveManagerSavedField.GetValue(manager) as Action)?.Invoke();
                return;
            }
            catch (Exception ex) when (IsRetriableSaveException(ex) && attempt < maxAttempts)
            {
                SaveLog.Warn($"Retrying synchronous run save after transient file lock. attempt={attempt} path={savePath} error={ex.Message}");
                Thread.Sleep(150 * attempt);
            }
        }
    }

    private static void CaptureSharedEventVotes(MegaCrit.Sts2.Core.Multiplayer.Game.EventSynchronizer synchronizer)
    {
        SharedEventVoteSnapshot.Clear();

        IPlayerCollection playerCollection = EventSynchronizerPlayerCollectionRef(synchronizer);
        List<uint?> playerVotes = EventSynchronizerPlayerVotesRef(synchronizer);
        foreach (Player player in playerCollection.Players.Where(ForkedRoadManager.IsPlayerActive))
        {
            uint? vote = playerVotes[playerCollection.GetPlayerSlotIndex(player)];
            if (vote.HasValue)
            {
                SharedEventVoteSnapshot[player.NetId] = (int)vote.Value;
            }
        }

        EventLog.Info($"Captured shared-event vote snapshot: active={string.Join(",", playerCollection.Players.Where(ForkedRoadManager.IsPlayerActive).Select(static p => p.NetId))} votes={string.Join(",", SharedEventVoteSnapshot.Select(static pair => $"{pair.Key}:{pair.Value}"))}");
    }

    private static void ClearSharedEventVoteSnapshot()
    {
        SharedEventVoteSnapshot.Clear();
    }

    private static bool TryGetSharedEventVote(Player player, out int optionIndex)
    {
        return SharedEventVoteSnapshot.TryGetValue(player.NetId, out optionIndex);
    }

    private static List<Player> GetEventActivePlayers(EventModel eventModel)
    {
        if (eventModel.Owner?.RunState == null)
        {
            return new List<Player>();
        }

        return ForkedRoadManager.GetActivePlayers(eventModel.Owner.RunState).ToList();
    }

    private static async Task BeforeSharedOptionChosenSplitAsync(MegaCrit.Sts2.Core.Nodes.Events.NEventLayout layout, EventOption option)
    {
        List<MegaCrit.Sts2.Core.Nodes.Events.NEventOptionButton> buttons = layout.OptionButtons.ToList();
        MegaCrit.Sts2.Core.Nodes.Events.NEventOptionButton? chosenButton = null;
        foreach (MegaCrit.Sts2.Core.Nodes.Events.NEventOptionButton optionButton in buttons)
        {
            optionButton.Disable();
            optionButton.RefreshVotes();
            if (optionButton.Option == option)
            {
                chosenButton = optionButton;
            }
        }

        if (chosenButton == null)
        {
            EventLog.Warn($"Shared-event animation skipped because chosen button for option {option.TextKey} was not found.");
            return;
        }

        EventModel eventModel = chosenButton.Event;
        List<Player> activePlayers = GetEventActivePlayers(eventModel);
        EventLog.Info($"Running split shared-event resolution for {eventModel.Id}: activePlayers={string.Join(",", activePlayers.Select(static p => p.NetId))} snapshotVotes={string.Join(",", SharedEventVoteSnapshot.Select(static pair => $"{pair.Key}:{pair.Value}"))}");

        if (activePlayers.Count > 1)
        {
            List<Player> sortedPlayers = buttons
                .SelectMany(static button => button.VoteContainer.Players)
                .Distinct()
                .ToList();

            if (sortedPlayers.Count > 1)
            {
                MegaCrit.Sts2.Core.Random.Rng rng = new((uint)HashCode.Combine(eventModel.Owner!.RunState.Rng.Seed, eventModel.Owner.RunState.ActFloor));
                int ticks = rng.NextInt(12, 18);
                float pause = rng.NextFloat(0.05f, 0.3f);
                List<Player> chosenPlayers = chosenButton.VoteContainer.Players.ToList();
                Player winner = rng.NextItem(chosenPlayers.Count > 0 ? chosenPlayers : sortedPlayers);
                Player? highlightedPlayer = null;

                void HighlightPlayer(Player? player)
                {
                    if (highlightedPlayer == player)
                    {
                        return;
                    }

                    if (highlightedPlayer != null)
                    {
                        foreach (MegaCrit.Sts2.Core.Nodes.Events.NEventOptionButton button in buttons)
                        {
                            if (button.VoteContainer.Players.Contains(highlightedPlayer))
                            {
                                button.VoteContainer.SetPlayerHighlighted(highlightedPlayer, isHighlighted: false);
                            }
                        }
                    }

                    highlightedPlayer = player;
                    if (player == null)
                    {
                        return;
                    }

                    foreach (MegaCrit.Sts2.Core.Nodes.Events.NEventOptionButton button in buttons)
                    {
                        if (button.VoteContainer.Players.Contains(player))
                        {
                            button.VoteContainer.SetPlayerHighlighted(player, isHighlighted: true);
                        }
                    }
                }

                Tween tween = layout.CreateTween();
                tween.TweenMethod(Callable.From<float>((float value) =>
                {
                    if (sortedPlayers.Count == 0)
                    {
                        return;
                    }

                    int tick = Mathf.RoundToInt(value * ticks);
                    int winnerIndex = sortedPlayers.IndexOf(winner) - (ticks - tick);
                    int index = (winnerIndex % sortedPlayers.Count + sortedPlayers.Count) % sortedPlayers.Count;
                    HighlightPlayer(sortedPlayers[index]);
                }), 0f, 1f, 1.2000000476837158f).SetTrans(Tween.TransitionType.Sine).SetEase(Tween.EaseType.Out);
                tween.TweenInterval(pause);
                await layout.ToSignal(tween, Tween.SignalName.Finished);
                chosenButton.VoteContainer.BouncePlayers();
            }
        }

        foreach (MegaCrit.Sts2.Core.Nodes.Events.NEventOptionButton optionButton in buttons)
        {
            if (optionButton != chosenButton)
            {
                optionButton.GrayOut();
            }
        }

        await chosenButton.FlashConfirmation();
    }

    private static bool ShouldRunRoomEnteredHook(AbstractModel model)
    {
        if (!ForkedRoadManager.IsSplitBatchInProgress)
        {
            return true;
        }

        try
        {
            return model switch
            {
                CardModel card => card.Owner != null && ForkedRoadManager.IsPlayerActive(card.Owner),
                RelicModel relic => relic.Owner != null && ForkedRoadManager.IsPlayerActive(relic.Owner),
                PotionModel potion => potion.Owner != null && ForkedRoadManager.IsPlayerActive(potion.Owner),
                _ => true
            };
        }
        catch
        {
            return true;
        }
    }

    private static async Task AfterRoomEnteredFilteredAsync(IRunState runState, AbstractRoom room)
    {
        foreach (AbstractModel model in runState.IterateHookListeners(null))
        {
            if (!ShouldRunRoomEnteredHook(model))
            {
                continue;
            }

            await model.AfterRoomEntered(room);
            model.InvokeExecutionFinished();
        }
    }

    internal static async Task ShowSpectatorMerchantAsync(IRunState runState)
    {
        List<Player> activePlayers = ForkedRoadManager.GetActivePlayers(runState).ToList();
        Player? perspective = ForkedRoadManager.GetPerspectivePlayer(activePlayers);
        if (perspective == null)
        {
            MerchantLog.Warn("ShowSpectatorMerchantAsync aborted because no perspective player was found.");
            return;
        }

        MerchantLog.Info($"Creating mirrored merchant scene for spectator. Perspective player: {perspective.NetId} activePlayers={string.Join(",", activePlayers.Select(static p => p.NetId))}");
        MerchantRoom room = new MerchantRoom();
        MerchantInventoryRef(room) = MegaCrit.Sts2.Core.Entities.Merchant.MerchantInventory.CreateForNormalMerchant(perspective);
        await MegaCrit.Sts2.Core.Assets.PreloadManager.LoadRoomMerchantAssets();
        NMapScreen.Instance?.Close(animateOut: false);
        NRun.Instance?.SetCurrentRoom(NMerchantRoom.Create(room, runState.Players));
    }

    internal static async Task ShowSpectatorTreasureAsync(IRunState runState)
    {
        await MegaCrit.Sts2.Core.Assets.PreloadManager.LoadRoomTreasureAssets(runState.Act);
        TreasureRoom room = new TreasureRoom(runState.CurrentActIndex);
        NMapScreen.Instance?.Close(animateOut: false);
        NRun.Instance?.SetCurrentRoom(NTreasureRoom.Create(room, runState));
    }

    internal static RunState? GetRunManagerState(RunManager runManager)
    {
        return RunManagerStateRef(runManager);
    }

    internal static void RestoreLocalRunLocation(RunState runState)
    {
        MapCoord? localCoord = ForkedRoadManager.GetLocalPlayerCoord(runState);
        if (!localCoord.HasValue)
        {
            return;
        }

        Log.Info($"ForkedRoad restoring local run location to {localCoord.Value} after split batch.");

        List<MapCoord> visitedCoords = RunStateVisitedCoordsRef(runState);
        visitedCoords.Clear();
        IReadOnlyList<MapCoord> localHistory = ForkedRoadManager.GetPlayerVisitedCoords(LocalContext.NetId ?? 0, runState);
        if (localHistory.Count > 0)
        {
            visitedCoords.AddRange(localHistory);
        }
        else
        {
            visitedCoords.Add(localCoord.Value);
        }

        Log.Info($"ForkedRoad visited coords after restore: {string.Join(" -> ", visitedCoords)}");

        foreach (MapCoord coord in ForkedRoadManager.GetKnownBranchCoords(runState))
        {
            RunManager.Instance.RunLocationTargetedBuffer.OnRunLocationChanged(new RunLocation(coord, runState.CurrentActIndex));
        }

        RunLocation restoredLocation = new RunLocation(localCoord, runState.CurrentActIndex);
        RunManager.Instance.MapSelectionSynchronizer.OnRunLocationChanged(restoredLocation);
        RunManager.Instance.RunLocationTargetedBuffer.OnRunLocationChanged(restoredLocation);
    }

    private static void EnsureTreasureSpectatorCollectionReady()
    {
        if (!ForkedRoadManager.IsSplitBatchInProgress || !ForkedRoadManager.IsSpectatingBranch())
        {
            return;
        }

        NTreasureRoom? treasureRoom = NRun.Instance?.TreasureRoom;
        if (treasureRoom == null)
        {
            return;
        }

        var collection = (MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic.NTreasureRoomRelicCollection)NTreasureRoomRelicCollectionRef(treasureRoom);
        if (!collection.Visible)
        {
            collection.InitializeRelics();
            collection.Visible = true;
            collection.SetSelectionEnabled(isEnabled: false);
            NTreasureRoomIsRelicCollectionOpenRef(treasureRoom) = true;
            ActiveScreenContext.Instance.Update();
        }

        if (TreasureRelicPickingTcsRef(collection) == null)
        {
            TreasureRelicPickingTcsRef(collection) = new TaskCompletionSource();
        }
    }

    private static NTreasureRoom? GetTreasureRoomForCollection(MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic.NTreasureRoomRelicCollection collection)
    {
        Node? current = collection;
        while (current != null)
        {
            if (current is NTreasureRoom treasureRoom)
            {
                return treasureRoom;
            }
            current = current.GetParent();
        }
        return null;
    }

    private static MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic.NTreasureRoomRelicHolder? FindTreasureHolder(
        List<MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic.NTreasureRoomRelicHolder> holders,
        MegaCrit.Sts2.Core.Entities.TreasureRelicPicking.RelicPickingResult result,
        int fallbackIndex)
    {
        MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic.NTreasureRoomRelicHolder? holder = holders.FirstOrDefault(
            h => ReferenceEquals(h.Relic.Model, result.relic));
        if (holder != null)
        {
            return holder;
        }

        holder = holders.FirstOrDefault(h => h.Relic.Model?.Id == result.relic?.Id);
        if (holder != null)
        {
            return holder;
        }

        if (fallbackIndex >= 0 && fallbackIndex < holders.Count)
        {
            return holders[fallbackIndex];
        }

        return null;
    }

    private static async Task SafeAnimateTreasureAwardsAsync(
        MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic.NTreasureRoomRelicCollection collection,
        List<MegaCrit.Sts2.Core.Entities.TreasureRelicPicking.RelicPickingResult> results)
    {
        try
        {
            List<MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic.NTreasureRoomRelicHolder> holders = TreasureHoldersInUseRef(collection);
            if (holders.Count == 0)
            {
                TreasureLog.Info("Treasure awards initializing holders lazily before animating.");
                collection.InitializeRelics();
                holders = TreasureHoldersInUseRef(collection);
            }

            IRunState runState = TreasureRelicCollectionRunStateRef(collection);
            for (int i = 0; i < holders.Count; i++)
            {
                holders[i].SetFocusMode(Control.FocusModeEnum.None);
            }

            TreasureLog.Info($"Treasure awards animating results. holders={holders.Count} results={results.Count}");
            for (int i = 0; i < results.Count; i++)
            {
                var result = results[i];
                var holder = FindTreasureHolder(holders, result, i);
                if (holder == null)
                {
                    TreasureLog.Warn($"Treasure award holder not found for relic {result.relic?.Id}; fallback index={i}");
                    continue;
                }

                holder.AnimateAwayVotes();

                RelicModel relic = result.relic.ToMutable();
                TaskHelper.RunSafely(MegaCrit.Sts2.Core.Commands.RelicCmd.Obtain(relic, result.player));

                if (LocalContext.IsMe(result.player))
                {
                    NRun.Instance?.GlobalUi?.RelicInventory?.AnimateRelic(relic, holder.GlobalPosition, holder.Scale);
                }

                if (runState.Players.Count == 1)
                {
                    holder.Visible = false;
                }

                foreach (Player player in result.player.RunState.Players)
                {
                    if (player != result.player)
                    {
                        player.RelicGrabBag.MoveToFallback(result.relic);
                    }
                }

                await Task.Yield();
            }
        }
        finally
        {
            if (TreasureRelicPickingTcsRef(collection) == null)
            {
                TreasureRelicPickingTcsRef(collection) = new TaskCompletionSource();
            }
            TreasureRelicPickingTcsRef(collection)!.TrySetResult();
            TreasureLog.Info("Treasure awards flow completed; relic picking task resolved.");

            if (ForkedRoadManager.IsSplitBatchInProgress && !ForkedRoadManager.IsSpectatingBranch())
            {
                FinalizeTreasureRoomAfterAwards(collection);
            }
            else if (ForkedRoadManager.IsSplitBatchInProgress && ForkedRoadManager.IsSpectatingBranch())
            {
                TreasureLog.Info("Treasure spectator flow completed; notifying branch advance readiness.");
                ForkedRoadManager.NotifyLocalTerminalProceed();
            }
        }
    }

    private static void FinalizeTreasureRoomAfterAwards(MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic.NTreasureRoomRelicCollection collection)
    {
        NTreasureRoom? treasureRoom = GetTreasureRoomForCollection(collection);
        if (treasureRoom == null)
        {
            TreasureLog.Warn("Could not find treasure room for collection while finalizing awards.");
            return;
        }

        if (NTreasureRoomHasRelicBeenClaimedRef(treasureRoom))
        {
            return;
        }

        TreasureLog.Info("Finalizing treasure room after relic awards; enabling proceed.");
        NTreasureRoomIsRelicCollectionOpenRef(treasureRoom) = false;
        NTreasureRoomHasRelicBeenClaimedRef(treasureRoom) = true;
        NTreasureRoomBannerRef(treasureRoom)?.AnimateOut();
        NMapScreen.Instance?.SetTravelEnabled(enabled: true);
        NTreasureRoomProceedButtonRef(treasureRoom)?.Enable();
        collection.AnimOut(NTreasureRoomChestNodeRef(treasureRoom));
        ActiveScreenContext.Instance.Update();
    }

    internal static void InvokeSharedMoveToMapCoord(MapSelectionSynchronizer synchronizer)
    {
        MapSelectionMoveToMapCoordMethod.Invoke(synchronizer, Array.Empty<object?>());
    }

    private static bool IsForkedRoadMultiplayer(IRunState? runState)
    {
        return runState is RunState state && state.Players.Count > 1;
    }

    private static bool IsLocalCombatSpectator(CombatState? combatState)
    {
        return combatState != null && ForkedRoadManager.IsSplitBatchInProgress && !ForkedRoadManager.IsLocalPlayerActiveInCurrentBranch && combatState.Players.Count > 0;
    }

    private static bool ShouldResolveSupportBranchDeath(CombatState? combatState)
    {
        return ForkedRoadManager.IsSplitBatchInProgress &&
               ForkedRoadManager.IsSupportBranchActive &&
               combatState != null &&
               combatState.Players.Count > 0 &&
               combatState.Players.All(static player => player.Creature.IsDead);
    }

    private static async Task<bool> ResolveSupportBranchDeathAsync(CombatManager manager, CombatState combatState)
    {
        IRunState runState = combatState.RunState;
        CombatRoom room = (CombatRoom)runState.CurrentRoom!;
        Log.Info($"ForkedRoad resolving branch death for room {room.RoomType}; merging players into another branch.");
        int branchSequence = ForkedRoadManager.ActiveBranchSequence;

        SetPrivateProperty(manager, nameof(CombatManager.IsInProgress), false);
        SetPrivateProperty(manager, nameof(CombatManager.IsPlayPhase), false);
        SetPrivateProperty(manager, nameof(CombatManager.PlayerActionsDisabled), false);
        CombatManagerPlayersTakingExtraTurnRef(manager).Clear();

        foreach (Player player in combatState.Players)
        {
            ForkedRoadManager.MarkPlayerForBranchMerge(player);
        }

        ForkedRoadManager.BroadcastCurrentBranchMergeResolved();
        if (!ForkedRoadManager.TryBeginCurrentBranchMergeCleanup())
        {
            return true;
        }

        try
        {
            await Hook.AfterCombatEnd(runState, combatState, room);
            manager.History.Clear();
            room.OnCombatEnded();

            foreach (Player player in combatState.Players)
            {
                player.AfterCombatEnd();
            }

            MegaCrit.Sts2.Core.Nodes.HoverTips.NHoverTipSet.Clear();
            if (runState.CurrentMapPointHistoryEntry != null)
            {
                runState.CurrentMapPointHistoryEntry.Rooms.Last().TurnsTaken = combatState.RoundNumber;
            }

            room.MarkPreFinished();
            await SaveManager.Instance.SaveRun(room, saveProgress: false);
            NMapScreen.Instance?.SetTravelEnabled(enabled: true);
            combatState.MultiplayerScalingModel?.OnCombatFinished();
            RunManager.Instance.ActionExecutor.Unpause();
            RunManager.Instance.ActionQueueSynchronizer.SetCombatState(ActionSynchronizerCombatState.NotInCombat);
            NRunMusicController.Instance?.UpdateTrack();
            (CombatManagerCombatEndedField.GetValue(manager) as Action<CombatRoom>)?.Invoke(room);

            foreach (Player player in combatState.Players)
            {
                if (player.Creature.IsDead)
                {
                    // ForkedRoad: revive after the old combat is over so merged players can participate in the next branch.
                    player.ActivateHooks();
                    await player.ReviveBeforeCombatEnd();
                }
            }

            await TransitionMergedBranchToMapAsync((RunState)runState);
            ForkedRoadManager.NotifyLocalTerminalProceed();
        }
        finally
        {
            ForkedRoadManager.CompleteBranchMergeCleanup(branchSequence);
        }
        return true;
    }

    private static void SetPrivateProperty(object instance, string propertyName, object? value)
    {
        MethodInfo? setter = AccessTools.PropertySetter(instance.GetType(), propertyName);
        setter?.Invoke(instance, new[] { value });
    }

    private static async Task ExitTerminalRoomToMapAsync(string source)
    {
        RunManager runManager = RunManager.Instance;
        RunState? runState = GetRunManagerState(runManager);
        if (runState == null)
        {
            return;
        }

        Log.Info($"ForkedRoad exiting terminal room via {source}. currentRoomCount={runState.CurrentRoomCount} split={ForkedRoadManager.IsSplitBatchInProgress}");
        if (runState.CurrentRoomCount > 0)
        {
            Task<AbstractRoom?> exitTask = (Task<AbstractRoom?>)RunManagerExitCurrentRoomMethod.Invoke(runManager, Array.Empty<object?>())!;
            await exitTask;
        }

        NRun.Instance?.SetCurrentRoom(NMapRoom.Create(runState.Act, runState.CurrentActIndex));
        if (runState.CurrentRoom != null)
        {
            NRunMusicController.Instance?.UpdateTrack();
        }
        NRunMusicController.Instance?.UpdateAmbience();
        NMapScreen.Instance?.SetTravelEnabled(enabled: true);
        NMapScreen.Instance?.Open();
        ForkedRoadManager.NotifyLocalTerminalProceed();
    }

    internal static async Task TransitionMergedBranchToMapAsync(RunState runState)
    {
        RunManager runManager = RunManager.Instance;
        // ForkedRoad: fully unwind nested event/combat rooms before returning the merged player to map flow.
        runManager.ActionExecutor.Cancel();
        // Do not reset ActionQueueSet here. Reset() clears the per-player queue table entirely,
        // and the next combat may start without recreating those local owner queues, which causes
        // "Tried to get local action queue for nonexistent player" as soon as anyone plays a card
        // or ends turn after a death merge.
        runManager.ActionQueueSynchronizer.SetCombatState(ActionSynchronizerCombatState.NotInCombat);
        CombatManager.Instance.Reset(graceful: true);

        while (runState.CurrentRoomCount > 0)
        {
            Task<AbstractRoom?> exitTask = (Task<AbstractRoom?>)RunManagerExitCurrentRoomMethod.Invoke(runManager, Array.Empty<object?>())!;
            await exitTask;
        }

        NRun.Instance?.SetCurrentRoom(NMapRoom.Create(runState.Act, runState.CurrentActIndex));
        NMapScreen.Instance?.SetTravelEnabled(enabled: true);
        if (runState.CurrentRoom != null)
        {
            NRunMusicController.Instance?.UpdateTrack();
        }
        NRunMusicController.Instance?.UpdateAmbience();
    }

    [HarmonyPatch(typeof(RunManager), "InitializeShared")]
    private static class RunManager_InitializeShared_Patch
    {
        private static void Postfix(RunManager __instance, INetGameService netService)
        {
            ForkedRoadManager.InitializeForRun(GetRunManagerState(__instance), netService);
        }
    }

    [HarmonyPatch(typeof(RunManager), nameof(RunManager.CleanUp))]
    private static class RunManager_CleanUp_Patch
    {
        private static void Postfix()
        {
            ForkedRoadManager.Reset();
        }
    }

    [HarmonyPatch(typeof(RunManager), nameof(RunManager.EnterMapCoord))]
    private static class RunManager_EnterMapCoord_Patch
    {
        private static void Prefix(RunManager __instance, MapCoord coord)
        {
            ForkedRoadManager.BeforeEnterMapCoord(__instance, coord);
        }
    }

    [HarmonyPatch(typeof(RunState), nameof(RunState.AppendToMapPointHistory))]
    private static class RunState_AppendToMapPointHistory_Patch
    {
        private static bool Prefix(RunState __instance, MapPointType mapPointType, RoomType initialRoomType, ModelId? roomModelId)
        {
            if (!ForkedRoadManager.ShouldReuseExistingFloorHistory(__instance))
            {
                return true;
            }

            ForkedRoadManager.AppendRoomToExistingFloorHistory(__instance, initialRoomType, roomModelId);
            return false;
        }
    }

    [HarmonyPatch(typeof(MapRoom), nameof(MapRoom.Enter))]
    private static class MapRoom_Enter_Patch
    {
        private static void Postfix(IRunState? runState)
        {
            TaskHelper.RunSafely(ForkedRoadManager.HandleMapRoomEnteredAsync(runState as RunState));
        }
    }

    [HarmonyPatch(typeof(Hook), nameof(Hook.AfterRoomEntered))]
    private static class Hook_AfterRoomEntered_Patch
    {
        private static bool Prefix(IRunState runState, AbstractRoom room, ref Task __result)
        {
            if (!ForkedRoadManager.IsSplitBatchInProgress)
            {
                return true;
            }

            __result = AfterRoomEnteredFilteredAsync(runState, room);
            return false;
        }
    }

    [HarmonyPatch(typeof(Hook), nameof(Hook.ShouldGainStars))]
    private static class Hook_ShouldGainStars_Patch
    {
        private static bool Prefix(CombatState? combatState, decimal amount, Player? player, ref bool __result)
        {
            if (!ForkedRoadManager.IsSplitBatchInProgress)
            {
                return true;
            }

            if (combatState == null || player == null || player.PlayerCombatState == null || player.Creature?.CombatState != combatState || !ForkedRoadManager.IsPlayerActive(player))
            {
                __result = false;
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(MapSplitVoteAnimation), nameof(MapSplitVoteAnimation.TryPlay))]
    private static class MapSplitVoteAnimation_TryPlay_Patch
    {
        private static bool Prefix(ref Task __result)
        {
            if (!ForkedRoadManager.IsSplitBatchInProgress)
            {
                return true;
            }

            __result = Task.CompletedTask;
            return false;
        }
    }

    [HarmonyPatch(typeof(MapSelectionSynchronizer), nameof(MapSelectionSynchronizer.PlayerVotedForMapCoord))]
    private static class MapSelectionSynchronizer_PlayerVoted_Patch
    {
        private static bool Prefix(MapSelectionSynchronizer __instance, Player player, RunLocation source, MapVote? destination)
        {
            RunState runState = MapSelectionRunStateRef(__instance);
            if (!IsForkedRoadMultiplayer(runState))
            {
                return true;
            }
            return !ForkedRoadManager.TryHandleVote(__instance, player, source, destination);
        }
    }

    [HarmonyPatch(typeof(NMapScreen), "RecalculateTravelability")]
    private static class NMapScreen_RecalculateTravelability_Patch
    {
        private static bool Prefix(NMapScreen __instance)
        {
            RunState runState = MapScreenRunStateRef(__instance);
            if (!IsForkedRoadMultiplayer(runState))
            {
                return true;
            }

            ForkedRoadManager.InitializeForRun(runState, RunManager.Instance.NetService);

            Dictionary<MapCoord, NMapPoint> pointDictionary = MapScreenPointDictionaryRef(__instance);
            foreach (NMapPoint point in pointDictionary.Values)
            {
                point.State = MapPointState.Untravelable;
            }

            foreach (MapCoord visitedMapCoord in runState.VisitedMapCoords)
            {
                if (pointDictionary.TryGetValue(visitedMapCoord, out NMapPoint? point))
                {
                    point.State = MapPointState.Traveled;
                }
            }

            MapCoord? localCoord = ForkedRoadManager.GetLocalPlayerCoord(runState);
            if (!localCoord.HasValue)
            {
                MapScreenStartingPointRef(__instance).State = MapPointState.Travelable;
                return false;
            }

            NBossMapPoint bossPoint = MapScreenBossPointRef(__instance);
            NBossMapPoint? secondBossPoint = MapScreenSecondBossPointRef(__instance);
            if (secondBossPoint != null && localCoord.Value == bossPoint.Point.coord)
            {
                secondBossPoint.State = MapPointState.Travelable;
                return false;
            }

            ActMap map = MapScreenMapRef(__instance);
            if (localCoord.Value.row != map.GetRowCount() - 1)
            {
                IEnumerable<MapPoint> nextPoints = runState.Modifiers.OfType<MegaCrit.Sts2.Core.Models.Modifiers.Flight>().Any()
                    ? map.GetPointsInRow(localCoord.Value.row + 1)
                    : pointDictionary[localCoord.Value].Point.Children;
                foreach (MapPoint nextPoint in nextPoints)
                {
                    pointDictionary[nextPoint.coord].State = MapPointState.Travelable;
                }
                return false;
            }

            bossPoint.State = MapPointState.Travelable;
            return false;
        }
    }

    [HarmonyPatch(typeof(NMapScreen), nameof(NMapScreen.Open))]
    private static class NMapScreen_Open_Patch
    {
        private static void Postfix(NMapScreen __instance)
        {
            RunState runState = MapScreenRunStateRef(__instance);
            if (!IsForkedRoadMultiplayer(runState))
            {
                return;
            }

            MapCoord? localCoord = ForkedRoadManager.GetLocalPlayerCoord(runState);
            if (!localCoord.HasValue)
            {
                return;
            }

            Dictionary<MapCoord, NMapPoint> pointDictionary = MapScreenPointDictionaryRef(__instance);
            if (!pointDictionary.TryGetValue(localCoord.Value, out NMapPoint? point))
            {
                return;
            }

            if (MapScreenBossPointRef(__instance).Point.coord.row != localCoord.Value.row && MapScreenStartingPointRef(__instance).Point.coord.row != localCoord.Value.row)
            {
                MapScreenMarkerRef(__instance).SetMapPoint(point);
            }

            float distY = MapScreenDistYRef(__instance);
            Vector2 localMapPos = new Vector2(0f, -600f + (float)localCoord.Value.row * distY);
            MapScreenTargetDragPosRef(__instance) = localMapPos;
            MapScreenMapContainerRef(__instance).Position = localMapPos;

        }
    }

    [HarmonyPatch(typeof(NMapPoint), "ShouldDisplayPlayerVote")]
    private static class NMapPoint_ShouldDisplayPlayerVote_Patch
    {
        private static bool Prefix(NMapPoint __instance, Player player, ref bool __result)
        {
            IRunState runState = MapPointRunStateRef(__instance);
            if (!IsForkedRoadMultiplayer(runState))
            {
                return true;
            }

            NMapScreen screen = MapPointScreenRef(__instance);
            if (screen.PlayerVoteDictionary.TryGetValue(player, out MapCoord? voteCoord) && voteCoord.HasValue)
            {
                __result = voteCoord.Value == __instance.Point.coord;
                return false;
            }

            __result = ForkedRoadManager.GetPlayerCoord(player) == __instance.Point.coord;
            return false;
        }
    }

    [HarmonyPatch(typeof(NMapScreen), "OnPlayerVoteChanged")]
    private static class NMapScreen_OnPlayerVoteChanged_Patch
    {
        private static bool Prefix(NMapScreen __instance, Player player, MapVote? oldLocation, MapVote? newLocation)
        {
            RunState runState = MapScreenRunStateRef(__instance);
            if (!IsForkedRoadMultiplayer(runState))
            {
                return true;
            }

            if (!LocalContext.IsMe(player))
            {
                MapCoord? oldCoord = oldLocation?.coord ?? ForkedRoadManager.GetPlayerCoord(player);
                NMapScreenOnPlayerVoteChangedInternalMethod.Invoke(__instance, new object?[] { player, oldCoord, newLocation?.coord });
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(NMapScreen), nameof(NMapScreen.OnMapPointSelectedLocally))]
    private static class NMapScreen_OnMapPointSelectedLocally_Patch
    {
        private static bool Prefix(NMapScreen __instance, NMapPoint point)
        {
            RunState runState = MapScreenRunStateRef(__instance);
            if (!IsForkedRoadMultiplayer(runState))
            {
                return true;
            }

            if (ForkedRoadManager.ShouldSuppressManualMapSelection(runState))
            {
                return false;
            }

            Player? me = LocalContext.GetMe(runState);
            if (me == null)
            {
                return false;
            }

            MapCoord? currentVote = RunManager.Instance.MapSelectionSynchronizer.GetVote(me)?.coord;
            if (!__instance.PlayerVoteDictionary.TryGetValue(me, out MapCoord? displayedVote) || displayedVote != point.Point.coord)
            {
                MapCoord? oldCoord = currentVote ?? ForkedRoadManager.GetPlayerCoord(me);
                NMapScreenOnPlayerVoteChangedInternalMethod.Invoke(__instance, new object?[] { me, oldCoord, point.Point.coord });

                RunLocation source = new RunLocation(ForkedRoadManager.GetPlayerCoord(me), runState.CurrentActIndex);
                MapVote vote = new MapVote
                {
                    coord = point.Point.coord,
                    mapGenerationCount = RunManager.Instance.MapSelectionSynchronizer.MapGenerationCount
                };
                RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(new VoteForMapCoordAction(me, source, vote));
            }
            else if (runState.Players.Count > 1)
            {
                RunManager.Instance.FlavorSynchronizer.SendMapPing(point.Point.coord);
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(RunManager), nameof(RunManager.ProceedFromTerminalRewardsScreen))]
    private static class RunManager_ProceedFromTerminalRewardsScreen_Patch
    {
        private static void Postfix()
        {
            ForkedRoadManager.NotifyLocalTerminalProceed();
        }
    }

    [HarmonyPatch(typeof(SaveManager), nameof(SaveManager.SaveProgressFile))]
    private static class SaveManager_SaveProgressFile_Patch
    {
        private static bool Prefix(SaveManager __instance)
        {
            SaveProgressWithRetry(SaveManagerProgressManagerRef(__instance));
            return false;
        }
    }

    [HarmonyPatch(typeof(RunSaveManager), nameof(RunSaveManager.SaveRun))]
    private static class RunSaveManager_SaveRun_Patch
    {
        private static bool Prefix(RunSaveManager __instance, AbstractRoom? preFinishedRoom, ref Task __result)
        {
            __result = SaveRunWithRetryAsync(__instance, preFinishedRoom);
            return false;
        }
    }

    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Commands.RewardsCmd), nameof(MegaCrit.Sts2.Core.Commands.RewardsCmd.OfferForRoomEnd))]
    private static class RewardsCmd_OfferForRoomEnd_Patch
    {
        private static bool Prefix(Player player, AbstractRoom room, ref Task __result)
        {
            if (!ForkedRoadManager.IsSplitBatchInProgress)
            {
                return true;
            }

            if (!ForkedRoadManager.ShouldSkipRoomEndRewards(player))
            {
                return true;
            }

            Log.Info($"ForkedRoad skipping room-end rewards for player {player.NetId} in room {room.RoomType} after branch death merge.");
            __result = Task.CompletedTask;
            return false;
        }
    }

    [HarmonyPatch(typeof(EventRoom), nameof(EventRoom.Exit))]
    private static class EventRoom_Exit_Patch
    {
        private static bool Prefix(EventRoom __instance, IRunState? runState, ref Task __result)
        {
            if (!ForkedRoadManager.IsSplitBatchInProgress)
            {
                return true;
            }

            Action<EventModel> handler = (Action<EventModel>)Delegate.CreateDelegate(typeof(Action<EventModel>), __instance, "OnEventStateChanged");
            foreach (EventModel @event in RunManager.Instance.EventSynchronizer.Events)
            {
                @event.StateChanged -= handler;
                @event.EnsureCleanup();
            }
            __result = Task.CompletedTask;
            return false;
        }
    }

    [HarmonyPatch(typeof(RestSiteRoom), nameof(RestSiteRoom.Exit))]
    private static class RestSiteRoom_Exit_Patch
    {
        private static bool Prefix(ref Task __result)
        {
            if (!ForkedRoadManager.IsSplitBatchInProgress)
            {
                return true;
            }

            __result = Task.CompletedTask;
            return false;
        }
    }

    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Rooms.NEventRoom), nameof(MegaCrit.Sts2.Core.Nodes.Rooms.NEventRoom.Proceed))]
    private static class NEventRoom_Proceed_Patch
    {
        private static bool Prefix(ref Task __result)
        {
            if (!ForkedRoadManager.IsSplitBatchInProgress || ForkedRoadManager.IsSpectatingBranch())
            {
                return true;
            }

            __result = ExitTerminalRoomToMapAsync("event");
            return false;
        }
    }

    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Rooms.NRestSiteRoom), "OnProceedButtonReleased")]
    private static class NRestSiteRoom_OnProceedButtonReleased_Patch
    {
        private static bool Prefix()
        {
            if (!ForkedRoadManager.IsSplitBatchInProgress || ForkedRoadManager.IsSpectatingBranch())
            {
                return true;
            }

            TaskHelper.RunSafely(ExitTerminalRoomToMapAsync("rest_site"));
            return false;
        }
    }

    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Rooms.NMerchantRoom), "HideScreen")]
    private static class NMerchantRoom_HideScreen_Patch
    {
        private static void Postfix()
        {
            if (!ForkedRoadManager.IsSplitBatchInProgress || ForkedRoadManager.IsSpectatingBranch())
            {
                return;
            }

            if (NMapScreen.Instance?.IsOpen == true)
            {
                MerchantLog.Info("Merchant proceed detected; notifying branch advance.");
                ForkedRoadManager.NotifyLocalTerminalProceed();
            }
        }
    }

    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Entities.Multiplayer.NetFullCombatState), nameof(MegaCrit.Sts2.Core.Entities.Multiplayer.NetFullCombatState.FromRun))]
    private static class NetFullCombatState_FromRun_Patch
    {
        private static bool Prefix(IRunState runState, GameAction? justFinishedAction, ref MegaCrit.Sts2.Core.Entities.Multiplayer.NetFullCombatState __result)
        {
            if (!ForkedRoadManager.IsSplitBatchInProgress)
            {
                return true;
            }

            List<Player> activePlayers = runState.Players.Where(ForkedRoadManager.IsPlayerActive).ToList();
            CombatState? combatState = activePlayers.Select((Player p) => p.Creature.CombatState).FirstOrDefault((CombatState? c) => c != null);
            MegaCrit.Sts2.Core.Entities.Multiplayer.NetFullCombatState state = new MegaCrit.Sts2.Core.Entities.Multiplayer.NetFullCombatState();
            NetFullCombatStateChoiceIdsRef(state) = RunManager.Instance.PlayerChoiceSynchronizer.ChoiceIds.ToList();
            NetFullCombatStateLastHookRef(state) = justFinishedAction is GenericHookGameAction hookAction ? hookAction.HookId : null;
            NetFullCombatStateLastActionRef(state) = justFinishedAction?.Id;
            NetFullCombatStateCreaturesRef(state) = new List<MegaCrit.Sts2.Core.Entities.Multiplayer.NetFullCombatState.CreatureState>();
            NetFullCombatStatePlayersRef(state) = new List<MegaCrit.Sts2.Core.Entities.Multiplayer.NetFullCombatState.PlayerState>();
            NetFullCombatStateRngRef(state) = runState.Rng.ToSerializable();

            foreach (MegaCrit.Sts2.Core.Entities.Creatures.Creature creature in combatState?.Creatures ?? Array.Empty<MegaCrit.Sts2.Core.Entities.Creatures.Creature>())
            {
                MegaCrit.Sts2.Core.Entities.Multiplayer.NetFullCombatState.CreatureState creatureState = new MegaCrit.Sts2.Core.Entities.Multiplayer.NetFullCombatState.CreatureState
                {
                    monsterId = creature.Monster?.Id,
                    playerId = creature.Player?.NetId,
                    currentHp = creature.CurrentHp,
                    maxHp = creature.MaxHp,
                    block = creature.Block,
                    powers = new List<MegaCrit.Sts2.Core.Entities.Multiplayer.NetFullCombatState.PowerState>()
                };
                foreach (PowerModel power in creature.Powers)
                {
                    creatureState.powers.Add(new MegaCrit.Sts2.Core.Entities.Multiplayer.NetFullCombatState.PowerState
                    {
                        id = power.Id,
                        amount = power.Amount
                    });
                }
                NetFullCombatStateCreaturesRef(state).Add(creatureState);
            }

            foreach (Player player in activePlayers)
            {
                MegaCrit.Sts2.Core.Entities.Multiplayer.NetFullCombatState.PlayerState playerState = new MegaCrit.Sts2.Core.Entities.Multiplayer.NetFullCombatState.PlayerState
                {
                    playerId = player.NetId,
                    characterId = player.Character.Id,
                    energy = player.PlayerCombatState?.Energy ?? 0,
                    stars = player.PlayerCombatState?.Stars ?? 0,
                    maxStars = 0,
                    maxPotionCount = player.MaxPotionCount,
                    gold = player.Gold,
                    piles = new List<MegaCrit.Sts2.Core.Entities.Multiplayer.NetFullCombatState.CombatPileState>(),
                    potions = new List<MegaCrit.Sts2.Core.Entities.Multiplayer.NetFullCombatState.PotionState>(),
                    relics = new List<MegaCrit.Sts2.Core.Entities.Multiplayer.NetFullCombatState.RelicState>(),
                    orbs = new List<MegaCrit.Sts2.Core.Entities.Multiplayer.NetFullCombatState.OrbState>(),
                    rngSet = player.PlayerRng.ToSerializable(),
                    oddsSet = player.PlayerOdds.ToSerializable(),
                    relicGrabBag = player.RelicGrabBag.ToSerializable()
                };
                if (player.PlayerCombatState != null && player.Creature.CombatState == combatState && CombatManager.Instance.IsInProgress)
                {
                    playerState.piles.Add(MegaCrit.Sts2.Core.Entities.Multiplayer.NetFullCombatState.CombatPileState.From(player.PlayerCombatState.Hand));
                    playerState.piles.Add(MegaCrit.Sts2.Core.Entities.Multiplayer.NetFullCombatState.CombatPileState.From(player.PlayerCombatState.DrawPile));
                    playerState.piles.Add(MegaCrit.Sts2.Core.Entities.Multiplayer.NetFullCombatState.CombatPileState.From(player.PlayerCombatState.DiscardPile));
                    playerState.piles.Add(MegaCrit.Sts2.Core.Entities.Multiplayer.NetFullCombatState.CombatPileState.From(player.PlayerCombatState.ExhaustPile));
                    playerState.piles.Add(MegaCrit.Sts2.Core.Entities.Multiplayer.NetFullCombatState.CombatPileState.From(player.PlayerCombatState.PlayPile));
                    playerState.orbs.AddRange(player.PlayerCombatState.OrbQueue.Orbs.Select(MegaCrit.Sts2.Core.Entities.Multiplayer.NetFullCombatState.OrbState.From));
                }
                foreach (PotionModel potion in player.Potions)
                {
                    playerState.potions.Add(new MegaCrit.Sts2.Core.Entities.Multiplayer.NetFullCombatState.PotionState
                    {
                        id = potion.Id
                    });
                }
                foreach (RelicModel relic in player.Relics)
                {
                    playerState.relics.Add(new MegaCrit.Sts2.Core.Entities.Multiplayer.NetFullCombatState.RelicState
                    {
                        relic = relic.ToSerializable()
                    });
                }
                NetFullCombatStatePlayersRef(state).Add(playerState);
            }

            __result = state;
            return false;
        }
    }

    [HarmonyPatch(typeof(Hook), nameof(Hook.ModifyOrbValue))]
    private static class Hook_ModifyOrbValue_Patch
    {
        private static bool Prefix(CombatState combatState, Player player, decimal amount, ref decimal __result)
        {
            if (!ForkedRoadManager.IsSplitBatchInProgress)
            {
                return true;
            }

            if (combatState == null || player == null || player.PlayerCombatState == null || player.Creature?.CombatState != combatState || !ForkedRoadManager.IsPlayerActive(player))
            {
                __result = amount;
                return false;
            }

            decimal result = amount;
            foreach (AbstractModel model in combatState.IterateHookListeners())
            {
                try
                {
                    result = model.ModifyOrbValue(player, result);
                }
                catch
                {
                    // Ignore spectator-side orb modifier faults during branch splits.
                }
            }
            __result = result;
            return false;
        }
    }

    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Entities.Multiplayer.NetFullCombatState.OrbState), nameof(MegaCrit.Sts2.Core.Entities.Multiplayer.NetFullCombatState.OrbState.From))]
    private static class NetFullCombatState_OrbState_From_Patch
    {
        private static bool Prefix(OrbModel orb, ref MegaCrit.Sts2.Core.Entities.Multiplayer.NetFullCombatState.OrbState __result)
        {
            if (!ForkedRoadManager.IsSplitBatchInProgress)
            {
                return true;
            }

            try
            {
                if (orb?.Owner == null || orb.Owner.PlayerCombatState == null || !ForkedRoadManager.IsPlayerActive(orb.Owner))
                {
                    __result = new MegaCrit.Sts2.Core.Entities.Multiplayer.NetFullCombatState.OrbState
                    {
                        id = orb.Id,
                        passive = 0,
                        evoke = 0
                    };
                    return false;
                }
            }
            catch
            {
                __result = default;
                return false;
            }

            return true;
        }
    }

    [HarmonyPatch(typeof(CombatRoom), nameof(CombatRoom.Enter))]
    private static class CombatRoom_Enter_Patch
    {
        private static void Prefix(CombatRoom __instance, IRunState? runState)
        {
            if (!ForkedRoadManager.IsSplitBatchInProgress || runState == null || __instance.CombatState.Players.Count > 0)
            {
                return;
            }

            foreach (Player player in ForkedRoadManager.GetActivePlayers(runState))
            {
                __instance.CombatState.AddPlayer(player);
            }
        }
    }

    [HarmonyPatch(typeof(MerchantRoom), nameof(MerchantRoom.Enter))]
    private static class MerchantRoom_Enter_Patch
    {
        private static bool Prefix(MerchantRoom __instance, IRunState? runState, bool isRestoringRoomStackBase, ref Task __result)
        {
            if (!ForkedRoadManager.IsSplitBatchInProgress || !ForkedRoadManager.IsSpectatingBranch())
            {
                return true;
            }

            __result = EnterSpectatorMerchantBranchAsync(__instance, runState, isRestoringRoomStackBase);
            return false;
        }

        private static async Task EnterSpectatorMerchantBranchAsync(MerchantRoom room, IRunState? runState, bool isRestoringRoomStackBase)
        {
            if (isRestoringRoomStackBase)
            {
                throw new InvalidOperationException("MerchantRoom does not support room stack reconstruction.");
            }
            if (runState == null)
            {
                MerchantLog.Warn("Spectator merchant enter aborted because runState was null.");
                return;
            }

            List<Player> activePlayers = ForkedRoadManager.GetActivePlayers(runState).ToList();
            Player? perspective = ForkedRoadManager.GetPerspectivePlayer(activePlayers);
            if (perspective == null)
            {
                MerchantLog.Warn("Spectator merchant enter aborted because no active perspective player was found.");
                return;
            }

            MerchantLog.Info($"Spectator merchant enter building mirrored shop from player {perspective.NetId}.");
            MerchantInventoryRef(room) = MegaCrit.Sts2.Core.Entities.Merchant.MerchantInventory.CreateForNormalMerchant(perspective);
            await MegaCrit.Sts2.Core.Assets.PreloadManager.LoadRoomMerchantAssets();
            NRun.Instance?.SetCurrentRoom(NMerchantRoom.Create(room, runState.Players));
            await Hook.AfterRoomEntered(runState, room);
        }
    }

    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Rooms.NMerchantRoom), "AfterRoomIsLoaded")]
    private static class NMerchantRoom_AfterRoomIsLoaded_Patch
    {
        private static bool Prefix(MegaCrit.Sts2.Core.Nodes.Rooms.NMerchantRoom __instance)
        {
            if (!ForkedRoadManager.IsSplitBatchInProgress || !ForkedRoadManager.IsSpectatingBranch())
            {
                return true;
            }

            List<Player> renderPlayers = NMerchantRoomPlayersRef(__instance)
                .Where(ForkedRoadManager.IsPlayerActive)
                .ToList();

            if (renderPlayers.Count == 0)
            {
                MerchantLog.Warn("Spectator merchant room had no active render players; suppressing room visuals.");
                return false;
            }

            Player? perspective = ForkedRoadManager.GetPerspectivePlayer(renderPlayers);
            if (perspective != null)
            {
                renderPlayers.RemoveAll((Player p) => p.NetId == perspective.NetId);
                renderPlayers.Insert(0, perspective);
            }

            List<Player> players = NMerchantRoomPlayersRef(__instance);
            players.Clear();
            players.AddRange(renderPlayers);
            MerchantLog.Info($"Spectator merchant visuals rebuilding for players: {string.Join(",", renderPlayers.Select(static p => p.NetId))}");

            Control characterContainer = NMerchantRoomCharacterContainerRef(__instance);
            List<NMerchantCharacter> playerVisuals = NMerchantRoomPlayerVisualsRef(__instance);
            playerVisuals.Clear();

            int sideLength = Mathf.CeilToInt(Mathf.Sqrt(renderPlayers.Count));
            for (int row = 0; row < sideLength; row++)
            {
                float x = -140f * row;
                for (int col = 0; col < sideLength; col++)
                {
                    int index = row * sideLength + col;
                    if (index >= renderPlayers.Count)
                    {
                        break;
                    }

                    NMerchantCharacter visual = MegaCrit.Sts2.Core.Assets.PreloadManager.Cache
                        .GetScene(renderPlayers[index].Character.MerchantAnimPath)
                        .Instantiate<NMerchantCharacter>(PackedScene.GenEditState.Disabled);
                    characterContainer.AddChild(visual);
                    characterContainer.MoveChild(visual, 0);
                    visual.Position = new Vector2(x, -50f * row);
                    if (row > 0)
                    {
                        visual.Modulate = new Color(0.5f, 0.5f, 0.5f);
                    }

                    x -= 275f;
                    playerVisuals.Add(visual);
                }
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(LocalContext), nameof(LocalContext.GetMe), new[] { typeof(CombatState) })]
    private static class LocalContext_GetMe_CombatState_Patch
    {
        private static bool Prefix(CombatState? combatState, ref Player? __result)
        {
            if (!ForkedRoadManager.IsSplitBatchInProgress || combatState == null || !LocalContext.NetId.HasValue)
            {
                return true;
            }

            Player? player = combatState.GetPlayer(LocalContext.NetId.Value);
            if (player != null)
            {
                __result = player;
                return false;
            }

            __result = null;
            return false;
        }
    }

    [HarmonyPatch(typeof(CombatRoom), "StartCombat")]
    private static class CombatRoom_StartCombat_Patch
    {
        private static bool Prefix(CombatRoom __instance, IRunState? runState, ref Task __result)
        {
            if (!ForkedRoadManager.IsSplitBatchInProgress || ForkedRoadManager.IsLocalPlayerActiveInCurrentBranch)
            {
                return true;
            }

            __result = StartCombatWithoutLocalUiAsync(__instance, runState);
            return false;
        }

        private static async Task StartCombatWithoutLocalUiAsync(CombatRoom room, IRunState? runState)
        {
            if (!room.Encounter.HaveMonstersBeenGenerated)
            {
                room.Encounter.GenerateMonstersWithSlots(room.CombatState.RunState);
            }

            if (room.ShouldCreateCombat)
            {
                await MegaCrit.Sts2.Core.Assets.PreloadManager.LoadRoomCombatAssets(room.Encounter, runState ?? NullRunState.Instance);
            }

            foreach ((MonsterModel monsterModel, string? slot) in room.Encounter.MonstersWithSlots)
            {
                monsterModel.AssertMutable();
                if (room.ShouldCreateCombat)
                {
                    MegaCrit.Sts2.Core.Entities.Creatures.Creature creature = room.CombatState.CreateCreature(monsterModel, CombatSide.Enemy, slot);
                    room.CombatState.AddCreature(creature);
                }
                room.CombatState.RunState.CurrentMapPointHistoryEntry?.Rooms.Last().MonsterIds.Add(monsterModel.Id);
            }

            if (room.ShouldCreateCombat)
            {
                NRun.Instance?.SetCurrentRoom(NCombatRoom.Create(room, CombatRoomMode.ActiveCombat));
            }

            CombatManager.Instance.SetUpCombat(room.CombatState);
            if (runState != null)
            {
                await Hook.AfterRoomEntered(runState, room);
            }
            CombatManager.Instance.AfterCombatRoomLoaded();
        }
    }

    [HarmonyPatch(typeof(NCombatRoom), "OnCombatSetUp")]
    private static class NCombatRoom_OnCombatSetUp_Patch
    {
        private static bool Prefix(NCombatRoom __instance, CombatState state)
        {
            if (!ForkedRoadManager.IsSplitBatchInProgress || ForkedRoadManager.IsLocalPlayerActiveInCurrentBranch)
            {
                return true;
            }

            __instance.SetWaitingForOtherPlayersOverlayVisible(visible: false);
            if (__instance.Background == null)
            {
                __instance.SetUpBackground(state.RunState);
            }
            return false;
        }
    }

    [HarmonyPatch(typeof(NCombatRoom), "OnActiveScreenUpdated")]
    private static class NCombatRoom_OnActiveScreenUpdated_Patch
    {
        private static bool Prefix()
        {
            if (ForkedRoadManager.IsSpectatingBranch())
            {
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(NCombatUi), nameof(NCombatUi.Enable))]
    private static class NCombatUi_Enable_Patch
    {
        private static bool Prefix(NCombatUi __instance)
        {
            if (!ForkedRoadManager.IsSplitBatchInProgress)
            {
                return true;
            }

            CombatState? state = NCombatUiStateRef(__instance);
            if (state == null || LocalContext.GetMe(state) == null)
            {
                return false;
            }
            return true;
        }

        private static Exception? Finalizer(Exception? __exception)
        {
            if (ForkedRoadManager.IsSplitBatchInProgress)
            {
                return null;
            }
            return __exception;
        }
    }

    [HarmonyPatch(typeof(NCombatUi), nameof(NCombatUi.Disable))]
    private static class NCombatUi_Disable_Patch
    {
        private static bool Prefix(NCombatUi __instance)
        {
            if (!ForkedRoadManager.IsSplitBatchInProgress)
            {
                return true;
            }

            CombatState? state = NCombatUiStateRef(__instance);
            if (state == null || LocalContext.GetMe(state) == null)
            {
                return false;
            }
            return true;
        }

        private static Exception? Finalizer(Exception? __exception)
        {
            if (ForkedRoadManager.IsSplitBatchInProgress)
            {
                return null;
            }
            return __exception;
        }
    }

    [HarmonyPatch(typeof(NCombatUi), nameof(NCombatUi.AnimOut))]
    private static class NCombatUi_AnimOut_Patch
    {
        private static bool Prefix(NCombatUi __instance)
        {
            if (!ForkedRoadManager.IsSplitBatchInProgress)
            {
                return true;
            }

            CombatState? state = NCombatUiStateRef(__instance);
            if (state == null || LocalContext.GetMe(state) == null)
            {
                return false;
            }
            return true;
        }

        private static Exception? Finalizer(Exception? __exception)
        {
            if (ForkedRoadManager.IsSplitBatchInProgress)
            {
                return null;
            }
            return __exception;
        }
    }

    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Combat.NEndTurnButton), nameof(MegaCrit.Sts2.Core.Nodes.Combat.NEndTurnButton.Initialize))]
    private static class NEndTurnButton_Initialize_Patch
    {
        private static bool Prefix(MegaCrit.Sts2.Core.Nodes.Combat.NEndTurnButton __instance, CombatState state)
        {
            if (!ForkedRoadManager.IsSplitBatchInProgress)
            {
                return true;
            }

            Player? me = LocalContext.GetMe(state);
            if (me != null)
            {
                return true;
            }

            NEndTurnButtonCombatStateRef(__instance) = state;
            NEndTurnButtonPlayerHandRef(__instance) = null;
            return false;
        }
    }

    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Combat.NEndTurnButton), "PlayerCanTakeAction")]
    private static class NEndTurnButton_PlayerCanTakeAction_Patch
    {
        private static bool Prefix(Player player, ref bool __result)
        {
            if (player != null)
            {
                return true;
            }

            __result = false;
            return false;
        }
    }

    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Combat.NEndTurnButton), "OnTurnStarted")]
    private static class NEndTurnButton_OnTurnStarted_Patch
    {
        private static bool Prefix(CombatState state)
        {
            if (!ForkedRoadManager.IsSplitBatchInProgress)
            {
                return true;
            }

            return LocalContext.GetMe(state) != null;
        }
    }

    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Combat.NEndTurnButton), "StartOrStopPulseVfx")]
    private static class NEndTurnButton_StartOrStopPulseVfx_Patch
    {
        private static bool Prefix(MegaCrit.Sts2.Core.Nodes.Combat.NEndTurnButton __instance)
        {
            if (!ForkedRoadManager.IsSplitBatchInProgress)
            {
                return true;
            }

            CombatState? state = NEndTurnButtonCombatStateRef(__instance);
            return state == null || LocalContext.GetMe(state) != null;
        }
    }

    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Combat.NEndTurnButton), nameof(MegaCrit.Sts2.Core.Nodes.Combat.NEndTurnButton.CallReleaseLogic))]
    private static class NEndTurnButton_CallReleaseLogic_Patch
    {
        private static bool Prefix(MegaCrit.Sts2.Core.Nodes.Combat.NEndTurnButton __instance)
        {
            if (!ForkedRoadManager.IsSplitBatchInProgress)
            {
                return true;
            }

            CombatState? state = NEndTurnButtonCombatStateRef(__instance);
            return state == null || LocalContext.GetMe(state) != null;
        }
    }

    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Combat.NEndTurnButton), "SecretEndTurnLogicViaFtue")]
    private static class NEndTurnButton_SecretEndTurnLogicViaFtue_Patch
    {
        private static bool Prefix(MegaCrit.Sts2.Core.Nodes.Combat.NEndTurnButton __instance)
        {
            if (!ForkedRoadManager.IsSplitBatchInProgress)
            {
                return true;
            }

            CombatState? state = NEndTurnButtonCombatStateRef(__instance);
            return state == null || LocalContext.GetMe(state) != null;
        }
    }

    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Combat.NEndTurnButton), "ShouldShowPlayableCardsFtue")]
    private static class NEndTurnButton_ShouldShowPlayableCardsFtue_Patch
    {
        private static bool Prefix(MegaCrit.Sts2.Core.Nodes.Combat.NEndTurnButton __instance, ref bool __result)
        {
            if (!ForkedRoadManager.IsSplitBatchInProgress)
            {
                return true;
            }

            CombatState? state = NEndTurnButtonCombatStateRef(__instance);
            if (state != null && LocalContext.GetMe(state) == null)
            {
                __result = false;
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Combat.NEndTurnButton), "OnFocus")]
    private static class NEndTurnButton_OnFocus_Patch
    {
        private static bool Prefix(MegaCrit.Sts2.Core.Nodes.Combat.NEndTurnButton __instance)
        {
            if (!ForkedRoadManager.IsSplitBatchInProgress)
            {
                return true;
            }

            CombatState? state = NEndTurnButtonCombatStateRef(__instance);
            return state == null || LocalContext.GetMe(state) != null;
        }
    }

    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Combat.NCreature), "OnFocus")]
    private static class NCreature_OnFocus_Patch
    {
        private static bool Prefix(MegaCrit.Sts2.Core.Nodes.Combat.NCreature __instance)
        {
            if (!ForkedRoadManager.IsSplitBatchInProgress)
            {
                return true;
            }

            CombatState? combatState = __instance.Entity?.CombatState;
            return combatState == null || combatState.Players.Any(LocalContext.IsMe);
        }
    }

    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Combat.NCreature), "OnUnfocus")]
    private static class NCreature_OnUnfocus_Patch
    {
        private static bool Prefix(MegaCrit.Sts2.Core.Nodes.Combat.NCreature __instance)
        {
            if (!ForkedRoadManager.IsSplitBatchInProgress)
            {
                return true;
            }

            CombatState? combatState = __instance.Entity?.CombatState;
            return combatState == null || combatState.Players.Any(LocalContext.IsMe);
        }
    }

    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Multiplayer.Game.EventSynchronizer), nameof(MegaCrit.Sts2.Core.Multiplayer.Game.EventSynchronizer.BeginEvent))]
    private static class EventSynchronizer_BeginEvent_Patch
    {
        private static bool Prefix(MegaCrit.Sts2.Core.Multiplayer.Game.EventSynchronizer __instance, EventModel canonicalEvent, bool isPrefinished, Action<EventModel>? debugOnStart)
        {
            ClearSharedEventVoteSnapshot();

            List<EventModel> existingEvents = EventSynchronizerEventsRef(__instance);
            if (existingEvents.Count == 0)
            {
                return true;
            }

            bool hasInvalidEvent = existingEvents.Any(static e => e == null || (!e.IsFinished && e.Owner == null));
            if (!hasInvalidEvent)
            {
                return true;
            }

            foreach (EventModel existingEvent in existingEvents.Where(static e => e != null))
            {
                existingEvent.EnsureCleanup();
            }

            existingEvents.Clear();
            List<uint?> playerVotes = EventSynchronizerPlayerVotesRef(__instance);
            for (int i = 0; i < playerVotes.Count; i++)
            {
                playerVotes[i] = null;
            }

            EventSynchronizerPageIndexRef(__instance) = 0u;
            EventSynchronizerCanonicalEventRef(__instance) = null;
            return true;
        }
    }

    [HarmonyPatch(typeof(EventModel), "EnterCombatWithoutExitingEvent", new[] { typeof(EncounterModel), typeof(IReadOnlyList<Reward>), typeof(bool) })]
    private static class EventModel_EnterCombatWithoutExitingEvent_Patch
    {
        private static bool Prefix(EventModel __instance, EncounterModel mutableEncounter, IReadOnlyList<Reward> extraRewards, bool shouldResumeAfterCombat)
        {
            if (!ForkedRoadManager.IsSplitBatchInProgress || !ForkedRoadManager.IsSpectatingBranch())
            {
                return true;
            }

            if (!__instance.IsShared)
            {
                return true;
            }

            if (shouldResumeAfterCombat && __instance.LayoutType == EventLayoutType.Combat)
            {
                return true;
            }

            InvokeEventEnteringCombat(__instance);

            Player? owner = __instance.Owner;
            if (owner == null)
            {
                EventLog.Warn($"Spectator event-combat entry aborted because event {__instance.Id} has no owner.");
                return false;
            }

            EventModelNodeRef(__instance) = null;
            CombatState combatState = __instance.LayoutType != EventLayoutType.Combat
                ? new CombatState(mutableEncounter, owner.RunState, owner.RunState.Modifiers, owner.RunState.MultiplayerScalingModel)
                : EventModelCombatStateForCombatLayoutRef(__instance)!;

            CombatRoom combatRoom = new CombatRoom(combatState)
            {
                ShouldCreateCombat = (__instance.LayoutType != EventLayoutType.Combat),
                ShouldResumeParentEventAfterCombat = shouldResumeAfterCombat,
                ParentEventId = __instance.Id
            };

            foreach (Reward extraReward in extraRewards)
            {
                combatRoom.AddExtraReward(extraReward.Player, extraReward);
            }

            EventLog.Info($"Spectator entering event combat for {__instance.Id}; resumeAfterCombat={shouldResumeAfterCombat} layout={__instance.LayoutType}.");
            TaskHelper.RunSafely(RunManager.Instance.EnterRoomWithoutExitingCurrentRoom(combatRoom, __instance.LayoutType != EventLayoutType.Combat));
            return false;
        }
    }

    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Multiplayer.Game.EventSynchronizer), "PlayerVotedForSharedOptionIndex")]
    private static class EventSynchronizer_PlayerVotedForSharedOptionIndex_Patch
    {
        private static bool Prefix(MegaCrit.Sts2.Core.Multiplayer.Game.EventSynchronizer __instance, Player player, uint optionIndex, uint pageIndex)
        {
            if (!ForkedRoadManager.IsSplitBatchInProgress)
            {
                return true;
            }

            if (pageIndex < EventSynchronizerPageIndexRef(__instance))
            {
                return false;
            }
            if (pageIndex > EventSynchronizerPageIndexRef(__instance))
            {
                return false;
            }

            if (!ForkedRoadManager.IsPlayerActive(player))
            {
                EventLog.Info($"Ignoring shared-event vote from inactive player {player.NetId} for option {optionIndex}.");
                return false;
            }

            IPlayerCollection playerCollection = EventSynchronizerPlayerCollectionRef(__instance);
            List<uint?> playerVotes = EventSynchronizerPlayerVotesRef(__instance);
            int slot = playerCollection.GetPlayerSlotIndex(player);
            playerVotes[slot] = optionIndex;
            InvokeEventPlayerVoteChanged(__instance, player);

            List<Player> activePlayers = playerCollection.Players.Where(ForkedRoadManager.IsPlayerActive).ToList();
            if (activePlayers.Count == 0)
            {
                return false;
            }

            EventLog.Info($"Shared-event vote received: player={player.NetId} option={optionIndex} activePlayers={string.Join(",", activePlayers.Select(static p => p.NetId))}");

            if (activePlayers.All((Player p) => playerVotes[playerCollection.GetPlayerSlotIndex(p)].HasValue) &&
                EventSynchronizerNetServiceRef(__instance).Type != NetGameType.Client)
            {
                CaptureSharedEventVotes(__instance);
                uint chosenIndex = EventSynchronizerSharedOptionRngRef(__instance)
                    .NextItem(activePlayers.Select((Player p) => playerVotes[playerCollection.GetPlayerSlotIndex(p)]!.Value));

                SharedEventOptionChosenMessage message = new SharedEventOptionChosenMessage
                {
                    optionIndex = chosenIndex,
                    pageIndex = EventSynchronizerPageIndexRef(__instance),
                    location = RunManager.Instance.RunLocationTargetedBuffer.CurrentLocation
                };
                EventSynchronizerNetServiceRef(__instance).SendMessage(message);
                AccessTools.Method(typeof(MegaCrit.Sts2.Core.Multiplayer.Game.EventSynchronizer), "ChooseOptionForSharedEvent")!
                    .Invoke(__instance, new object?[] { chosenIndex });
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Multiplayer.Game.EventSynchronizer), "ChooseOptionForSharedEvent")]
    private static class EventSynchronizer_ChooseOptionForSharedEvent_Patch
    {
        private static bool Prefix(MegaCrit.Sts2.Core.Multiplayer.Game.EventSynchronizer __instance, uint optionIndex)
        {
            if (!ForkedRoadManager.IsSplitBatchInProgress)
            {
                return true;
            }

            CaptureSharedEventVotes(__instance);

            List<uint?> playerVotes = EventSynchronizerPlayerVotesRef(__instance);
            for (int i = 0; i < playerVotes.Count; i++)
            {
                playerVotes[i] = null;
            }

            EventSynchronizerPageIndexRef(__instance) = EventSynchronizerPageIndexRef(__instance) + 1u;

            List<Player> activePlayers = EventSynchronizerPlayerCollectionRef(__instance).Players.Where(ForkedRoadManager.IsPlayerActive).ToList();
            EventLog.Info($"Choosing shared-event option {optionIndex} for active players {string.Join(",", activePlayers.Select(static p => p.NetId))}.");

            foreach (Player player in activePlayers)
            {
                AccessTools.Method(typeof(MegaCrit.Sts2.Core.Multiplayer.Game.EventSynchronizer), "ChooseOptionForEvent")!
                    .Invoke(__instance, new object?[] { player, (int)optionIndex });
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Multiplayer.Game.EventSynchronizer), nameof(MegaCrit.Sts2.Core.Multiplayer.Game.EventSynchronizer.GetLocalEvent))]
    private static class EventSynchronizer_GetLocalEvent_Patch
    {
        private static bool Prefix(MegaCrit.Sts2.Core.Multiplayer.Game.EventSynchronizer __instance, ref EventModel __result)
        {
            if (!ForkedRoadManager.IsSpectatingBranch() || GetRunManagerState(RunManager.Instance) == null)
            {
                return true;
            }

            Player? proxy = ForkedRoadManager.GetPerspectivePlayer(GetRunManagerState(RunManager.Instance)!.Players);
            if (proxy == null)
            {
                return true;
            }

            __result = __instance.GetEventForPlayer(proxy);
            return false;
        }
    }

    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Multiplayer.Game.EventSynchronizer), "ChooseOptionForEvent")]
    private static class EventSynchronizer_ChooseOptionForEvent_Patch
    {
        private static bool Prefix(MegaCrit.Sts2.Core.Multiplayer.Game.EventSynchronizer __instance, Player player, int optionIndex)
        {
            if (!ForkedRoadManager.IsSplitBatchInProgress)
            {
                return true;
            }

            if (!ForkedRoadManager.IsPlayerActive(player))
            {
                return false;
            }

            EventModel eventForPlayer = __instance.GetEventForPlayer(player);
            if (eventForPlayer.IsFinished)
            {
                return false;
            }
            if (optionIndex >= eventForPlayer.CurrentOptions.Count)
            {
                return false;
            }
            return true;
        }
    }

    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Multiplayer.Game.RestSiteSynchronizer), nameof(MegaCrit.Sts2.Core.Multiplayer.Game.RestSiteSynchronizer.GetLocalOptions))]
    private static class RestSiteSynchronizer_GetLocalOptions_Patch
    {
        private static bool Prefix(MegaCrit.Sts2.Core.Multiplayer.Game.RestSiteSynchronizer __instance, ref IReadOnlyList<MegaCrit.Sts2.Core.Entities.RestSite.RestSiteOption> __result)
        {
            if (!ForkedRoadManager.IsSpectatingBranch() || GetRunManagerState(RunManager.Instance) == null)
            {
                return true;
            }

            Player? proxy = ForkedRoadManager.GetPerspectivePlayer(GetRunManagerState(RunManager.Instance)!.Players);
            if (proxy == null)
            {
                return true;
            }

            __result = __instance.GetOptionsForPlayer(proxy);
            return false;
        }
    }

    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Multiplayer.Game.TreasureRoomRelicSynchronizer), nameof(MegaCrit.Sts2.Core.Multiplayer.Game.TreasureRoomRelicSynchronizer.BeginRelicPicking))]
    private static class TreasureRoomRelicSynchronizer_BeginRelicPicking_Patch
    {
        private static bool Prefix(MegaCrit.Sts2.Core.Multiplayer.Game.TreasureRoomRelicSynchronizer __instance)
        {
            if (!ForkedRoadManager.IsSplitBatchInProgress)
            {
                return true;
            }

            IPlayerCollection players = TreasurePlayerCollectionRef(__instance);
            List<RelicModel>? currentRelics = TreasureCurrentRelicsRef(__instance);
            if (currentRelics != null)
            {
                throw new InvalidOperationException("Attempted to start new relic picking session while one was already occurring!");
            }

            List<RelicModel> relics = new();
            List<int?> votes = TreasureVotesRef(__instance);
            votes.Clear();
            TreasurePredictedVoteRef(__instance) = null;
            TreasureCurrentRelicsRef(__instance) = relics;

            MegaCrit.Sts2.Core.Runs.RelicGrabBag grabBag = TreasureSharedGrabBagRef(__instance);
            MegaCrit.Sts2.Core.Random.Rng rng = TreasureRngRef(__instance);

            foreach (Player player in players.Players)
            {
                votes.Add(null);
                if (!ForkedRoadManager.IsPlayerActive(player))
                {
                    continue;
                }

                IRunState runState = player.RunState;
                if (Hook.ShouldGenerateTreasure(runState, player))
                {
                    MegaCrit.Sts2.Core.Entities.Relics.RelicRarity rarity = MegaCrit.Sts2.Core.Factories.RelicFactory.RollRarity(rng);
                    RelicModel? forced = (RelicModel?)TreasureTryGetRelicForTutorialMethod.Invoke(__instance, new object?[] { runState.UnlockState });
                    RelicModel item = forced ?? grabBag.PullFromFront(rarity, runState) ?? MegaCrit.Sts2.Core.Factories.RelicFactory.FallbackRelic;
                    relics.Add(item);
                }
            }

            if (relics.Count > 0)
            {
                (TreasureVotesChangedField.GetValue(__instance) as Action)?.Invoke();
            }
            else
            {
                TreasureCurrentRelicsRef(__instance) = null;
                (TreasureRelicsAwardedField.GetValue(__instance) as Action<List<MegaCrit.Sts2.Core.Entities.TreasureRelicPicking.RelicPickingResult>>)?.Invoke(new List<MegaCrit.Sts2.Core.Entities.TreasureRelicPicking.RelicPickingResult>());
            }

            return false;
        }
    }

    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Multiplayer.Game.TreasureRoomRelicSynchronizer), nameof(MegaCrit.Sts2.Core.Multiplayer.Game.TreasureRoomRelicSynchronizer.OnPicked))]
    private static class TreasureRoomRelicSynchronizer_OnPicked_Patch
    {
        private static bool Prefix(MegaCrit.Sts2.Core.Multiplayer.Game.TreasureRoomRelicSynchronizer __instance, Player player, int index)
        {
            if (!ForkedRoadManager.IsSplitBatchInProgress)
            {
                return true;
            }

            List<RelicModel>? relics = TreasureCurrentRelicsRef(__instance);
            if (relics == null)
            {
                return false;
            }
            if (index >= relics.Count)
            {
                return false;
            }

            IPlayerCollection players = TreasurePlayerCollectionRef(__instance);
            List<int?> votes = TreasureVotesRef(__instance);
            votes[players.GetPlayerSlotIndex(player)] = index;
            (TreasureVotesChangedField.GetValue(__instance) as Action)?.Invoke();

            List<Player> activePlayers = players.Players.Where(ForkedRoadManager.IsPlayerActive).ToList();
            if (activePlayers.Any((Player p) => !votes[players.GetPlayerSlotIndex(p)].HasValue))
            {
                return false;
            }

            Dictionary<int, List<Player>> groupedVotes = new();
            for (int i = 0; i < relics.Count; i++)
            {
                groupedVotes[i] = new List<Player>();
            }

            foreach (Player activePlayer in activePlayers)
            {
                int selectedIndex = votes[players.GetPlayerSlotIndex(activePlayer)]!.Value;
                groupedVotes[selectedIndex].Add(activePlayer);
            }

            List<MegaCrit.Sts2.Core.Entities.TreasureRelicPicking.RelicPickingResult> results = new();
            List<RelicModel> leftovers = new();
            MegaCrit.Sts2.Core.Random.Rng rng = TreasureRngRef(__instance);
            foreach ((int relicIndex, List<Player> votedPlayers) in groupedVotes)
            {
                RelicModel relic = relics[relicIndex];
                if (votedPlayers.Count == 0)
                {
                    leftovers.Add(relic);
                }
                else if (votedPlayers.Count == 1)
                {
                    results.Add(new MegaCrit.Sts2.Core.Entities.TreasureRelicPicking.RelicPickingResult
                    {
                        type = MegaCrit.Sts2.Core.Entities.TreasureRelicPicking.RelicPickingResultType.OnlyOnePlayerVoted,
                        relic = relic,
                        player = votedPlayers[0]
                    });
                }
                else
                {
                    MegaCrit.Sts2.Core.Entities.TreasureRelicPicking.RelicPickingFightMove[] possibleMoves = Enum.GetValues<MegaCrit.Sts2.Core.Entities.TreasureRelicPicking.RelicPickingFightMove>();
                    results.Add(MegaCrit.Sts2.Core.Entities.TreasureRelicPicking.RelicPickingResult.GenerateRelicFight(votedPlayers, relic, () => rng.NextItem(possibleMoves)));
                }
            }

            List<Player> playersWithoutRelic = activePlayers.Where((Player p) => results.Find((MegaCrit.Sts2.Core.Entities.TreasureRelicPicking.RelicPickingResult r) => r.player == p) == null).ToList();
            for (int i = leftovers.Count - 1; i > 0; i--)
            {
                int swapIndex = rng.NextInt(i + 1);
                (leftovers[i], leftovers[swapIndex]) = (leftovers[swapIndex], leftovers[i]);
            }
            for (int i = 0; i < Mathf.Min(leftovers.Count, playersWithoutRelic.Count); i++)
            {
                results.Add(new MegaCrit.Sts2.Core.Entities.TreasureRelicPicking.RelicPickingResult
                {
                    type = MegaCrit.Sts2.Core.Entities.TreasureRelicPicking.RelicPickingResultType.ConsolationPrize,
                    player = playersWithoutRelic[i],
                    relic = leftovers[i]
                });
            }

            (TreasureRelicsAwardedField.GetValue(__instance) as Action<List<MegaCrit.Sts2.Core.Entities.TreasureRelicPicking.RelicPickingResult>>)?.Invoke(results);
            TreasureCurrentRelicsRef(__instance) = null;
            return false;
        }
    }

    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic.NTreasureRoomRelicCollection), nameof(MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic.NTreasureRoomRelicCollection.InitializeRelics))]
    private static class NTreasureRoomRelicCollection_InitializeRelics_Patch
    {
        private static void Prefix(MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic.NTreasureRoomRelicCollection __instance)
        {
            TreasureHoldersInUseRef(__instance).Clear();
        }
    }

    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic.NTreasureRoomRelicCollection), "OnRelicsAwarded")]
    private static class NTreasureRoomRelicCollection_OnRelicsAwarded_Patch
    {
        private static bool Prefix(MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic.NTreasureRoomRelicCollection __instance, List<MegaCrit.Sts2.Core.Entities.TreasureRelicPicking.RelicPickingResult> results)
        {
            EnsureTreasureSpectatorCollectionReady();

            if (!ForkedRoadManager.IsSplitBatchInProgress)
            {
                return true;
            }

            List<MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic.NTreasureRoomRelicHolder> holders = TreasureHoldersInUseRef(__instance);
            if (holders.Count == 0)
            {
                __instance.InitializeRelics();
            }

            TaskHelper.RunSafely(SafeAnimateTreasureAwardsAsync(__instance, results));
            return false;
        }
    }

    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic.NTreasureRoomRelicCollection), "AnimateRelicAwards")]
    private static class NTreasureRoomRelicCollection_AnimateRelicAwards_Patch
    {
        private static bool Prefix(MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic.NTreasureRoomRelicCollection __instance, List<MegaCrit.Sts2.Core.Entities.TreasureRelicPicking.RelicPickingResult> results, ref Task __result)
        {
            if (!ForkedRoadManager.IsSplitBatchInProgress)
            {
                return true;
            }

            EnsureTreasureSpectatorCollectionReady();
            __result = SafeAnimateTreasureAwardsAsync(__instance, results);
            return false;
        }
    }

    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Screens.TreasureRoomRelic.NTreasureRoomRelicCollection), "PickRelic")]
    private static class NTreasureRoomRelicCollection_PickRelic_Patch
    {
        private static bool Prefix()
        {
            if (!ForkedRoadManager.IsSplitBatchInProgress)
            {
                return true;
            }

            return !ForkedRoadManager.IsSpectatingBranch() && RunManager.Instance.TreasureRoomRelicSynchronizer.CurrentRelics != null;
        }
    }

    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Multiplayer.Game.TreasureRoomRelicSynchronizer), nameof(MegaCrit.Sts2.Core.Multiplayer.Game.TreasureRoomRelicSynchronizer.PickRelicLocally))]
    private static class TreasureRoomRelicSynchronizer_PickRelicLocally_Patch
    {
        private static bool Prefix()
        {
            if (!ForkedRoadManager.IsSplitBatchInProgress)
            {
                return true;
            }

            return RunManager.Instance.TreasureRoomRelicSynchronizer.CurrentRelics != null;
        }
    }

    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Rooms.NMerchantRoom), "_Ready")]
    private static class NMerchantRoom_Ready_Patch
    {
        private static void Postfix(MegaCrit.Sts2.Core.Nodes.Rooms.NMerchantRoom __instance)
        {
            MerchantLog.Info($"NMerchantRoom._Ready reached. split={ForkedRoadManager.IsSplitBatchInProgress} spectating={ForkedRoadManager.IsSpectatingBranch()} players={NMerchantRoomPlayersRef(__instance).Count}");
            if (ForkedRoadManager.IsSplitBatchInProgress && !ForkedRoadManager.IsSpectatingBranch())
            {
                List<Player> players = NMerchantRoomPlayersRef(__instance);
                List<NMerchantCharacter> visuals = NMerchantRoomPlayerVisualsRef(__instance);
                for (int i = 0; i < Mathf.Min(players.Count, visuals.Count); i++)
                {
                    if (!ForkedRoadManager.IsPlayerActive(players[i]))
                    {
                        visuals[i].Visible = false;
                    }
                }
            }

            if (ForkedRoadManager.IsSpectatingBranch())
            {
                __instance.MerchantButton.Disable();
                __instance.ProceedButton.Disable();
                MerchantLog.Info("Merchant room initialized in spectator mode; controls disabled.");
            }
            else if (ForkedRoadManager.IsSplitBatchInProgress)
            {
                MerchantLog.Info("Merchant room initialized for active branch player; broadcasting scene-ready.");
                ForkedRoadManager.NotifyMerchantSceneReady();
            }
        }
    }

    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Rooms.NMerchantRoom), "OnActiveScreenUpdated")]
    private static class NMerchantRoom_OnActiveScreenUpdated_Patch
    {
        private static bool Prefix(MegaCrit.Sts2.Core.Nodes.Rooms.NMerchantRoom __instance)
        {
            if (!ForkedRoadManager.IsSpectatingBranch())
            {
                return true;
            }

            __instance.MerchantButton.Disable();
            __instance.ProceedButton.Disable();
            MerchantLog.Info("Merchant spectator screen update forced controls disabled.");
            return false;
        }
    }

    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Rooms.NRestSiteRoom), "_Ready")]
    private static class NRestSiteRoom_Ready_Patch
    {
        private static void Postfix(MegaCrit.Sts2.Core.Nodes.Rooms.NRestSiteRoom __instance)
        {
            if (ForkedRoadManager.IsSpectatingBranch())
            {
                __instance.DisableOptions();
                __instance.ProceedButton.Disable();
            }
            foreach (var character in __instance.Characters)
            {
                if (!ForkedRoadManager.IsPlayerActive(character.Player))
                {
                    character.Visible = false;
                }
            }
        }
    }

    [HarmonyPatch(typeof(NMultiplayerPlayerStateContainer), nameof(NMultiplayerPlayerStateContainer.Initialize))]
    private static class NMultiplayerPlayerStateContainer_Initialize_Patch
    {
        private static void Postfix()
        {
            ForkedRoadManager.RefreshDisplayedPlayers();
        }
    }

    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Multiplayer.Game.PeerInput.ScreenStateTracker), "OnOverlayStackChanged")]
    private static class ScreenStateTracker_OnOverlayStackChanged_Patch
    {
        private static bool Prefix(MegaCrit.Sts2.Core.Multiplayer.Game.PeerInput.ScreenStateTracker __instance)
        {
            if (RunManager.Instance.IsSinglePlayerOrFakeMultiplayer)
            {
                return false;
            }

            MegaCrit.Sts2.Core.Nodes.Screens.Overlays.IOverlayScreen overlayScreen = MegaCrit.Sts2.Core.Nodes.Screens.Overlays.NOverlayStack.Instance.Peek();
            if (overlayScreen is MegaCrit.Sts2.Core.Nodes.Screens.NRewardsScreen rewardsScreen)
            {
                if (ScreenStateTrackerRewardsRef(__instance) != rewardsScreen)
                {
                    ScreenStateTrackerRewardsRef(__instance) = rewardsScreen;
                    Callable callback = Callable.From((Action)(() =>
                    {
                        ScreenStateTrackerSyncLocalScreenMethod.Invoke(__instance, Array.Empty<object?>());
                    }));
                    if (!rewardsScreen.IsConnected(MegaCrit.Sts2.Core.Nodes.Screens.NRewardsScreen.SignalName.Completed, callback))
                    {
                        rewardsScreen.Connect(MegaCrit.Sts2.Core.Nodes.Screens.NRewardsScreen.SignalName.Completed, callback);
                    }
                }
            }
            else
            {
                ScreenStateTrackerRewardsRef(__instance) = null;
            }
            ScreenStateTrackerOverlayScreenRef(__instance) = overlayScreen?.ScreenType ?? NetScreenType.None;
            ScreenStateTrackerSyncLocalScreenMethod.Invoke(__instance, Array.Empty<object?>());
            return false;
        }
    }

    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Events.NEventOptionButton), "ShouldDisplayPlayerVote")]
    private static class NEventOptionButton_ShouldDisplayPlayerVote_Patch
    {
        private static bool Prefix(MegaCrit.Sts2.Core.Nodes.Events.NEventOptionButton __instance, Player player, ref bool __result)
        {
            if (!ForkedRoadManager.IsSplitBatchInProgress)
            {
                return true;
            }

            if (!ForkedRoadManager.IsPlayerActive(player))
            {
                __result = false;
                return false;
            }

            int index = NEventOptionButtonIndexRef(__instance);
            if (TryGetSharedEventVote(player, out int snapshotVote))
            {
                __result = snapshotVote == index;
                return false;
            }

            __result = RunManager.Instance.EventSynchronizer.GetPlayerVote(player) == index;
            return false;
        }
    }

    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Events.NEventLayout), "BeforeSharedOptionChosen")]
    private static class NEventLayout_BeforeSharedOptionChosen_Patch
    {
        private static bool Prefix(MegaCrit.Sts2.Core.Nodes.Events.NEventLayout __instance, EventOption option, ref Task __result)
        {
            if (!ForkedRoadManager.IsSplitBatchInProgress)
            {
                return true;
            }

            __result = BeforeSharedOptionChosenSplitAsync(__instance, option);
            return false;
        }
    }

    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Events.NEventLayout), "AddOptions")]
    private static class NEventLayout_AddOptions_Patch
    {
        private static void Postfix(MegaCrit.Sts2.Core.Nodes.Events.NEventLayout __instance)
        {
            if (!ForkedRoadManager.IsSplitBatchInProgress)
            {
                return;
            }

            EventModel eventModel = NEventLayoutEventRef(__instance);
            bool showShared = eventModel != null &&
                              eventModel.IsShared &&
                              !eventModel.IsFinished &&
                              GetEventActivePlayers(eventModel).Count > 1;

            MegaCrit.Sts2.addons.mega_text.MegaLabel? sharedLabel = __instance.GetNodeOrNull<MegaCrit.Sts2.addons.mega_text.MegaLabel>("%SharedEventLabel");
            if (sharedLabel != null)
            {
                sharedLabel.Visible = showShared;
            }

            foreach (MegaCrit.Sts2.Core.Nodes.Events.NEventOptionButton optionButton in __instance.OptionButtons)
            {
                optionButton.RefreshVotes();
            }

            EventLog.Info($"Event options added for {eventModel?.Id}: sharedVisible={showShared} activePlayers={string.Join(",", GetEventActivePlayers(eventModel).Select(static p => p.NetId))}");
        }
    }

    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Events.EventSplitVoteAnimation), "TickSplitVoteAnimation")]
    private static class EventSplitVoteAnimation_TickSplitVoteAnimation_Patch
    {
        private static bool Prefix(MegaCrit.Sts2.Core.Nodes.Events.EventSplitVoteAnimation __instance)
        {
            if (!ForkedRoadManager.IsSplitBatchInProgress)
            {
                return true;
            }

            return EventSplitVoteAnimationSortedPlayersRef(__instance).Count > 0;
        }
    }

    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Rooms.NEventRoom), nameof(MegaCrit.Sts2.Core.Nodes.Rooms.NEventRoom.OptionButtonClicked))]
    private static class NEventRoom_OptionButtonClicked_Patch
    {
        private static bool Prefix()
        {
            return !ForkedRoadManager.IsSpectatingBranch();
        }
    }

    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Rooms.NEventRoom), "SetOptions")]
    private static class NEventRoom_SetOptions_Patch
    {
        private static void Postfix(MegaCrit.Sts2.Core.Nodes.Rooms.NEventRoom __instance)
        {
            ClearSharedEventVoteSnapshot();

            if (ForkedRoadManager.IsSpectatingBranch())
            {
                __instance.Layout?.DisableEventOptions();
            }

            if (ForkedRoadManager.IsSplitBatchInProgress)
            {
                foreach (MegaCrit.Sts2.Core.Nodes.Events.NEventOptionButton optionButton in __instance.Layout?.OptionButtons ?? Array.Empty<MegaCrit.Sts2.Core.Nodes.Events.NEventOptionButton>())
                {
                    optionButton.RefreshVotes();
                }
            }
        }
    }

    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Rooms.NTreasureRoom), "OnChestButtonReleased")]
    private static class NTreasureRoom_OnChestButtonReleased_Patch
    {
        private static bool Prefix()
        {
            return !ForkedRoadManager.IsSpectatingBranch();
        }
    }

    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Rooms.NTreasureRoom), "OnProceedButtonPressed")]
    private static class NTreasureRoom_OnProceedButtonPressed_Patch
    {
        private static bool Prefix()
        {
            return !ForkedRoadManager.IsSpectatingBranch();
        }
    }

    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Rooms.NTreasureRoom), "OnProceedButtonReleased")]
    private static class NTreasureRoom_OnProceedButtonReleased_Patch
    {
        private static bool Prefix()
        {
            return !ForkedRoadManager.IsSpectatingBranch();
        }
    }

    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Multiplayer.NMultiplayerPlayerState), "OnCombatSetUp")]
    private static class NMultiplayerPlayerState_OnCombatSetUp_Patch
    {
        private static bool Prefix(MegaCrit.Sts2.Core.Nodes.Multiplayer.NMultiplayerPlayerState __instance)
        {
            if (!ForkedRoadManager.IsSplitBatchInProgress)
            {
                return true;
            }

            Player player = (Player)AccessTools.Property(typeof(MegaCrit.Sts2.Core.Nodes.Multiplayer.NMultiplayerPlayerState), "Player")!.GetValue(__instance)!;
            if (LocalContext.IsMe(player))
            {
                return true;
            }

            return player.PlayerCombatState != null;
        }
    }

    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Nodes.Multiplayer.NMultiplayerPlayerState), "RefreshCombatValues")]
    private static class NMultiplayerPlayerState_RefreshCombatValues_Patch
    {
        private static bool Prefix(MegaCrit.Sts2.Core.Nodes.Multiplayer.NMultiplayerPlayerState __instance)
        {
            if (!ForkedRoadManager.IsSplitBatchInProgress)
            {
                return true;
            }

            Player player = (Player)AccessTools.Property(typeof(MegaCrit.Sts2.Core.Nodes.Multiplayer.NMultiplayerPlayerState), "Player")!.GetValue(__instance)!;
            return player.PlayerCombatState != null;
        }
    }

    [HarmonyPatch(typeof(CombatManager), "AfterAllPlayersReadyToEndTurn")]
    private static class CombatManager_AfterAllPlayersReady_Patch
    {
        private static bool Prefix(CombatManager __instance, Func<Task>? actionDuringEnemyTurn, ref Task __result)
        {
            CombatState? state = __instance.DebugOnlyGetState();
            if (!IsLocalCombatSpectator(state))
            {
                return true;
            }

            __result = RunAsSpectatorAsync(__instance, actionDuringEnemyTurn);
            return false;
        }

        private static async Task RunAsSpectatorAsync(CombatManager manager, Func<Task>? actionDuringEnemyTurn)
        {
            SetPrivateProperty(manager, nameof(CombatManager.EndingPlayerTurnPhaseOne), true);
            RunManager.Instance.ActionQueueSynchronizer.SetCombatState(ActionSynchronizerCombatState.EndTurnPhaseOne);
            await (Task)CombatManagerWaitUntilQueueEmptyMethod.Invoke(manager, Array.Empty<object?>())!;
            await (Task)CombatManagerEndPlayerTurnPhaseOneMethod.Invoke(manager, Array.Empty<object?>())!;
            if (manager.IsInProgress && RunManager.Instance.NetService.Type != NetGameType.Replay && ForkedRoadManager.IsLocalPlayerActiveInCurrentBranch)
            {
                CombatState? state = manager.DebugOnlyGetState();
                Player? me = LocalContext.GetMe(state);
                if (me != null)
                {
                    RunManager.Instance.ActionQueueSynchronizer.RequestEnqueue(new ReadyToBeginEnemyTurnAction(me, actionDuringEnemyTurn));
                }
            }
            SetPrivateProperty(manager, nameof(CombatManager.EndingPlayerTurnPhaseOne), false);
        }
    }

    [HarmonyPatch(typeof(CombatManager), nameof(CombatManager.EndCombatInternal))]
    private static class CombatManager_EndCombatInternal_Patch
    {
        private static bool Prefix(CombatManager __instance, ref Task __result)
        {
            CombatState? combatState = __instance.DebugOnlyGetState();
            if (!IsLocalCombatSpectator(combatState))
            {
                return true;
            }

            __result = EndCombatAsSpectatorAsync(__instance, combatState!);
            return false;
        }

        private static async Task EndCombatAsSpectatorAsync(CombatManager manager, CombatState combatState)
        {
            IRunState runState = combatState.RunState;
            CombatRoom room = (CombatRoom)runState.CurrentRoom!;
            SetPrivateProperty(manager, nameof(CombatManager.IsInProgress), false);
            SetPrivateProperty(manager, nameof(CombatManager.IsPlayPhase), false);
            SetPrivateProperty(manager, nameof(CombatManager.PlayerActionsDisabled), false);
            CombatManagerPlayersTakingExtraTurnRef(manager).Clear();

            foreach (Player player in combatState.Players)
            {
                await player.ReviveBeforeCombatEnd();
            }

            await Hook.AfterCombatEnd(runState, combatState, room);
            manager.History.Clear();
            room.OnCombatEnded();
            if (RunManager.Instance.NetService.Type != NetGameType.Replay)
            {
                string replayPath = SaveManager.Instance.GetProfileScopedPath("replays/latest.mcr");
                RunManager.Instance.CombatReplayWriter.WriteReplay(replayPath, stopRecording: true);
            }

            foreach (Player player2 in combatState.Players)
            {
                player2.AfterCombatEnd();
            }

            await Hook.AfterCombatVictory(runState, combatState, room);
            MegaCrit.Sts2.Core.Nodes.HoverTips.NHoverTipSet.Clear();
            if (runState.CurrentMapPointHistoryEntry != null)
            {
                runState.CurrentMapPointHistoryEntry.Rooms.Last().TurnsTaken = combatState.RoundNumber;
            }

            bool isSecondBoss = runState.Map.SecondBossMapPoint != null && runState.CurrentMapCoord == runState.Map.SecondBossMapPoint.coord;
            bool isFinalBoss = runState.Map.SecondBossMapPoint == null && runState.CurrentMapCoord == runState.Map.BossMapPoint.coord;
            if (room.RoomType == RoomType.Boss && runState.CurrentActIndex == runState.Acts.Count - 1 && (isSecondBoss || isFinalBoss))
            {
                RunManager.Instance.WinTime = RunManager.Instance.RunTime;
            }

            room.MarkPreFinished();
            await SaveManager.Instance.SaveRun(room, saveProgress: false);
            NMapScreen.Instance?.SetTravelEnabled(enabled: true);
            combatState.MultiplayerScalingModel?.OnCombatFinished();
            (CombatManagerCombatWonField.GetValue(manager) as Action<CombatRoom>)?.Invoke(room);
            RunManager.Instance.ActionExecutor.Unpause();
            RunManager.Instance.ActionQueueSynchronizer.SetCombatState(ActionSynchronizerCombatState.NotInCombat);
            NRunMusicController.Instance?.UpdateTrack();
            (CombatManagerCombatEndedField.GetValue(manager) as Action<CombatRoom>)?.Invoke(room);
            ForkedRoadManager.NotifyLocalTerminalProceed();
        }
    }

    [HarmonyPatch(typeof(CombatManager), "StartTurn")]
    private static class CombatManager_StartTurn_Patch
    {
        private static bool Prefix(CombatManager __instance, ref Task __result, Func<Task>? actionDuringEnemyTurn)
        {
            if (ForkedRoadManager.IsCurrentBranchEndingByMerge)
            {
                Log.Info("ForkedRoad suppressed StartTurn because the current branch is already ending via merge.");
                __result = Task.CompletedTask;
                return false;
            }

            CombatState? combatState = __instance.DebugOnlyGetState();
            if (!ShouldResolveSupportBranchDeath(combatState))
            {
                return true;
            }

            Log.Info("ForkedRoad detected all support-branch players dead before start turn; resolving branch death immediately.");
            __result = ResolveSupportBranchDeathAsync(__instance, combatState!);
            return false;
        }
    }

    [HarmonyPatch(typeof(MegaCrit.Sts2.Core.Commands.CreatureCmd), "Kill", new[] { typeof(IReadOnlyCollection<Creature>), typeof(bool) })]
    private static class CreatureCmd_Kill_Patch
    {
        private static void Postfix(IReadOnlyCollection<Creature> creatures)
        {
            if (!ForkedRoadManager.IsSplitBatchInProgress || !ForkedRoadManager.IsSupportBranchActive)
            {
                return;
            }

            CombatState? combatState = creatures
                .Select(static creature => creature?.CombatState)
                .FirstOrDefault(static state => state != null);

            if (!ShouldResolveSupportBranchDeath(combatState))
            {
                return;
            }

            Log.Info("ForkedRoad detected all support-branch players dead during CreatureCmd.Kill; forcing win-condition check.");
            TaskHelper.RunSafely(CombatManager.Instance.CheckWinCondition());
        }
    }

    [HarmonyPatch(typeof(CombatManager), nameof(CombatManager.HandlePlayerDeath))]
    private static class CombatManager_HandlePlayerDeath_Patch
    {
        private static void Postfix(Player player)
        {
            if (!ForkedRoadManager.IsSplitBatchInProgress || !ForkedRoadManager.IsSupportBranchActive || !player.Creature.IsDead)
            {
                return;
            }

            ForkedRoadManager.MarkPlayerForBranchMerge(player);
        }
    }

    [HarmonyPatch(typeof(CombatManager), nameof(CombatManager.SetReadyToEndTurn))]
    private static class CombatManager_SetReadyToEndTurn_Patch
    {
        private static bool Prefix(CombatManager __instance, Player player)
        {
            CombatState? combatState = __instance.DebugOnlyGetState();
            if (!ForkedRoadManager.IsCurrentBranchEndingByMerge && !ShouldResolveSupportBranchDeath(combatState))
            {
                return true;
            }

            if (!player.Creature.IsDead)
            {
                return true;
            }

            Log.Info($"ForkedRoad intercepted SetReadyToEndTurn for dead player {player.NetId}; forcing branch merge resolution.");
            TaskHelper.RunSafely(__instance.CheckWinCondition());
            return false;
        }
    }

    [HarmonyPatch(typeof(CombatManager), nameof(CombatManager.SetReadyToBeginEnemyTurn))]
    private static class CombatManager_SetReadyToBeginEnemyTurn_Patch
    {
        private static bool Prefix(CombatManager __instance, Player player)
        {
            CombatState? combatState = __instance.DebugOnlyGetState();
            if (!ForkedRoadManager.IsCurrentBranchEndingByMerge && !ShouldResolveSupportBranchDeath(combatState))
            {
                return true;
            }

            if (!player.Creature.IsDead)
            {
                return true;
            }

            Log.Info($"ForkedRoad suppressed SetReadyToBeginEnemyTurn for dead player {player.NetId} because branch merge is pending.");
            return false;
        }
    }

    [HarmonyPatch(typeof(CombatManager), nameof(CombatManager.CheckWinCondition))]
    private static class CombatManager_CheckWinCondition_Patch
    {
        private static bool Prefix(CombatManager __instance, ref Task<bool> __result)
        {
            CombatState? combatState = __instance.DebugOnlyGetState();
            if (!ShouldResolveSupportBranchDeath(combatState))
            {
                return true;
            }

            __result = ResolveSupportBranchDeathAsync(__instance, combatState);
            return false;
        }
    }

    [HarmonyPatch(typeof(MultiplayerScalingModel), nameof(MultiplayerScalingModel.ModifyBlockMultiplicative))]
    private static class MultiplayerScalingModel_ModifyBlock_Patch
    {
        private static bool Prefix(MegaCrit.Sts2.Core.Entities.Creatures.Creature target, decimal block, ValueProp props, CardModel? cardSource, CardPlay? cardPlay, ref decimal __result)
        {
            if (!ForkedRoadManager.IsSplitBatchInProgress)
            {
                return true;
            }

            if (target != null && !target.IsPrimaryEnemy && !target.IsSecondaryEnemy)
            {
                __result = 1m;
                return false;
            }

            if (!props.HasFlag(ValueProp.Move) || props.HasFlag(ValueProp.Unpowered))
            {
                __result = 1m;
                return false;
            }

            int count = ForkedRoadManager.ActivePlayerCount;
            if (count == 1)
            {
                __result = 1m;
                return false;
            }

            CombatState? combatState = target?.CombatState;
            __result = count * MultiplayerScalingModel.GetMultiplayerScaling(combatState?.Encounter, combatState?.RunState.CurrentActIndex ?? 0);
            return false;
        }
    }

    [HarmonyPatch(typeof(MultiplayerScalingModel), nameof(MultiplayerScalingModel.ModifyPowerAmountGiven))]
    private static class MultiplayerScalingModel_ModifyPower_Patch
    {
        private static bool Prefix(PowerModel power, MegaCrit.Sts2.Core.Entities.Creatures.Creature giver, decimal amount, MegaCrit.Sts2.Core.Entities.Creatures.Creature? target, CardModel? cardSource, ref decimal __result)
        {
            if (!ForkedRoadManager.IsSplitBatchInProgress)
            {
                return true;
            }

            if (target == null || !target.IsPrimaryEnemy && !target.IsSecondaryEnemy)
            {
                __result = amount;
                return false;
            }

            if (!power.ShouldScaleInMultiplayer)
            {
                __result = amount;
                return false;
            }

            int count = ForkedRoadManager.ActivePlayerCount;
            if (count == 1)
            {
                __result = amount;
                return false;
            }

            if (power is ArtifactPower or SlipperyPower or PlatingPower or BufferPower)
            {
                __result = ((count - 1) * 2 + 1) * amount;
                return false;
            }

            CombatState? combatState = target.CombatState;
            __result = amount * count * MultiplayerScalingModel.GetMultiplayerScaling(combatState?.Encounter, combatState?.RunState.CurrentActIndex ?? 0);
            return false;
        }
    }
}
