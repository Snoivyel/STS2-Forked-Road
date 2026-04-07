using System.Collections;
using System.Collections.Generic;
using System.Linq;
using HarmonyLib;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Actions;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Game;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Singleton;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Odds;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Runs.History;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.Unlocks;

namespace ForkedRoad;

internal static partial class ForkedRoadManager
{
    private static readonly RouteSplitRuntime Runtime = new();

    private static RunState? _runState;

    private static INetGameService? _netService;

    private static bool _messageHandlersRegistered;

    private static bool _roomEnteredSubscribed;

    private static ForkedRoadSpectatorOverlay? _spectatorOverlay;

    private static RunLocation? _suppressedCombatRewardLocation;

    private static RunLocation? _deathClearedCombatLocation;

    private static readonly AccessTools.FieldRef<MapSelectionSynchronizer, List<MapVote?>> MapSelectionVotesRef =
        AccessTools.FieldRefAccess<MapSelectionSynchronizer, List<MapVote?>>("_votes");

    private static readonly AccessTools.FieldRef<EventSynchronizer, IPlayerCollection> EventSynchronizerPlayerCollectionRef =
        AccessTools.FieldRefAccess<EventSynchronizer, IPlayerCollection>("_playerCollection");

    private static readonly AccessTools.FieldRef<RestSiteSynchronizer, IPlayerCollection> RestSiteSynchronizerPlayerCollectionRef =
        AccessTools.FieldRefAccess<RestSiteSynchronizer, IPlayerCollection>("_playerCollection");

    private static readonly AccessTools.FieldRef<TreasureRoomRelicSynchronizer, IPlayerCollection> TreasureRoomRelicSynchronizerPlayerCollectionRef =
        AccessTools.FieldRefAccess<TreasureRoomRelicSynchronizer, IPlayerCollection>("_playerCollection");

    private static readonly AccessTools.FieldRef<OneOffSynchronizer, IPlayerCollection> OneOffSynchronizerPlayerCollectionRef =
        AccessTools.FieldRefAccess<OneOffSynchronizer, IPlayerCollection>("_playerCollection");

    private static readonly AccessTools.FieldRef<MapSelectionSynchronizer, MapLocation> MapSelectionAcceptingVotesFromSourceRef =
        AccessTools.FieldRefAccess<MapSelectionSynchronizer, MapLocation>("_acceptingVotesFromSource");

    private static readonly AccessTools.FieldRef<PlayerChoiceSynchronizer, List<uint>> PlayerChoiceSynchronizerChoiceIdsRef =
        AccessTools.FieldRefAccess<PlayerChoiceSynchronizer, List<uint>>("_choiceIds");

    private static readonly AccessTools.FieldRef<CombatStateSynchronizer, Dictionary<ulong, SerializablePlayer>> CombatSyncDataRef =
        AccessTools.FieldRefAccess<CombatStateSynchronizer, Dictionary<ulong, SerializablePlayer>>("_syncData");

    private static readonly AccessTools.FieldRef<CombatStateSynchronizer, SerializableRunRngSet> CombatSyncRngSetRef =
        AccessTools.FieldRefAccess<CombatStateSynchronizer, SerializableRunRngSet>("_rngSet");

    private static readonly AccessTools.FieldRef<CombatStateSynchronizer, TaskCompletionSource> CombatSyncCompletionSourceRef =
        AccessTools.FieldRefAccess<CombatStateSynchronizer, TaskCompletionSource>("_syncCompletionSource");

    private static readonly AccessTools.FieldRef<ActionQueueSynchronizer, ActionQueueSet> ActionQueueSynchronizerQueueSetRef =
        AccessTools.FieldRefAccess<ActionQueueSynchronizer, ActionQueueSet>("_actionQueueSet");

    private static readonly AccessTools.FieldRef<RunLocationTargetedMessageBuffer, HashSet<RunLocation>> RunLocationVisitedLocationsRef =
        AccessTools.FieldRefAccess<RunLocationTargetedMessageBuffer, HashSet<RunLocation>>("_visitedLocations");

    private static readonly System.Reflection.FieldInfo ActionQueueSetQueuesField =
        AccessTools.Field(typeof(ActionQueueSet), "_actionQueues");

    private static readonly System.Type ActionQueueSetQueueType =
        AccessTools.Inner(typeof(ActionQueueSet), "ActionQueue");

    private static readonly System.Reflection.FieldInfo ActionQueueOwnerIdField =
        AccessTools.Field(ActionQueueSetQueueType, "ownerId");

    private static readonly System.Reflection.FieldInfo ActionQueueActionsField =
        AccessTools.Field(ActionQueueSetQueueType, "actions");

    private static readonly System.Reflection.MethodInfo RunManagerRollRoomTypeForMethod =
        AccessTools.Method(typeof(RunManager), "RollRoomTypeFor");

    private static readonly System.Reflection.FieldInfo PlayerChoiceSynchronizerReceivedChoicesField =
        AccessTools.Field(typeof(PlayerChoiceSynchronizer), "_receivedChoices");

    private static readonly HashSet<RunLocation> SeededSplitLocations = new();

    private static readonly HashSet<RunLocation> SeededMapVoteLocations = new();

    private static readonly System.Reflection.FieldInfo RunLocationWaitingMessagesField =
        AccessTools.Field(typeof(RunLocationTargetedMessageBuffer), "_messagesWaitingOnLocationChange");

    internal static bool IsSplitBatchInProgress => Runtime.ActiveBatch != null;

    internal static bool HasUnresolvedBranches => Runtime.ActiveBatch?.BranchGroups.Any(static group => group.Phase != RouteSplitBranchPhase.Completed) == true;

    private sealed class BranchPlayerCollection : IPlayerCollection
    {
        private readonly List<Player> _players;

        public IReadOnlyList<Player> Players => _players;

        public BranchPlayerCollection(IEnumerable<Player> players)
        {
            _players = players.ToList();
        }

        public int GetPlayerSlotIndex(Player player)
        {
            return _players.IndexOf(player);
        }

        public Player? GetPlayer(ulong netId)
        {
            return _players.FirstOrDefault(player => player.NetId == netId);
        }
    }

    private readonly record struct ResolvedBranchRoomPlan(MapPointType PointType, RoomType RoomType, ModelId? ModelId);

    private sealed class BranchScopedRunState : IRunState
    {
        private readonly RunState _inner;
        private readonly List<Player> _players;
        private readonly Dictionary<ulong, Player> _playersById;

        public BranchScopedRunState(RunState inner, IEnumerable<Player> players)
        {
            _inner = inner;
            _players = players.ToList();
            _playersById = _players.ToDictionary(player => player.NetId);
        }

        public IReadOnlyList<Player> Players => _players;

        public IReadOnlyList<ActModel> Acts => _inner.Acts;

        public int CurrentActIndex
        {
            get => _inner.CurrentActIndex;
            set => _inner.CurrentActIndex = value;
        }

        public ActModel Act => _inner.Act;

        public ActMap Map
        {
            get => _inner.Map;
            set => _inner.Map = value;
        }

        public MapCoord? CurrentMapCoord => _inner.CurrentMapCoord;

        public GameMode GameMode => _inner.GameMode;

        public MapPoint? CurrentMapPoint => _inner.CurrentMapPoint;

        public RunLocation RunLocation => _inner.RunLocation;

        public MapLocation MapLocation => _inner.MapLocation;

        public int ActFloor
        {
            get => _inner.ActFloor;
            set => _inner.ActFloor = value;
        }

        public int TotalFloor => _inner.TotalFloor;

        public int CurrentRoomCount => _inner.CurrentRoomCount;

        public AbstractRoom? CurrentRoom => _inner.CurrentRoom;

        public AbstractRoom? BaseRoom => _inner.BaseRoom;

        public bool IsGameOver => _inner.IsGameOver;

        public int AscensionLevel => _inner.AscensionLevel;

        public RunRngSet Rng => _inner.Rng;

        public RunOddsSet Odds => _inner.Odds;

        public RelicGrabBag SharedRelicGrabBag => _inner.SharedRelicGrabBag;

        public UnlockState UnlockState => _inner.UnlockState;

        public IReadOnlyList<ModifierModel> Modifiers => _inner.Modifiers;

        public MultiplayerScalingModel? MultiplayerScalingModel => _inner.MultiplayerScalingModel;

        public IReadOnlyList<IReadOnlyList<MapPointHistoryEntry>> MapPointHistory => _inner.MapPointHistory;

        public MapPointHistoryEntry? CurrentMapPointHistoryEntry => _inner.CurrentMapPointHistoryEntry;

        public ExtraRunFields ExtraFields => _inner.ExtraFields;

        public int GetPlayerSlotIndex(Player player)
        {
            return _players.IndexOf(player);
        }

        public Player? GetPlayer(ulong netId)
        {
            return _playersById.GetValueOrDefault(netId);
        }

        public bool ContainsCard(CardModel card)
        {
            return _inner.ContainsCard(card);
        }

        public T CreateCard<T>(Player owner) where T : CardModel
        {
            return _inner.CreateCard<T>(owner);
        }

        public CardModel CreateCard(CardModel canonicalCard, Player owner)
        {
            return _inner.CreateCard(canonicalCard, owner);
        }

        public CardModel CloneCard(CardModel mutableCard)
        {
            return _inner.CloneCard(mutableCard);
        }

        public void AddCard(CardModel mutableCard, Player owner)
        {
            _inner.AddCard(mutableCard, owner);
        }

        public void RemoveCard(CardModel card)
        {
            _inner.RemoveCard(card);
        }

        public CardModel LoadCard(SerializableCard serializableCard, Player owner)
        {
            return _inner.LoadCard(serializableCard, owner);
        }

        public void AppendToMapPointHistory(MapPointType mapPointType, RoomType initialRoomType, ModelId? modelId)
        {
            _inner.AppendToMapPointHistory(mapPointType, initialRoomType, modelId);
        }

        public MapPointHistoryEntry? GetHistoryEntryFor(MapLocation location)
        {
            return _inner.GetHistoryEntryFor(location);
        }

        public IEnumerable<AbstractModel> IterateHookListeners(CombatState? childCombatState)
        {
            return _inner.IterateHookListeners(childCombatState);
        }

        public int GetAndIncrementNextRoomId()
        {
            return _inner.GetAndIncrementNextRoomId();
        }
    }

    internal static void NormalizeRunLocationBuffer(RunLocation location)
    {
        if (RunManager.Instance?.RunLocationTargetedBuffer == null)
        {
            return;
        }

        if (!IsSplitBatchInProgress && !HasDivergedPlayerLocations())
        {
            return;
        }

        HashSet<RunLocation> visitedLocations = RunLocationVisitedLocationsRef(RunManager.Instance.RunLocationTargetedBuffer);
        visitedLocations.Clear();
        visitedLocations.Add(location);

        foreach (RunLocation voteLocation in SeededMapVoteLocations)
        {
            visitedLocations.Add(voteLocation);
        }

        if (_netService?.Type == NetGameType.Host)
        {
            foreach (RunLocation splitLocation in SeededSplitLocations)
            {
                visitedLocations.Add(splitLocation);
            }
        }

        if (RunLocationWaitingMessagesField.GetValue(RunManager.Instance.RunLocationTargetedBuffer) is IList waitingMessages)
        {
            if (waitingMessages.Count > 0)
            {
                Log.Debug($"ForkedRoad dropped {waitingMessages.Count} buffered location-targeted messages while normalizing to {location}.");
            }
            waitingMessages.Clear();
        }
    }

    internal static void InitializeForRun(RunState? runState, INetGameService? netService)
    {
        if (runState == null || netService == null)
        {
            return;
        }

        bool serviceChanged = !ReferenceEquals(_netService, netService);
        if (_messageHandlersRegistered && serviceChanged && _netService != null)
        {
            UnregisterMessageHandlers(_netService);
        }

        _runState = runState;
        _netService = netService;
        EnsureRuntimePlayers();

        if (!_messageHandlersRegistered || serviceChanged)
        {
            RegisterMessageHandlers(netService);
        }

        if (!_roomEnteredSubscribed)
        {
            RunManager.Instance.RoomEntered += OnRoomEntered;
            _roomEnteredSubscribed = true;
        }

        if (Runtime.ActiveBatch == null)
        {
            Runtime.Phase = RouteSplitRunPhase.SharedMapSelection;
        }

        TryInitializeSavedRestoreState();
        RefreshUiState();
    }

    internal static void CleanUp()
    {
        if (_messageHandlersRegistered && _netService != null)
        {
            UnregisterMessageHandlers(_netService);
        }

        if (_roomEnteredSubscribed)
        {
            RunManager.Instance.RoomEntered -= OnRoomEntered;
            _roomEnteredSubscribed = false;
        }

        if (_spectatorOverlay != null)
        {
            _spectatorOverlay.RequestPrevious -= OnSpectatorPreviousRequested;
            _spectatorOverlay.RequestNext -= OnSpectatorNextRequested;
            if (_spectatorOverlay.IsInsideTree())
            {
                _spectatorOverlay.QueueFree();
            }
            _spectatorOverlay = null;
        }

        _runState = null;
        _netService = null;
        _messageHandlersRegistered = false;
        _suppressedCombatRewardLocation = null;
        _deathClearedCombatLocation = null;
        Runtime.ActiveBatch = null;
        Runtime.Spectators.Clear();
        Runtime.Phase = RouteSplitRunPhase.SharedMapSelection;
        Runtime.RequiresAuthoritativeRoomPlans = false;
        ClearSeededSplitLocations();
        ClearSeededMapVoteLocations();
        _isLoadingSavedMultiplayerRun = false;
        _savedRestoreAvailabilityKnown = false;
        _expectedRemoteRestoreSnapshot = false;
        _hasReceivedRemoteRestoreSnapshot = false;
        _hasAppliedActiveSavedRestoreSnapshot = false;
        _shouldBroadcastSavedRestoreState = false;
        _hasBroadcastSavedRestoreState = false;
        _savedRestoreReadySource = null;
        _loadedSavedRestoreSnapshot = null;
        _activeSavedRestoreSnapshot = null;

        foreach (PlayerBranchRuntime player in Runtime.Players.Values)
        {
            player.CurrentBranchId = null;
            player.SpectatingBranchId = null;
            player.IsEliminated = false;
            player.Phase = RouteSplitPlayerPhase.ChoosingRoute;
        }
    }

    internal static IReadOnlyList<Player> GetActivePlayers(IRunState runState)
    {
        if (!IsSplitBatchInProgress)
        {
            return runState.Players.Where(player => !IsPlayerEliminated(player.NetId)).ToList();
        }

        HashSet<ulong> activeIds = GetActivePlayerIdsForCurrentBranch();
        if (activeIds.Count == 0)
        {
            return new List<Player>();
        }

        return runState.Players.Where(player => activeIds.Contains(player.NetId)).ToList();
    }

    internal static IReadOnlyList<Player> GetScopedPlayers(IReadOnlyList<Player> players)
    {
        if (!IsSplitBatchInProgress || _runState == null)
        {
            return players;
        }

        HashSet<ulong> activeIds = GetActivePlayerIdsForCurrentBranch();
        if (activeIds.Count == 0 || activeIds.Count == players.Count)
        {
            return players;
        }

        return players.Where(player => activeIds.Contains(player.NetId)).ToList();
    }

    internal static IRunState ScopeRunStateToLocalBranch(IRunState runState)
    {
        if (!IsSplitBatchInProgress || runState is BranchScopedRunState || runState is not RunState concreteRunState)
        {
            return runState;
        }

        IReadOnlyList<Player> activePlayers = GetActivePlayers(runState);
        if (activePlayers.Count == runState.Players.Count)
        {
            return runState;
        }

        return new BranchScopedRunState(concreteRunState, activePlayers);
    }

    internal static bool ShouldIgnoreCombatSyncSender(ulong senderId)
    {
        if (!IsSplitBatchInProgress)
        {
            return false;
        }

        HashSet<ulong> activeIds = GetActivePlayerIdsForCurrentBranch();
        return activeIds.Count > 0 && !activeIds.Contains(senderId);
    }

    internal static bool TryHandleCombatSyncCompletion(CombatStateSynchronizer synchronizer)
    {
        if (!IsSplitBatchInProgress)
        {
            return false;
        }

        TaskCompletionSource? completionSource = CombatSyncCompletionSourceRef(synchronizer);
        if (completionSource == null)
        {
            return false;
        }

        if (_netService == null || _netService.Type == NetGameType.Singleplayer || synchronizer.IsDisabled)
        {
            completionSource.TrySetResult();
            return true;
        }

        HashSet<ulong> activeIds = GetActivePlayerIdsForCurrentBranch();
        if (activeIds.Count == 0)
        {
            completionSource.TrySetResult();
            return true;
        }

        Dictionary<ulong, SerializablePlayer> syncData = CombatSyncDataRef(synchronizer);
        foreach (ulong playerId in activeIds)
        {
            if (!syncData.ContainsKey(playerId))
            {
                return true;
            }
        }

        if (CombatSyncRngSetRef(synchronizer) != null)
        {
            completionSource.TrySetResult();
        }

        return true;
    }

    internal static bool TrySuppressChecksum(string context, GameAction? action, ref MegaCrit.Sts2.Core.Entities.Multiplayer.NetChecksumData result)
    {
        if (!IsSplitBatchInProgress)
        {
            return false;
        }

        BroadcastLocalCombatSnapshot($"checksum:{context}");
        result = default;
        return true;
    }

    internal static bool ShouldSuppressMapOpenForBarrier()
    {
        if (!IsSplitBatchInProgress || !HasUnresolvedBranches || !LocalContext.NetId.HasValue)
        {
            return false;
        }

        return Runtime.Players.TryGetValue(LocalContext.NetId.Value, out PlayerBranchRuntime? player) &&
            player.Phase is RouteSplitPlayerPhase.FinishedWaiting or RouteSplitPlayerPhase.SpectatingOtherBranch;
    }

    internal static bool BeforePlayerVotedForMapCoord(MapSelectionSynchronizer synchronizer, Player player, MapLocation source, MapVote? destination)
    {
        if (IsPlayerEliminated(player.NetId))
        {
            if (destination.HasValue && Runtime.Players.TryGetValue(player.NetId, out PlayerBranchRuntime? eliminatedPlayer))
            {
                eliminatedPlayer.MapVoteDestinationCoord = destination.Value.coord;
                if (NMapScreen.Instance != null)
                {
                    SyncMapPlayerMarkers(NMapScreen.Instance);
                    NMapScreen.Instance.RefreshAllMapPointVotes();
                }
            }

            return false;
        }

        if (Runtime.Players.TryGetValue(player.NetId, out PlayerBranchRuntime? runtimePlayer))
        {
            runtimePlayer.MapVoteDestinationCoord = destination?.coord;
        }

        if (_netService?.Type == NetGameType.Host && HasEliminatedPlayers() && destination.HasValue)
        {
            List<MapVote?> votes = MapSelectionVotesRef(synchronizer);
            foreach (Player deadPlayer in _runState!.Players.Where(deadPlayer => IsPlayerEliminated(deadPlayer.NetId)))
            {
                votes[_runState.GetPlayerSlotIndex(deadPlayer)] = destination;
            }
        }

        if (!HasDivergedPlayerLocations() || IsSplitBatchInProgress)
        {
            if (NMapScreen.Instance != null && destination.HasValue)
            {
                SyncMapPlayerMarkers(NMapScreen.Instance);
                NMapScreen.Instance.RefreshAllMapPointVotes();
            }
            return true;
        }

        MapCoord? trackedCoord = GetTrackedPlayerCoord(player.NetId);
        if (trackedCoord.HasValue && source.coord != trackedCoord.Value)
        {
            Log.Warn($"ForkedRoad rejected vote from player {player.NetId} because source {source} did not match tracked coord {trackedCoord.Value}.");
            return false;
        }

        if (NMapScreen.Instance != null)
        {
            SyncMapPlayerMarkers(NMapScreen.Instance);
            NMapScreen.Instance.RefreshAllMapPointVotes();
        }

        MapSelectionAcceptingVotesFromSourceRef(synchronizer) = source;

        return true;
    }

    internal static void OnLocalMapCoordEntered(MapCoord coord)
    {
        EnsureRuntimePlayers();
        _suppressedCombatRewardLocation = null;
        _deathClearedCombatLocation = null;
        if (IsSplitBatchInProgress && LocalContext.NetId.HasValue && Runtime.Players.TryGetValue(LocalContext.NetId.Value, out PlayerBranchRuntime? localPlayer))
        {
            localPlayer.SelectionCoord = coord;
            return;
        }

        foreach (PlayerBranchRuntime player in Runtime.Players.Values)
        {
            player.SelectionCoord = coord;
            player.CurrentBranchId = null;
            player.SpectatingBranchId = null;
            player.MapVoteDestinationCoord = null;
            player.Phase = player.IsEliminated ? RouteSplitPlayerPhase.ReadyForNextBatch : RouteSplitPlayerPhase.ChoosingRoute;
        }

        SeedMapVoteLocationsIfNeeded();
        if (NMapScreen.Instance != null)
        {
            SyncMapPlayerMarkers(NMapScreen.Instance);
            NMapScreen.Instance.RefreshAllMapPointVotes();
        }
    }

    internal static void OnLocalCombatSetUp()
    {
        BroadcastLocalCombatSnapshot("setup");
    }

    internal static void RevivePlayersForUpcomingBranch(BranchGroupRuntime branch)
    {
        if (_runState == null)
        {
            return;
        }

        foreach (ulong playerId in branch.PlayerIds)
        {
            if (!Runtime.Players.TryGetValue(playerId, out PlayerBranchRuntime? runtimePlayer))
            {
                continue;
            }

            if (!runtimePlayer.IsEliminated && _runState.GetPlayer(playerId)?.Creature.IsDead != true)
            {
                continue;
            }

            runtimePlayer.IsEliminated = false;
            Player? runPlayer = _runState.GetPlayer(playerId);
            if (runPlayer?.Creature.IsDead == true)
            {
                runPlayer.Creature.HealInternal(1m);
            }
        }
    }

    internal static void PrepareEventSynchronizerScopeForLocalBranch()
    {
        if (_runState == null || RunManager.Instance?.EventSynchronizer == null)
        {
            return;
        }

        EventSynchronizerPlayerCollectionRef(RunManager.Instance.EventSynchronizer) = CreateCurrentPlayerCollection();
    }

    internal static void PrepareRestSiteSynchronizerScopeForLocalBranch()
    {
        if (_runState == null || RunManager.Instance?.RestSiteSynchronizer == null)
        {
            return;
        }

        RestSiteSynchronizerPlayerCollectionRef(RunManager.Instance.RestSiteSynchronizer) = CreateCurrentPlayerCollection();
    }

    internal static void PrepareTreasureSynchronizerScopeForLocalBranch()
    {
        if (_runState == null || RunManager.Instance == null)
        {
            return;
        }

        IPlayerCollection collection = CreateCurrentPlayerCollection();
        TreasureRoomRelicSynchronizerPlayerCollectionRef(RunManager.Instance.TreasureRoomRelicSynchronizer) = collection;
        OneOffSynchronizerPlayerCollectionRef(RunManager.Instance.OneOffSynchronizer) = collection;
    }

    internal static void PrepareOneOffSynchronizerScopeForLocalBranch()
    {
        if (_runState == null || RunManager.Instance?.OneOffSynchronizer == null)
        {
            return;
        }

        OneOffSynchronizerPlayerCollectionRef(RunManager.Instance.OneOffSynchronizer) = CreateCurrentPlayerCollection();
    }

    internal static bool ShouldDelayBranchCompletionForEmbeddedEventCombat()
    {
        if (!IsSplitBatchInProgress || _runState == null)
        {
            return false;
        }

        return _runState.CurrentRoomCount > 1 && _runState.CurrentRoom is CombatRoom { ShouldResumeParentEventAfterCombat: true };
    }

    internal static void OnLocalCombatEnded()
    {
        BroadcastLocalCombatSnapshot("ended");
        if (ShouldSuppressCurrentCombatRewardFlow())
        {
            NotifyLocalBranchCompleted("death_clear");
        }
    }

    private static void RegisterMessageHandlers(INetGameService netService)
    {
        netService.RegisterMessageHandler<ForkedRoadBatchLockedMessage>(HandleBatchLockedMessage);
        netService.RegisterMessageHandler<ForkedRoadBranchRoomPlanMessage>(HandleBranchRoomPlanMessage);
        netService.RegisterMessageHandler<ForkedRoadBranchRoomEnteredMessage>(HandleBranchRoomEnteredMessage);
        netService.RegisterMessageHandler<ForkedRoadBranchRoomCompletedMessage>(HandleBranchRoomCompletedMessage);
        netService.RegisterMessageHandler<ForkedRoadPlayerEliminatedMessage>(HandlePlayerEliminatedMessage);
        netService.RegisterMessageHandler<ForkedRoadBranchCombatSnapshotMessage>(HandleBranchCombatSnapshotMessage);
        netService.RegisterMessageHandler<ForkedRoadPlayerSpectateTargetChangedMessage>(HandlePlayerSpectateTargetChangedMessage);
        netService.RegisterMessageHandler<ForkedRoadBatchAllCompletedMessage>(HandleBatchAllCompletedMessage);
        netService.RegisterMessageHandler<ForkedRoadSaveRestoreAvailabilityMessage>(HandleSaveRestoreAvailabilityMessage);
        netService.RegisterMessageHandler<ForkedRoadSaveRestoreStateMessage>(HandleSaveRestoreStateMessage);
        _messageHandlersRegistered = true;
    }

    private static void UnregisterMessageHandlers(INetGameService netService)
    {
        netService.UnregisterMessageHandler<ForkedRoadBatchLockedMessage>(HandleBatchLockedMessage);
        netService.UnregisterMessageHandler<ForkedRoadBranchRoomPlanMessage>(HandleBranchRoomPlanMessage);
        netService.UnregisterMessageHandler<ForkedRoadBranchRoomEnteredMessage>(HandleBranchRoomEnteredMessage);
        netService.UnregisterMessageHandler<ForkedRoadBranchRoomCompletedMessage>(HandleBranchRoomCompletedMessage);
        netService.UnregisterMessageHandler<ForkedRoadPlayerEliminatedMessage>(HandlePlayerEliminatedMessage);
        netService.UnregisterMessageHandler<ForkedRoadBranchCombatSnapshotMessage>(HandleBranchCombatSnapshotMessage);
        netService.UnregisterMessageHandler<ForkedRoadPlayerSpectateTargetChangedMessage>(HandlePlayerSpectateTargetChangedMessage);
        netService.UnregisterMessageHandler<ForkedRoadBatchAllCompletedMessage>(HandleBatchAllCompletedMessage);
        netService.UnregisterMessageHandler<ForkedRoadSaveRestoreAvailabilityMessage>(HandleSaveRestoreAvailabilityMessage);
        netService.UnregisterMessageHandler<ForkedRoadSaveRestoreStateMessage>(HandleSaveRestoreStateMessage);
        _messageHandlersRegistered = false;
    }

    private static void EnsureRuntimePlayers()
    {
        if (_runState == null)
        {
            return;
        }

        foreach (ulong playerId in _runState.Players.Select(static player => player.NetId))
        {
            if (!Runtime.Players.TryGetValue(playerId, out PlayerBranchRuntime? runtimePlayer))
            {
                Runtime.Players[playerId] = new PlayerBranchRuntime
                {
                    PlayerId = playerId,
                    SelectionCoord = _runState.CurrentMapCoord
                };
            }
            else if (!runtimePlayer.SelectionCoord.HasValue)
            {
                runtimePlayer.SelectionCoord = _runState.CurrentMapCoord;
            }
        }
    }

    private static MapCoord? GetTrackedPlayerCoord(ulong playerId)
    {
        if (Runtime.Players.TryGetValue(playerId, out PlayerBranchRuntime? player) && player.SelectionCoord.HasValue)
        {
            return player.SelectionCoord.Value;
        }

        return _runState?.CurrentMapCoord;
    }

    private static bool HasDivergedPlayerLocations()
    {
        EnsureRuntimePlayers();
        return Runtime.Players.Values
            .Select(player => player.SelectionCoord)
            .Where(static coord => coord.HasValue)
            .Select(static coord => coord!.Value)
            .Distinct()
            .Take(2)
            .Count() > 1;
    }

    private static HashSet<ulong> GetActivePlayerIdsForCurrentBranch()
    {
        if (IsSplitBatchInProgress &&
            LocalContext.NetId.HasValue &&
            Runtime.Players.TryGetValue(LocalContext.NetId.Value, out PlayerBranchRuntime? localPlayer) &&
            localPlayer.SelectionCoord.HasValue &&
            _runState?.CurrentMapCoord.HasValue == true &&
            localPlayer.SelectionCoord.Value != _runState.CurrentMapCoord.Value)
        {
            BranchGroupRuntime? savedLocalBranch = GetLocalBranch();
            if (savedLocalBranch != null)
            {
                return savedLocalBranch.PlayerIds.Where(IsPlayerActiveForBranch).ToHashSet();
            }
        }

        if (_runState?.CurrentMapCoord.HasValue == true)
        {
            HashSet<ulong> coordMatchedPlayers = Runtime.Players.Values
                .Where(player => player.SelectionCoord.HasValue &&
                                 player.SelectionCoord.Value == _runState.CurrentMapCoord.Value &&
                                 IsPlayerActiveForBranch(player.PlayerId))
                .Select(player => player.PlayerId)
                .ToHashSet();
            if (coordMatchedPlayers.Count > 0)
            {
                return coordMatchedPlayers;
            }
        }

        BranchGroupRuntime? localBranch = GetLocalBranch();
        return localBranch != null
            ? localBranch.PlayerIds.Where(IsPlayerActiveForBranch).ToHashSet()
            : new HashSet<ulong>();
    }

    private static BranchGroupRuntime? GetLocalBranch()
    {
        if (!LocalContext.NetId.HasValue)
        {
            return null;
        }

        return Runtime.ActiveBatch?.FindBranchForPlayer(LocalContext.NetId.Value);
    }

    private static void OnRoomEntered()
    {
        HandleLocalRoomEntered();
    }

    private static void OnSpectatorPreviousRequested()
    {
        TryCycleLocalSpectatorBranch(-1);
    }

    private static void OnSpectatorNextRequested()
    {
        TryCycleLocalSpectatorBranch(1);
    }

    private static void RefreshUiState()
    {
        RefreshMultiplayerPlayerVisibility();
        RefreshSpectatorOverlay();
    }

    private static void RefreshMultiplayerPlayerVisibility()
    {
        if (NRun.Instance?.GlobalUi?.MultiplayerPlayerContainer == null)
        {
            return;
        }

        HashSet<ulong>? visiblePlayerIds = null;
        if (HasEliminatedPlayers())
        {
            visiblePlayerIds = _runState?.Players
                .Where(player => !IsPlayerEliminated(player.NetId))
                .Select(player => player.NetId)
                .ToHashSet();
        }
        else if (IsSplitBatchInProgress)
        {
            HashSet<ulong> activeIds = GetActivePlayerIdsForCurrentBranch();
            if (activeIds.Count > 0)
            {
                visiblePlayerIds = activeIds;
            }
        }

        foreach (NMultiplayerPlayerState node in NRun.Instance.GlobalUi.MultiplayerPlayerContainer.GetChildren().OfType<NMultiplayerPlayerState>())
        {
            node.Visible = visiblePlayerIds == null || visiblePlayerIds.Contains(node.Player.NetId);
        }
    }

    private static void EnsureSpectatorOverlay()
    {
        if (NRun.Instance?.GlobalUi == null)
        {
            return;
        }

        if (_spectatorOverlay != null && _spectatorOverlay.IsInsideTree())
        {
            return;
        }

        _spectatorOverlay = new ForkedRoadSpectatorOverlay();
        _spectatorOverlay.RequestPrevious += OnSpectatorPreviousRequested;
        _spectatorOverlay.RequestNext += OnSpectatorNextRequested;
        NRun.Instance.GlobalUi.AddChild(_spectatorOverlay);
    }

    private static void RefreshSpectatorOverlay()
    {
        EnsureSpectatorOverlay();
        if (_spectatorOverlay == null)
        {
            return;
        }

        if (!LocalContext.NetId.HasValue || Runtime.ActiveBatch == null)
        {
            _spectatorOverlay.UpdateState(false, string.Empty, string.Empty, false, false);
            return;
        }

        ulong localPlayerId = LocalContext.NetId.Value;
        if (!Runtime.Players.TryGetValue(localPlayerId, out PlayerBranchRuntime? player) ||
            player.Phase is not (RouteSplitPlayerPhase.FinishedWaiting or RouteSplitPlayerPhase.SpectatingOtherBranch))
        {
            _spectatorOverlay.UpdateState(false, string.Empty, string.Empty, false, false);
            return;
        }

        Runtime.Spectators.TryGetValue(localPlayerId, out SpectatorRuntimeState? spectatorState);
        string status = $"Batch #{Runtime.ActiveBatch.BatchId} | unresolved={Runtime.ActiveBatch.BranchGroups.Count(group => group.Phase != RouteSplitBranchPhase.Completed)} | localPhase={player.Phase}";
        string currentView = spectatorState?.CurrentBranchId.HasValue == true
            ? $"Current view: branch {spectatorState.CurrentBranchId.Value}"
            : "Current view: waiting / no branch selected";
        string details = string.Join("\n", Runtime.ActiveBatch.BranchGroups
            .OrderBy(group => group.BranchId)
            .Select(group => $"Branch {group.BranchId} @ ({group.TargetCoord.col},{group.TargetCoord.row}) room={group.RoomType?.ToString() ?? "Unknown"} entered={group.EnteredPlayerIds.Count}/{group.PlayerIds.Count} ready={group.ReadyPlayerIds.Count}/{group.PlayerIds.Count} phase={group.Phase}"));
        _spectatorOverlay.UpdateState(true, status + "\n" + currentView, details, spectatorState?.CanSwitchLeft == true, spectatorState?.CanSwitchRight == true);
    }

    private static void BroadcastLocalCombatSnapshot(string source)
    {
        if (_runState == null || _netService == null || !LocalContext.NetId.HasValue || Runtime.ActiveBatch == null)
        {
            return;
        }

        BranchGroupRuntime? branch = GetLocalBranch();
        if (branch == null || _runState.CurrentRoom is not CombatRoom combatRoom)
        {
            return;
        }

        try
        {
            NetFullCombatState snapshot = NetFullCombatState.FromRun(_runState, null);
            ForkedRoadBranchCombatSnapshotMessage message = new()
            {
                actIndex = Runtime.ActiveBatch.ActIndex,
                batchId = Runtime.ActiveBatch.BatchId,
                branchId = branch.BranchId,
                coord = branch.TargetCoord,
                encounterId = combatRoom.Encounter.Id,
                alliedCreatureCount = combatRoom.CombatState.Allies.Count,
                snapshot = snapshot
            };
            _netService.SendMessage(message);
            HandleBranchCombatSnapshotMessage(message, _netService.NetId);
            Log.Debug($"ForkedRoad broadcast combat snapshot for branch {branch.BranchId} via {source}.");
        }
        catch (System.Exception ex)
        {
            Log.Warn($"ForkedRoad failed to broadcast combat snapshot via {source}: {ex.Message}");
        }
    }

    private static void HandleBranchCombatSnapshotMessage(ForkedRoadBranchCombatSnapshotMessage message, ulong senderId)
    {
        if (Runtime.ActiveBatch?.BatchId != message.batchId)
        {
            return;
        }

        BranchGroupRuntime? branch = Runtime.ActiveBatch.FindBranch(message.branchId);
        if (branch == null)
        {
            return;
        }

        branch.RoomType = branch.RoomType ?? RoomType.Monster;
        branch.EncounterId = message.encounterId;
        branch.LatestCombatState = message.snapshot;
        branch.AlliedCreatureCount = message.alliedCreatureCount;
        RefreshUiState();
    }

    private static void SeedBranchRunLocations(BranchBatchRuntime batch)
    {
        if (_netService?.Type != NetGameType.Host || RunManager.Instance?.RunLocationTargetedBuffer == null)
        {
            return;
        }

        HashSet<RunLocation> visitedLocations = RunLocationVisitedLocationsRef(RunManager.Instance.RunLocationTargetedBuffer);
        foreach (BranchGroupRuntime branch in batch.BranchGroups)
        {
            MapLocation mapLocation = new(branch.TargetCoord, batch.ActIndex);
            foreach (int? roomId in new int?[] { null, 0, 1 })
            {
                RunLocation location = new(mapLocation, roomId);
                visitedLocations.Add(location);
                SeededSplitLocations.Add(location);
            }
        }
    }

    private static void ClearSeededSplitLocations()
    {
        if (_netService?.Type != NetGameType.Host || RunManager.Instance?.RunLocationTargetedBuffer == null || SeededSplitLocations.Count == 0)
        {
            return;
        }

        HashSet<RunLocation> visitedLocations = RunLocationVisitedLocationsRef(RunManager.Instance.RunLocationTargetedBuffer);
        foreach (RunLocation location in SeededSplitLocations)
        {
            visitedLocations.Remove(location);
        }

        SeededSplitLocations.Clear();
    }

    internal static bool ShouldDisplayActionInLocalBranchUi(GameAction action)
    {
        if (!IsSplitBatchInProgress)
        {
            return true;
        }

        HashSet<ulong> activeIds = GetActivePlayerIdsForCurrentBranch();
        if (activeIds.Count == 0)
        {
            return true;
        }

        return activeIds.Contains(action.OwnerId);
    }

    internal static bool ShouldRelayForeignBranchAction(RunLocation location)
    {
        if (!IsSplitBatchInProgress || _netService?.Type != NetGameType.Host || _runState == null || Runtime.ActiveBatch == null)
        {
            return false;
        }

        if (!location.mapLocation.coord.HasValue)
        {
            return false;
        }

        if (_runState.RunLocation == location)
        {
            return false;
        }

        return Runtime.ActiveBatch.BranchGroups.Any(branch => branch.TargetCoord == location.mapLocation.coord.Value);
    }

    internal static void RelayForeignBranchAction(RequestEnqueueActionMessage message, ulong senderId)
    {
        if (_netService == null)
        {
            return;
        }

        ActionEnqueuedMessage forwarded = new()
        {
            playerId = senderId,
            location = message.location,
            action = message.action
        };
        _netService.SendMessage(forwarded);
        Log.Debug($"ForkedRoad relayed foreign-branch action without local execution: sender={senderId} location={message.location} action={message.action}");
    }

    internal static void RelayForeignBranchHookAction(RequestEnqueueHookActionMessage message, ulong senderId)
    {
        if (_netService == null)
        {
            return;
        }

        HookActionEnqueuedMessage forwarded = new()
        {
            ownerId = senderId,
            hookActionId = message.hookActionId,
            location = message.location,
            gameActionType = message.gameActionType
        };
        _netService.SendMessage(forwarded);
        Log.Debug($"ForkedRoad relayed foreign-branch hook action without local execution: sender={senderId} location={message.location} hookId={message.hookActionId}");
    }

    internal static void RelayForeignBranchResume(RequestResumeActionAfterPlayerChoiceMessage message)
    {
        if (_netService == null)
        {
            return;
        }

        ResumeActionAfterPlayerChoiceMessage forwarded = new()
        {
            actionId = message.actionId,
            location = message.location
        };
        _netService.SendMessage(forwarded);
        Log.Debug($"ForkedRoad relayed foreign-branch resume without local execution: location={message.location} actionId={message.actionId}");
    }

    internal static bool TryHandleResumeActionAfterPlayerChoiceRequest(ActionQueueSynchronizer synchronizer, RequestResumeActionAfterPlayerChoiceMessage message, ulong senderId)
    {
        if (_netService?.Type != NetGameType.Host || _runState == null)
        {
            return false;
        }

        if (ShouldRelayForeignBranchAction(message.location))
        {
            RelayForeignBranchResume(message);
            return true;
        }

        if (!IsSplitBatchInProgress && !HasDivergedPlayerLocations())
        {
            return false;
        }

        uint localActionId = message.actionId;
        if (TryGetPausedPlayerChoiceActionId(synchronizer, senderId, out uint mappedActionId))
        {
            localActionId = mappedActionId;
        }
        else if (TryGetSingleGatheringPlayerChoiceActionId(synchronizer, out uint singlePausedActionId))
        {
            localActionId = singlePausedActionId;
            Log.Debug($"ForkedRoad used single paused action fallback for resume request from sender={senderId} location={message.location}: remoteActionId={message.actionId} localActionId={localActionId}");
        }
        else
        {
            Log.Warn($"ForkedRoad could not find paused action for resume request from sender={senderId} location={message.location}; using remote action id {message.actionId}.");
        }

        ResumeActionAfterPlayerChoiceMessage forwarded = new()
        {
            actionId = message.actionId,
            location = message.location
        };
        _netService.SendMessage(forwarded);

        if (localActionId != message.actionId)
        {
            Log.Debug($"ForkedRoad remapped player-choice resume request for sender={senderId} location={message.location}: remoteActionId={message.actionId} localActionId={localActionId}");
        }

        ActionQueueSynchronizerQueueSetRef(synchronizer).ResumeActionWithoutSynchronizing(localActionId);
        return true;
    }

    internal static bool TryHandleResumeActionAfterPlayerChoiceMessage(ActionQueueSynchronizer synchronizer, ResumeActionAfterPlayerChoiceMessage message)
    {
        if (ShouldIgnoreBufferedHookLocation(message.location))
        {
            return true;
        }

        if (!IsSplitBatchInProgress && !HasDivergedPlayerLocations())
        {
            return false;
        }

        if (TryResolveLocalResumeActionId(synchronizer, message.actionId, out uint localActionId, out string resolution))
        {
            if (localActionId != message.actionId)
            {
                Log.Debug($"ForkedRoad remapped player-choice resume delivery for location={message.location}: remoteActionId={message.actionId} localActionId={localActionId} resolution={resolution}");
            }

            ActionQueueSynchronizerQueueSetRef(synchronizer).ResumeActionWithoutSynchronizing(localActionId);
            return true;
        }

        Log.Warn($"ForkedRoad ignored unmatched player-choice resume delivery for location={message.location} remoteActionId={message.actionId}; no resumable local action was found.");
        return true;
    }

    internal static bool ShouldSuppressSplitVoteAnimation()
    {
        return HasDivergedPlayerLocations() || IsSplitBatchInProgress;
    }

    internal static void SyncMapPlayerMarkers(NMapScreen screen)
    {
        if (_runState == null)
        {
            return;
        }

        if (!HasDivergedPlayerLocations() && !IsSplitBatchInProgress)
        {
            return;
        }

        screen.PlayerVoteDictionary.Clear();
        foreach (Player player in _runState.Players)
        {
            if (!Runtime.Players.TryGetValue(player.NetId, out PlayerBranchRuntime? runtimePlayer))
            {
                continue;
            }

            MapCoord? coord = runtimePlayer.MapVoteDestinationCoord ?? runtimePlayer.SelectionCoord;
            if (coord.HasValue)
            {
                screen.PlayerVoteDictionary[player] = coord.Value;
            }
        }
    }

    internal static bool ShouldIgnoreRewardMessage(RunLocation location)
    {
        return ShouldIgnoreForeignRunLocation(location);
    }

    internal static bool ShouldSuppressRoomEndRewards(Player player, AbstractRoom room)
    {
        return room is CombatRoom && ShouldSuppressCurrentCombatRewardFlow();
    }

    internal static bool ShouldIgnoreRestSiteMessage(RunLocation location)
    {
        return ShouldIgnoreForeignRunLocation(location);
    }

    internal static bool ShouldIgnoreBufferedAction(ActionEnqueuedMessage message)
    {
        if (message.action is MegaCrit.Sts2.Core.GameActions.NetVoteForMapCoordAction)
        {
            return false;
        }

        if (message.action is MegaCrit.Sts2.Core.GameActions.NetVoteToMoveToNextActAction)
        {
            return false;
        }

        return ShouldIgnoreForeignRunLocation(message.location);
    }

    internal static bool ShouldIgnoreBufferedHookLocation(RunLocation location)
    {
        return ShouldIgnoreForeignRunLocation(location);
    }

    internal static bool ShouldIgnoreEventMessage(RunLocation location)
    {
        return ShouldIgnoreForeignRunLocation(location);
    }

    private static void SeedMapVoteLocationsIfNeeded()
    {
        if (RunManager.Instance?.RunLocationTargetedBuffer == null)
        {
            return;
        }

        ClearSeededMapVoteLocations();
        if (!HasDivergedPlayerLocations())
        {
            return;
        }

        HashSet<RunLocation> visitedLocations = RunLocationVisitedLocationsRef(RunManager.Instance.RunLocationTargetedBuffer);
        foreach (PlayerBranchRuntime player in Runtime.Players.Values)
        {
            if (!player.SelectionCoord.HasValue)
            {
                continue;
            }

            RunLocation location = new(new MapLocation(player.SelectionCoord.Value, _runState!.CurrentActIndex), 0);
            visitedLocations.Add(location);
            SeededMapVoteLocations.Add(location);
        }
    }

    private static void ClearSeededMapVoteLocations()
    {
        if (RunManager.Instance?.RunLocationTargetedBuffer == null || SeededMapVoteLocations.Count == 0)
        {
            return;
        }

        HashSet<RunLocation> visitedLocations = RunLocationVisitedLocationsRef(RunManager.Instance.RunLocationTargetedBuffer);
        foreach (RunLocation location in SeededMapVoteLocations)
        {
            visitedLocations.Remove(location);
        }

        SeededMapVoteLocations.Clear();
    }

    internal static bool ShouldInterceptForeignMapVoteRequest(RequestEnqueueActionMessage message)
    {
        if (_netService?.Type != NetGameType.Host || _runState == null || IsSplitBatchInProgress || !HasDivergedPlayerLocations())
        {
            return false;
        }

        if (message.action is not MegaCrit.Sts2.Core.GameActions.NetVoteForMapCoordAction)
        {
            return false;
        }

        return message.location != _runState.RunLocation;
    }

    internal static void HandleForeignMapVoteRequest(RequestEnqueueActionMessage message, ulong senderId)
    {
        if (_runState == null || _netService == null || message.action is not MegaCrit.Sts2.Core.GameActions.NetVoteForMapCoordAction voteAction)
        {
            return;
        }

        Player? player = _runState.GetPlayer(senderId);
        if (player == null)
        {
            return;
        }

        if (Runtime.Players.TryGetValue(senderId, out PlayerBranchRuntime? runtimePlayer))
        {
            runtimePlayer.MapVoteDestinationCoord = voteAction.destination?.coord;
        }

        if (IsPlayerEliminated(senderId))
        {
            if (NMapScreen.Instance != null)
            {
                SyncMapPlayerMarkers(NMapScreen.Instance);
                NMapScreen.Instance.RefreshAllMapPointVotes();
            }

            return;
        }

        RunManager.Instance.MapSelectionSynchronizer.PlayerVotedForMapCoord(player, voteAction.source, voteAction.destination);
        ActionEnqueuedMessage forwarded = new()
        {
            playerId = senderId,
            location = message.location,
            action = message.action
        };
        _netService.SendMessage(forwarded);

        if (NMapScreen.Instance != null)
        {
            SyncMapPlayerMarkers(NMapScreen.Instance);
            NMapScreen.Instance.RefreshAllMapPointVotes();
        }

        Log.Debug($"ForkedRoad handled foreign map vote request directly: sender={senderId} source={voteAction.source} destination={voteAction.destination}");
    }

    private static bool ShouldIgnoreForeignRunLocation(RunLocation location)
    {
        if (_runState == null)
        {
            return false;
        }

        if (!IsSplitBatchInProgress && !HasDivergedPlayerLocations())
        {
            return false;
        }

        return location != _runState.RunLocation;
    }

    private static bool TryGetPausedPlayerChoiceActionId(ActionQueueSynchronizer synchronizer, ulong ownerId, out uint actionId)
    {
        foreach ((ulong queueOwnerId, GameAction action) in EnumerateQueuedActions(synchronizer))
        {
            if (queueOwnerId == ownerId &&
                action.State == GameActionState.GatheringPlayerChoice &&
                action.Id.HasValue)
            {
                actionId = action.Id.Value;
                return true;
            }
        }

        actionId = 0u;
        return false;
    }

    private static bool TryGetSingleGatheringPlayerChoiceActionId(ActionQueueSynchronizer synchronizer, out uint actionId)
    {
        List<uint> gatheringIds = EnumerateQueuedActions(synchronizer)
            .Where(static pair => pair.action.State == GameActionState.GatheringPlayerChoice && pair.action.Id.HasValue)
            .Select(static pair => pair.action.Id!.Value)
            .Distinct()
            .ToList();

        if (gatheringIds.Count == 1)
        {
            actionId = gatheringIds[0];
            return true;
        }

        actionId = 0u;
        return false;
    }

    private static bool TryResolveLocalResumeActionId(ActionQueueSynchronizer synchronizer, uint remoteActionId, out uint localActionId, out string resolution)
    {
        List<GameAction> queuedActions = EnumerateQueuedActions(synchronizer)
            .Select(static pair => pair.action)
            .ToList();

        GameAction? exactAction = queuedActions.FirstOrDefault(action => action.Id == remoteActionId);
        if (exactAction?.State == GameActionState.GatheringPlayerChoice)
        {
            localActionId = remoteActionId;
            resolution = "exact";
            return true;
        }

        if (exactAction != null)
        {
            localActionId = 0u;
            resolution = $"stale_exact:{exactAction.State}";
            return false;
        }

        List<GameAction> gatheringActions = queuedActions
            .Where(action => action.State == GameActionState.GatheringPlayerChoice && action.Id.HasValue)
            .ToList();

        if (gatheringActions.Count == 1)
        {
            localActionId = gatheringActions[0].Id!.Value;
            resolution = "single_gathering_fallback";
            return true;
        }

        localActionId = 0u;
        resolution = gatheringActions.Count == 0 ? "no_gathering_action" : $"ambiguous_gathering:{gatheringActions.Count}";
        return false;
    }

    private static IEnumerable<(ulong ownerId, GameAction action)> EnumerateQueuedActions(ActionQueueSynchronizer synchronizer)
    {
        if (ActionQueueSetQueuesField.GetValue(ActionQueueSynchronizerQueueSetRef(synchronizer)) is not IEnumerable queues)
        {
            yield break;
        }

        foreach (object? queue in queues)
        {
            if (queue == null ||
                ActionQueueOwnerIdField.GetValue(queue) is not ulong queueOwnerId ||
                ActionQueueActionsField.GetValue(queue) is not IList actions)
            {
                continue;
            }

            foreach (object? queuedAction in actions)
            {
                if (queuedAction is GameAction action)
                {
                    yield return (queueOwnerId, action);
                }
            }
        }
    }

    private static bool ShouldUseAuthoritativeRoomPlan(BranchGroupRuntime branch)
    {
        return Runtime.RequiresAuthoritativeRoomPlans && branch.PlayerIds.Count(IsPlayerActiveForBranch) > 1;
    }

    private static void ApplyResolvedRoomPlan(BranchGroupRuntime branch, ResolvedBranchRoomPlan plan)
    {
        branch.PointType = plan.PointType;
        branch.RoomType = plan.RoomType;
        branch.ResolvedModelId = plan.ModelId;
    }

    private static bool TryGetResolvedRoomPlan(BranchGroupRuntime branch, out ResolvedBranchRoomPlan plan)
    {
        if (!branch.PointType.HasValue || !branch.RoomType.HasValue)
        {
            plan = default;
            return false;
        }

        plan = new ResolvedBranchRoomPlan(branch.PointType.Value, branch.RoomType.Value, branch.ResolvedModelId);
        return true;
    }

    private static ResolvedBranchRoomPlan ResolveRoomPlanForBranch(BranchGroupRuntime branch)
    {
        if (_runState == null)
        {
            throw new System.InvalidOperationException("Run state is not initialized.");
        }

        MapPoint? point = _runState.Map.GetPoint(branch.TargetCoord);
        if (point == null)
        {
            throw new System.InvalidOperationException($"Could not resolve map point for coord {branch.TargetCoord}.");
        }
        MapPointType pointType = point.PointType;
        object? unknownRoomTypeResult = pointType == MapPointType.Unknown
            ? RunManagerRollRoomTypeForMethod.Invoke(
                RunManager.Instance,
                new object[]
                {
                    pointType,
                    RunManager.BuildRoomTypeBlacklist(_runState.CurrentMapPointHistoryEntry, _runState.CurrentMapPoint?.Children ?? new HashSet<MapPoint>())
                })
            : null;
        RoomType roomType = pointType switch
        {
            MapPointType.Unknown when unknownRoomTypeResult is RoomType resolvedRoomType => resolvedRoomType,
            MapPointType.Shop => RoomType.Shop,
            MapPointType.Treasure => RoomType.Treasure,
            MapPointType.RestSite => RoomType.RestSite,
            MapPointType.Monster => RoomType.Monster,
            MapPointType.Elite => RoomType.Elite,
            MapPointType.Boss => RoomType.Boss,
            MapPointType.Ancient => RoomType.Event,
            _ => throw new System.InvalidOperationException($"Unsupported point type for branch resolution: {pointType}")
        };

        ModelId? modelId = roomType switch
        {
            RoomType.Monster or RoomType.Elite or RoomType.Boss => _runState.Act.PullNextEncounter(roomType).Id,
            RoomType.Event when pointType == MapPointType.Ancient => _runState.Act.PullAncient().Id,
            RoomType.Event => _runState.Act.PullNextEvent(_runState).Id,
            _ => null
        };

        return new ResolvedBranchRoomPlan(pointType, roomType, modelId);
    }

    private static void BroadcastResolvedRoomPlan(BranchGroupRuntime branch, ResolvedBranchRoomPlan plan)
    {
        if (_netService == null || Runtime.ActiveBatch == null)
        {
            return;
        }

        ForkedRoadBranchRoomPlanMessage message = new()
        {
            actIndex = Runtime.ActiveBatch.ActIndex,
            batchId = Runtime.ActiveBatch.BatchId,
            branchId = branch.BranchId,
            coord = branch.TargetCoord,
            pointType = plan.PointType,
            roomType = plan.RoomType,
            hasModelId = plan.ModelId != null,
            modelCategory = plan.ModelId?.Category,
            modelEntry = plan.ModelId?.Entry
        };
        _netService.SendMessage(message);
        HandleBranchRoomPlanMessage(message, _netService.NetId);
        Log.Info($"ForkedRoad broadcast room plan for branch {branch.BranchId}: pointType={plan.PointType} roomType={plan.RoomType} model={plan.ModelId}");
    }

    private static async Task<bool> WaitForResolvedRoomPlanAsync(BranchGroupRuntime branch, int timeoutMs = 5000)
    {
        int remaining = timeoutMs;
        while (remaining > 0)
        {
            if (TryGetResolvedRoomPlan(branch, out _))
            {
                return true;
            }

            await Task.Delay(50);
            remaining -= 50;
        }

        return TryGetResolvedRoomPlan(branch, out _);
    }

    private static async Task EnterResolvedRoomAsync(BranchGroupRuntime branch, ResolvedBranchRoomPlan plan)
    {
        if (_runState == null)
        {
            return;
        }

        AbstractModel? model = null;
        if (plan.ModelId != null)
        {
            if (plan.RoomType == RoomType.Event)
            {
                EventModel eventModel = ModelDb.GetById<EventModel>(plan.ModelId);
                if (plan.PointType != MapPointType.Ancient && !_runState.VisitedEventIds.Contains(eventModel.Id))
                {
                    _runState.AddVisitedEvent(eventModel);
                }

                model = eventModel;
            }
            else if (plan.RoomType is RoomType.Monster or RoomType.Elite or RoomType.Boss)
            {
                model = ModelDb.GetById<EncounterModel>(plan.ModelId).ToMutable();
            }
        }

        _runState.ActFloor = branch.TargetCoord.row + 1;
        await RunManager.Instance.EnterMapCoordDebug(branch.TargetCoord, plan.RoomType, plan.PointType, model);
        RunManager.Instance.MapSelectionSynchronizer.OnLocationChanged(_runState.MapLocation);
        NormalizeRunLocationBuffer(_runState.RunLocation);
    }

    private static void HandleBranchRoomPlanMessage(ForkedRoadBranchRoomPlanMessage message, ulong senderId)
    {
        if (Runtime.ActiveBatch?.BatchId != message.batchId)
        {
            return;
        }

        BranchGroupRuntime? branch = Runtime.ActiveBatch.FindBranch(message.branchId);
        if (branch == null || senderId != branch.AuthorityPlayerId)
        {
            return;
        }

        ApplyResolvedRoomPlan(
            branch,
            new ResolvedBranchRoomPlan(
                message.pointType,
                message.roomType,
                message.hasModelId && !string.IsNullOrEmpty(message.modelCategory) && !string.IsNullOrEmpty(message.modelEntry)
                    ? new ModelId(message.modelCategory, message.modelEntry)
                    : null));
        Log.Info($"ForkedRoad received room plan for branch {branch.BranchId}: pointType={message.pointType} roomType={message.roomType} model={(message.hasModelId ? $"{message.modelCategory}.{message.modelEntry}" : "none")}");
        RefreshUiState();
    }

    internal static void OnCombatPlayerDied(Player player)
    {
        if (_runState == null || _netService == null || !Runtime.Players.TryGetValue(player.NetId, out PlayerBranchRuntime? runtimePlayer))
        {
            return;
        }

        runtimePlayer.IsEliminated = true;
        runtimePlayer.MapVoteDestinationCoord = null;
        runtimePlayer.Phase = RouteSplitPlayerPhase.FinishedWaiting;

        BranchGroupRuntime? branch = GetBranchForPlayer(player.NetId);
        if (branch != null)
        {
            branch.EliminatedPlayerIds.Add(player.NetId);
        }

        ForkedRoadPlayerEliminatedMessage message = new()
        {
            actIndex = _runState.CurrentActIndex,
            batchId = Runtime.ActiveBatch?.BatchId ?? 0,
            playerId = player.NetId,
            branchId = branch?.BranchId
        };
        _netService.SendMessage(message);
        HandlePlayerEliminatedMessage(message, _netService.NetId);

        if (!ShouldTriggerDeathClear(branch))
        {
            return;
        }

        MarkDeathClearTriggered(branch);
        _ = TaskHelper.RunSafely(ClearCombatAfterPlayerDeathAsync(branch));
    }

    internal static bool TryTriggerDeathClearForCurrentCombat()
    {
        if (_runState?.CurrentRoom is not CombatRoom combatRoom || !CombatManager.Instance.IsInProgress)
        {
            return false;
        }

        BranchGroupRuntime? branch = GetLocalBranch();
        if (!combatRoom.CombatState.Players.Any(static player => player.Creature.IsDead))
        {
            return false;
        }

        if (!ShouldTriggerDeathClear(branch))
        {
            return false;
        }

        MarkDeathClearTriggered(branch);
        _ = TaskHelper.RunSafely(ClearCombatAfterPlayerDeathAsync(branch));
        return true;
    }

    private static async Task ClearCombatAfterPlayerDeathAsync(BranchGroupRuntime? branch)
    {
        if (_runState?.CurrentRoom is not CombatRoom combatRoom)
        {
            return;
        }

        Log.Info($"ForkedRoad death clear triggered: branch={(branch?.BranchId.ToString() ?? "shared")}");
        foreach (Creature enemy in combatRoom.CombatState.Enemies.Where(static enemy => enemy.IsAlive).ToList())
        {
            await CreatureCmd.Escape(enemy);
        }

        if (CombatManager.Instance.IsInProgress)
        {
            await CombatManager.Instance.CheckWinCondition();
        }
    }

    private static void HandlePlayerEliminatedMessage(ForkedRoadPlayerEliminatedMessage message, ulong senderId)
    {
        if (!Runtime.Players.TryGetValue(message.playerId, out PlayerBranchRuntime? runtimePlayer))
        {
            return;
        }

        runtimePlayer.IsEliminated = true;
        runtimePlayer.MapVoteDestinationCoord = null;
        runtimePlayer.Phase = RouteSplitPlayerPhase.FinishedWaiting;

        BranchGroupRuntime? branch = message.branchId.HasValue
            ? Runtime.ActiveBatch?.FindBranch(message.branchId.Value)
            : GetBranchForPlayer(message.playerId);
        if (branch != null)
        {
            branch.EliminatedPlayerIds.Add(message.playerId);
        }

        RefreshUiState();
    }

    internal static void ApplyPlayerChoiceNamespacesForActiveBatch()
    {
        if (_runState == null || Runtime.ActiveBatch == null || RunManager.Instance?.PlayerChoiceSynchronizer == null)
        {
            return;
        }

        List<uint> choiceIds = PlayerChoiceSynchronizerChoiceIdsRef(RunManager.Instance.PlayerChoiceSynchronizer);
        while (choiceIds.Count < _runState.Players.Count)
        {
            choiceIds.Add(0u);
        }

        foreach (Player player in _runState.Players)
        {
            BranchGroupRuntime? branch = Runtime.ActiveBatch.FindBranchForPlayer(player.NetId);
            if (branch == null)
            {
                continue;
            }

            uint namespaceBase = (uint)branch.BranchId * 100000u;
            int slot = _runState.GetPlayerSlotIndex(player);
            if (choiceIds[slot] < namespaceBase)
            {
                choiceIds[slot] = namespaceBase;
            }
        }

        if (PlayerChoiceSynchronizerReceivedChoicesField.GetValue(RunManager.Instance.PlayerChoiceSynchronizer) is IList receivedChoices && receivedChoices.Count > 0)
        {
            Log.Debug($"ForkedRoad cleared {receivedChoices.Count} buffered player-choice results while applying branch namespaces.");
            receivedChoices.Clear();
        }
    }

    private static IPlayerCollection CreateCurrentPlayerCollection()
    {
        if (_runState == null)
        {
            throw new System.InvalidOperationException("Run state is not initialized.");
        }

        if (!IsSplitBatchInProgress)
        {
            return new BranchPlayerCollection(_runState.Players.Where(player => !IsPlayerEliminated(player.NetId)));
        }

        HashSet<ulong> activeIds = GetActivePlayerIdsForCurrentBranch();
        if (activeIds.Count == 0)
        {
            return new BranchPlayerCollection(Array.Empty<Player>());
        }

        return new BranchPlayerCollection(_runState.Players.Where(player => activeIds.Contains(player.NetId)));
    }

    internal static bool ShouldScopeCombatParticipants(IRunState runState)
    {
        return IsSplitBatchInProgress || HasEliminatedPlayers() || runState.Players.Any(static player => player.Creature.IsDead);
    }

    internal static bool ShouldSkipReviveBeforeCombatEnd(Player player)
    {
        return ShouldSuppressCurrentCombatRewardFlow();
    }

    private static bool IsPlayerEliminated(ulong playerId)
    {
        return Runtime.Players.TryGetValue(playerId, out PlayerBranchRuntime? player) && player.IsEliminated;
    }

    private static bool IsPlayerActiveForBranch(ulong playerId)
    {
        return !IsPlayerEliminated(playerId);
    }

    private static bool HasEliminatedPlayers()
    {
        return Runtime.Players.Values.Any(static player => player.IsEliminated);
    }

    private static IReadOnlyList<Player> GetRouteChoosingPlayers()
    {
        if (_runState == null)
        {
            return Array.Empty<Player>();
        }

        return _runState.Players.Where(player => !IsPlayerEliminated(player.NetId)).ToList();
    }

    private static BranchGroupRuntime? GetBranchForPlayer(ulong playerId)
    {
        return Runtime.ActiveBatch?.FindBranchForPlayer(playerId);
    }

    private static int GetRequiredReadyCount(BranchGroupRuntime branch)
    {
        return branch.PlayerIds.Count(IsPlayerActiveForBranch);
    }

    private static bool ShouldTriggerDeathClear(BranchGroupRuntime? branch)
    {
        if (_runState?.CurrentRoom is not CombatRoom combatRoom || !CombatManager.Instance.IsInProgress)
        {
            return false;
        }

        if (HasDeathClearTriggered(branch))
        {
            return false;
        }

        return combatRoom.CombatState.Players.Count > 0 &&
            combatRoom.CombatState.Players.All(static currentPlayer => currentPlayer.Creature.IsDead);
    }

    private static bool HasDeathClearTriggered(BranchGroupRuntime? branch)
    {
        return branch != null ? branch.DeathClearTriggered : _runState?.RunLocation == _deathClearedCombatLocation;
    }

    private static void MarkDeathClearTriggered(BranchGroupRuntime? branch)
    {
        if (_runState == null)
        {
            return;
        }

        _deathClearedCombatLocation = _runState.RunLocation;
        _suppressedCombatRewardLocation = _runState.RunLocation;
        if (branch != null)
        {
            branch.DeathClearTriggered = true;
            branch.SuppressCombatRewards = true;
        }
    }

    private static bool ShouldSuppressCurrentCombatRewardFlow()
    {
        return _runState != null &&
            _suppressedCombatRewardLocation.HasValue &&
            _runState.RunLocation == _suppressedCombatRewardLocation.Value;
    }

    private static void ApplyHookActivationForLocalBranch()
    {
        if (_runState == null)
        {
            return;
        }

        HashSet<ulong> activeIds = GetActivePlayerIdsForCurrentBranch();
        if (activeIds.Count == 0)
        {
            RestoreAllHookActivation();
            return;
        }

        foreach (Player player in _runState.Players)
        {
            if (activeIds.Contains(player.NetId) && !player.Creature.IsDead)
            {
                player.ActivateHooks();
            }
            else
            {
                player.DeactivateHooks();
            }
        }
    }

    private static void RestoreAllHookActivation()
    {
        if (_runState == null)
        {
            return;
        }

        foreach (Player player in _runState.Players)
        {
            if (player.Creature.IsDead)
            {
                player.DeactivateHooks();
            }
            else
            {
                player.ActivateHooks();
            }
        }
    }
}
