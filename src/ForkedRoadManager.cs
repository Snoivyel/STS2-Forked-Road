using System.Collections;
using System.Collections.Generic;
using System.Linq;
using Godot;
using HarmonyLib;
using MegaCrit.Sts2.Core.Assets;
using MegaCrit.Sts2.Core.Combat;
using MegaCrit.Sts2.Core.Commands;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Actions;
using MegaCrit.Sts2.Core.Entities.Cards;
using MegaCrit.Sts2.Core.Entities.Creatures;
using MegaCrit.Sts2.Core.Entities.Merchant;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Entities.RestSite;
using MegaCrit.Sts2.Core.Entities.UI;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.GameActions;
using MegaCrit.Sts2.Core.GameActions.Multiplayer;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Localization;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Game;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Game.Sync;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Cards;
using MegaCrit.Sts2.Core.Nodes.Combat;
using MegaCrit.Sts2.Core.Nodes.CommonUi;
using MegaCrit.Sts2.Core.Nodes.Events;
using MegaCrit.Sts2.Core.Nodes.GodotExtensions;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Nodes.Rooms;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Models.Powers;
using MegaCrit.Sts2.Core.Models.Singleton;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Odds;
using MegaCrit.Sts2.Core.Rewards;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Runs.History;
using MegaCrit.Sts2.Core.Random;
using MegaCrit.Sts2.Core.Saves.Runs;
using MegaCrit.Sts2.Core.Unlocks;
using MegaCrit.Sts2.Core.ValueProps;

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

    private static int? _renderedSpectatorBranchId;

    private static ulong? _renderedSpectatorPlayerId;

    private static int _renderedSpectatorRevision = -1;

    private static readonly AccessTools.FieldRef<MapSelectionSynchronizer, List<MapVote?>> MapSelectionVotesRef =
        AccessTools.FieldRefAccess<MapSelectionSynchronizer, List<MapVote?>>("_votes");

    private static readonly AccessTools.FieldRef<EventSynchronizer, IPlayerCollection> EventSynchronizerPlayerCollectionRef =
        AccessTools.FieldRefAccess<EventSynchronizer, IPlayerCollection>("_playerCollection");

    private static readonly AccessTools.FieldRef<EventSynchronizer, List<uint?>> EventSynchronizerPlayerVotesRef =
        AccessTools.FieldRefAccess<EventSynchronizer, List<uint?>>("_playerVotes");

    private static readonly AccessTools.FieldRef<EventSynchronizer, uint> EventSynchronizerPageIndexRef =
        AccessTools.FieldRefAccess<EventSynchronizer, uint>("_pageIndex");

    private static readonly AccessTools.FieldRef<EventSynchronizer, EventModel> EventSynchronizerCanonicalEventRef =
        AccessTools.FieldRefAccess<EventSynchronizer, EventModel>("_canonicalEvent");

    private static readonly AccessTools.FieldRef<EventSynchronizer, Rng> EventSynchronizerSharedOptionRngRef =
        AccessTools.FieldRefAccess<EventSynchronizer, Rng>("_multiplayerOptionSelectionRng");

    private static readonly AccessTools.FieldRef<RestSiteSynchronizer, IPlayerCollection> RestSiteSynchronizerPlayerCollectionRef =
        AccessTools.FieldRefAccess<RestSiteSynchronizer, IPlayerCollection>("_playerCollection");

    private static readonly AccessTools.FieldRef<TreasureRoomRelicSynchronizer, IPlayerCollection> TreasureRoomRelicSynchronizerPlayerCollectionRef =
        AccessTools.FieldRefAccess<TreasureRoomRelicSynchronizer, IPlayerCollection>("_playerCollection");

    private static readonly AccessTools.FieldRef<OneOffSynchronizer, IPlayerCollection> OneOffSynchronizerPlayerCollectionRef =
        AccessTools.FieldRefAccess<OneOffSynchronizer, IPlayerCollection>("_playerCollection");

    private static readonly AccessTools.FieldRef<MapSelectionSynchronizer, RunLocation> MapSelectionAcceptingVotesFromSourceRef =
        AccessTools.FieldRefAccess<MapSelectionSynchronizer, RunLocation>("_acceptingVotesFromSource");

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

    private static readonly AccessTools.FieldRef<PlayerCombatState, int> PlayerCombatStateEnergyRef =
        AccessTools.FieldRefAccess<PlayerCombatState, int>("_energy");

    private static readonly AccessTools.FieldRef<PlayerCombatState, int> PlayerCombatStateStarsRef =
        AccessTools.FieldRefAccess<PlayerCombatState, int>("_stars");

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

    private static readonly System.Reflection.MethodInfo EventSynchronizerChooseOptionForSharedEventMethod =
        AccessTools.Method(typeof(EventSynchronizer), "ChooseOptionForSharedEvent");

    private static readonly System.Reflection.MethodInfo? RunManagerAfterLocationChangedMethod =
        AccessTools.Method(typeof(RunManager), "AfterLocationChanged");

    private static readonly System.Reflection.MethodInfo? MultiplayerPlayerStateOnCombatSetUpMethod =
        AccessTools.Method(typeof(NMultiplayerPlayerState), "OnCombatSetUp");

    private static readonly System.Reflection.MethodInfo? MultiplayerPlayerStateRefreshCombatValuesMethod =
        AccessTools.Method(typeof(NMultiplayerPlayerState), "RefreshCombatValues");

    private static readonly System.Reflection.MethodInfo? CreatureNodeUpdateBoundsMethod =
        AccessTools.Method(typeof(NCreature), "UpdateBounds", new[] { typeof(Node) });

    private static readonly System.Reflection.MethodInfo? EventModelSetEventStateMethod =
        AccessTools.Method(typeof(EventModel), "SetEventState");

    private static readonly System.Reflection.FieldInfo? EventModelOwnerBackingField =
        AccessTools.Field(typeof(EventModel), "<Owner>k__BackingField");

    private static readonly System.Reflection.FieldInfo? MerchantRoomInventoryBackingField =
        AccessTools.Field(typeof(MerchantRoom), "<Inventory>k__BackingField");

    private static readonly System.Reflection.FieldInfo PlayerChoiceSynchronizerReceivedChoicesField =
        AccessTools.Field(typeof(PlayerChoiceSynchronizer), "_receivedChoices");

    private static readonly HashSet<RunLocation> SeededSplitLocations = new();

    private static readonly HashSet<RunLocation> SeededMapVoteLocations = new();

    private static int _spectatorTransientEventTextId;

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

    private sealed class SnapshotCombatVisuals : ICombatRoomVisuals
    {
        public EncounterModel Encounter { get; }

        public IEnumerable<Creature> Allies { get; }

        public IEnumerable<Creature> Enemies { get; }

        public ActModel Act { get; }

        public SnapshotCombatVisuals(EncounterModel encounter, IEnumerable<Creature> allies, IEnumerable<Creature> enemies, ActModel act)
        {
            Encounter = encounter;
            Allies = allies.ToList();
            Enemies = enemies.ToList();
            Act = act;
        }
    }

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

        public MapPoint? CurrentMapPoint => _inner.CurrentMapPoint;

        public RunLocation CurrentLocation => _inner.CurrentLocation;

        public RunLocation RunLocation => _inner.CurrentLocation;

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

        public MapPointHistoryEntry? GetHistoryEntryFor(RunLocation location)
        {
            return _inner.GetHistoryEntryFor(location);
        }

        public IEnumerable<AbstractModel> IterateHookListeners(CombatState? childCombatState)
        {
            return _inner.IterateHookListeners(childCombatState);
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

    private static void EnsureCurrentRunLocationVisited()
    {
        if (_runState == null || RunManager.Instance?.RunLocationTargetedBuffer == null)
        {
            return;
        }

        HashSet<RunLocation> visitedLocations = RunLocationVisitedLocationsRef(RunManager.Instance.RunLocationTargetedBuffer);
        visitedLocations.Add(_runState.CurrentLocation);
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

        UnregisterEarlySavedRestoreHandlers(netService);

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
        _renderedSpectatorBranchId = null;
        _renderedSpectatorPlayerId = null;
        _renderedSpectatorRevision = -1;
        Runtime.ActiveBatch = null;
        Runtime.Spectators.Clear();
        Runtime.Players.Clear();
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
        UnregisterEarlySavedRestoreHandlers();
        ResetEarlySavedRestoreMessageBuffer();

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
            HashSet<ulong> fallbackIds = GetCurrentBranchPlayerIds(includeEliminated: true);
            if (fallbackIds.Count == 0)
            {
                return new List<Player>();
            }

            return runState.Players.Where(player => fallbackIds.Contains(player.NetId)).ToList();
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

        HashSet<ulong> relevantIds = GetActivePlayerIdsForCurrentBranch();
        if (relevantIds.Count == 0)
        {
            relevantIds = GetCurrentBranchPlayerIds(includeEliminated: true);
        }

        return relevantIds.Count > 0 && !relevantIds.Contains(senderId);
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

        HashSet<ulong> syncPlayerIds = GetActivePlayerIdsForCurrentBranch();
        if (syncPlayerIds.Count == 0)
        {
            syncPlayerIds = GetCurrentBranchPlayerIds(includeEliminated: true);
            if (syncPlayerIds.Count == 0)
            {
                completionSource.TrySetResult();
                return true;
            }
        }

        Dictionary<ulong, SerializablePlayer> syncData = CombatSyncDataRef(synchronizer);
        foreach (ulong playerId in syncPlayerIds)
        {
            if (!syncData.ContainsKey(playerId))
            {
                return true;
            }
        }

        if (_netService.Type == NetGameType.Client &&
            syncPlayerIds.Count == 1 &&
            LocalContext.NetId.HasValue &&
            syncPlayerIds.Contains(LocalContext.NetId.Value) &&
            _runState?.CurrentRoom is EventRoom &&
            GetLocalBranch()?.RoomType == RoomType.Event)
        {
            Log.Debug("ForkedRoad completing combat sync early for embedded event combat with a single local participant.");
            completionSource.TrySetResult();
            return true;
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

    internal static bool IsReadOnlySpectatingRemoteBranch()
    {
        if (!LocalContext.NetId.HasValue || Runtime.ActiveBatch == null)
        {
            return false;
        }

        return Runtime.Players.TryGetValue(LocalContext.NetId.Value, out PlayerBranchRuntime? player) &&
               player.Phase == RouteSplitPlayerPhase.SpectatingOtherBranch &&
               player.SpectatingBranchId.HasValue &&
               player.SpectatingBranchId != player.CurrentBranchId;
    }

    internal static bool ShouldSuppressLegacyProgressSaveDuringAutosave()
    {
        if (_netService == null)
        {
            return false;
        }

        if (_netService.Type == NetGameType.Singleplayer)
        {
            return false;
        }

        Log.Debug("ForkedRoad suppressing legacy progress.save write during multiplayer runtime to avoid save/transition corruption.");
        return true;
    }

    internal static void HandleActTransition(int nextActIndex)
    {
        if (_runState == null || _netService == null || _netService.Type == NetGameType.Singleplayer)
        {
            return;
        }

        if (!HasStaleActiveBatchForDifferentAct(nextActIndex) &&
            !IsSplitBatchInProgress &&
            !HasDivergedPlayerLocations() &&
            !HasEliminatedPlayers())
        {
            return;
        }

        ResetSplitRuntimeToSharedMap(null, $"act_transition:{_runState.CurrentActIndex}->{nextActIndex}");
    }

    private static bool HasStaleActiveBatchForDifferentAct(int actIndex)
    {
        return Runtime.ActiveBatch != null && Runtime.ActiveBatch.ActIndex != actIndex;
    }

    private static bool HasStaleActiveBatchForCurrentAct()
    {
        return _runState != null && HasStaleActiveBatchForDifferentAct(_runState.CurrentActIndex);
    }

    private static void ResetSplitRuntimeToSharedMap(MapCoord? sharedCoord, string source)
    {
        if (_runState == null)
        {
            return;
        }

        Log.Info($"ForkedRoad resetting split runtime to shared map via {source}: coord={(sharedCoord.HasValue ? sharedCoord.Value.ToString() : "none")} activeBatch={(Runtime.ActiveBatch?.BatchId.ToString() ?? "none")}.");
        _suppressedCombatRewardLocation = null;
        _deathClearedCombatLocation = null;
        Runtime.ActiveBatch = null;
        Runtime.Spectators.Clear();
        Runtime.Phase = RouteSplitRunPhase.SharedMapSelection;
        Runtime.RequiresAuthoritativeRoomPlans = false;
        ClearSeededSplitLocations();
        ClearSeededMapVoteLocations();

        foreach (PlayerBranchRuntime runtimePlayer in Runtime.Players.Values)
        {
            runtimePlayer.CurrentBranchId = null;
            runtimePlayer.SelectionCoord = sharedCoord;
            runtimePlayer.SpectatingBranchId = null;
            runtimePlayer.MapVoteDestinationCoord = null;
            runtimePlayer.IsEliminated = false;
            runtimePlayer.Phase = RouteSplitPlayerPhase.ChoosingRoute;
        }

        foreach (Player runPlayer in _runState.Players)
        {
            if (runPlayer.Creature.IsDead)
            {
                runPlayer.Creature.HealInternal(1m);
            }
        }

        RestoreAllHookActivation();
        if (NMapScreen.Instance != null)
        {
            NMapScreen.Instance.PlayerVoteDictionary.Clear();
            NMapScreen.Instance.RefreshAllMapPointVotes();
        }

        if (_netService?.Type == NetGameType.Host)
        {
            DeleteSaveRestoreSnapshotFile();
        }

        RefreshUiState();
    }

    internal static bool TryOverrideLegacyMultiplayerBlockScaling(Creature target, ValueProp props, ref decimal result)
    {
        CombatState? combatState = CombatManager.Instance.DebugOnlyGetState();
        if (combatState == null || !ShouldUseLegacyCombatPlayerCountForScaling(combatState))
        {
            return false;
        }

        if (target != null && !target.IsPrimaryEnemy && !target.IsSecondaryEnemy)
        {
            result = 1m;
            return true;
        }

        if (!props.HasFlag(ValueProp.Move) || props.HasFlag(ValueProp.Unpowered))
        {
            result = 1m;
            return true;
        }

        int playerCount = combatState.Players.Count;
        if (playerCount <= 1)
        {
            result = 1m;
            return true;
        }

        result = (decimal)playerCount * MultiplayerScalingModel.GetMultiplayerScaling(combatState.Encounter, combatState.RunState.CurrentActIndex);
        return true;
    }

    internal static bool TryOverrideLegacyMultiplayerPowerScaling(PowerModel power, decimal amount, Creature? target, ref decimal result)
    {
        CombatState? combatState = CombatManager.Instance.DebugOnlyGetState();
        if (combatState == null || !ShouldUseLegacyCombatPlayerCountForScaling(combatState))
        {
            return false;
        }

        if (target == null || (target != null && !target.IsPrimaryEnemy && !target.IsSecondaryEnemy))
        {
            result = amount;
            return true;
        }

        if (!power.ShouldScaleInMultiplayer)
        {
            result = amount;
            return true;
        }

        int playerCount = combatState.Players.Count;
        if (playerCount <= 1)
        {
            result = amount;
            return true;
        }

        if (power is ArtifactPower or SlipperyPower or PlatingPower or BufferPower)
        {
            result = (decimal)((playerCount - 1) * 2 + 1) * amount;
            return true;
        }

        result = amount * (decimal)playerCount * MultiplayerScalingModel.GetMultiplayerScaling(combatState.Encounter, combatState.RunState.CurrentActIndex);
        return true;
    }

    private static bool ShouldUseLegacyCombatPlayerCountForScaling(CombatState combatState)
    {
        if (_netService == null || _netService.Type == NetGameType.Singleplayer)
        {
            return false;
        }

        return IsSplitBatchInProgress || HasDivergedPlayerLocations() || combatState.Players.Count != combatState.RunState.Players.Count;
    }

    internal static bool BeforePlayerVotedForMapCoord(MapSelectionSynchronizer synchronizer, Player player, RunLocation source, MapVote? destination)
    {
        NormalizeResolvedMapRuntimeStateIfNeeded();

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
        if (HasStaleActiveBatchForCurrentAct())
        {
            ResetSplitRuntimeToSharedMap(coord, $"stale_batch_on_enter_coord:{coord}");
        }

        if (LocalContext.NetId.HasValue &&
            Runtime.Players.TryGetValue(LocalContext.NetId.Value, out PlayerBranchRuntime? localPlayer) &&
            (IsSplitBatchInProgress || HasDivergedPlayerLocations()))
        {
            localPlayer.SelectionCoord = coord;
            if (!IsSplitBatchInProgress)
            {
                localPlayer.CurrentBranchId = null;
                localPlayer.SpectatingBranchId = null;
                localPlayer.MapVoteDestinationCoord = null;
                localPlayer.Phase = localPlayer.IsEliminated ? RouteSplitPlayerPhase.ReadyForNextBatch : RouteSplitPlayerPhase.ChoosingRoute;
                SeedMapVoteLocationsIfNeeded();
                if (NMapScreen.Instance != null)
                {
                    SyncMapPlayerMarkers(NMapScreen.Instance);
                    NMapScreen.Instance.RefreshAllMapPointVotes();
                }
            }
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

    internal static void TryResolveSharedEventChoiceAsBranchAuthority(EventSynchronizer synchronizer)
    {
        if (!IsSplitBatchInProgress || _netService?.Type != NetGameType.Client || !LocalContext.NetId.HasValue)
        {
            return;
        }

        EventModel? canonicalEvent = EventSynchronizerCanonicalEventRef(synchronizer);
        if (canonicalEvent?.IsShared != true || _runState == null)
        {
            return;
        }

        IReadOnlyList<Player> players = GetActivePlayers(_runState);
        if (players.Count == 0)
        {
            return;
        }

        List<uint?> votes = EventSynchronizerPlayerVotesRef(synchronizer);
        if (votes.Count < players.Count)
        {
            return;
        }

        List<uint?> activeVotes = votes.Take(players.Count).ToList();
        if (activeVotes.Any(static vote => !vote.HasValue))
        {
            return;
        }

        uint pageIndex = EventSynchronizerPageIndexRef(synchronizer);
        uint optionIndex = EventSynchronizerSharedOptionRngRef(synchronizer)
            .NextItem(activeVotes.Select(static vote => vote!.Value))!;

        ApplySharedEventChoiceLocally(synchronizer, optionIndex, pageIndex);
        _netService.SendMessage(new ForkedRoadSharedEventOptionChosenMessage
        {
            optionIndex = optionIndex,
            pageIndex = pageIndex,
            location = RunManager.Instance.RunLocationTargetedBuffer.CurrentLocation
        });
        Log.Debug($"ForkedRoad client resolved shared event option {optionIndex} on page {pageIndex} for location {RunManager.Instance.RunLocationTargetedBuffer.CurrentLocation}.");
    }

    internal static void HandleSharedEventChoiceBroadcast(ForkedRoadSharedEventOptionChosenMessage message)
    {
        if (_runState == null || ShouldIgnoreEventMessage(message.location))
        {
            return;
        }

        EventSynchronizer synchronizer = RunManager.Instance.EventSynchronizer;
        EventModel? canonicalEvent = EventSynchronizerCanonicalEventRef(synchronizer);
        if (canonicalEvent?.IsShared != true)
        {
            return;
        }

        if (EventSynchronizerPageIndexRef(synchronizer) != message.pageIndex)
        {
            return;
        }

        ApplySharedEventChoiceLocally(synchronizer, message.optionIndex, message.pageIndex);
        Log.Debug($"ForkedRoad applied shared event option {message.optionIndex} on page {message.pageIndex} for location {message.location}.");
    }

    private static void ApplySharedEventChoiceLocally(EventSynchronizer synchronizer, uint optionIndex, uint pageIndex)
    {
        if (EventSynchronizerPageIndexRef(synchronizer) != pageIndex)
        {
            return;
        }

        EventSynchronizerChooseOptionForSharedEventMethod.Invoke(synchronizer, new object[] { optionIndex });
    }

    private static void RegisterMessageHandlers(INetGameService netService)
    {
        netService.RegisterMessageHandler<ForkedRoadBatchLockedMessage>(HandleBatchLockedMessage);
        netService.RegisterMessageHandler<ForkedRoadBranchRoomPlanMessage>(HandleBranchRoomPlanMessage);
        netService.RegisterMessageHandler<ForkedRoadBranchRoomEnteredMessage>(HandleBranchRoomEnteredMessage);
        netService.RegisterMessageHandler<ForkedRoadBranchRoomCompletedMessage>(HandleBranchRoomCompletedMessage);
        netService.RegisterMessageHandler<ForkedRoadPlayerEliminatedMessage>(HandlePlayerEliminatedMessage);
        netService.RegisterMessageHandler<ForkedRoadBranchCombatSnapshotMessage>(HandleBranchCombatSnapshotMessage);
        netService.RegisterMessageHandler<ForkedRoadBranchSpectatorStateMessage>(HandleBranchSpectatorStateMessage);
        netService.RegisterMessageHandler<ForkedRoadPlayerSpectateTargetChangedMessage>(HandlePlayerSpectateTargetChangedMessage);
        netService.RegisterMessageHandler<ForkedRoadBatchAllCompletedMessage>(HandleBatchAllCompletedMessage);
        netService.RegisterMessageHandler<ForkedRoadSaveRestoreAvailabilityMessage>(HandleSaveRestoreAvailabilityMessage);
        netService.RegisterMessageHandler<ForkedRoadSaveRestoreStateMessage>(HandleSaveRestoreStateMessage);
        netService.RegisterMessageHandler<ForkedRoadSharedEventOptionChosenMessage>(HandleSharedEventOptionChosenMessage);
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
        netService.UnregisterMessageHandler<ForkedRoadBranchSpectatorStateMessage>(HandleBranchSpectatorStateMessage);
        netService.UnregisterMessageHandler<ForkedRoadPlayerSpectateTargetChangedMessage>(HandlePlayerSpectateTargetChangedMessage);
        netService.UnregisterMessageHandler<ForkedRoadBatchAllCompletedMessage>(HandleBatchAllCompletedMessage);
        netService.UnregisterMessageHandler<ForkedRoadSaveRestoreAvailabilityMessage>(HandleSaveRestoreAvailabilityMessage);
        netService.UnregisterMessageHandler<ForkedRoadSaveRestoreStateMessage>(HandleSaveRestoreStateMessage);
        netService.UnregisterMessageHandler<ForkedRoadSharedEventOptionChosenMessage>(HandleSharedEventOptionChosenMessage);
        _messageHandlersRegistered = false;
    }

    private static void HandleSharedEventOptionChosenMessage(ForkedRoadSharedEventOptionChosenMessage message, ulong senderId)
    {
        HandleSharedEventChoiceBroadcast(message);
    }

    private static void EnsureRuntimePlayers()
    {
        if (_runState == null)
        {
            return;
        }

        HashSet<ulong> currentRunPlayerIds = _runState.Players.Select(static player => player.NetId).ToHashSet();
        foreach (ulong stalePlayerId in Runtime.Players.Keys.Where(playerId => !currentRunPlayerIds.Contains(playerId)).ToList())
        {
            Runtime.Players.Remove(stalePlayerId);
            Runtime.Spectators.Remove(stalePlayerId);
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

    private static HashSet<ulong> GetCurrentBranchPlayerIds(bool includeEliminated)
    {
        if (_runState == null)
        {
            return new HashSet<ulong>();
        }

        if (!IsSplitBatchInProgress || Runtime.ActiveBatch == null)
        {
            return includeEliminated
                ? _runState.Players.Select(static player => player.NetId).ToHashSet()
                : _runState.Players.Where(player => !IsPlayerEliminated(player.NetId)).Select(static player => player.NetId).ToHashSet();
        }

        BranchGroupRuntime? branch = null;
        if (LocalContext.NetId.HasValue &&
            Runtime.Players.TryGetValue(LocalContext.NetId.Value, out PlayerBranchRuntime? localPlayer) &&
            localPlayer.SelectionCoord.HasValue &&
            _runState.CurrentMapCoord.HasValue &&
            localPlayer.SelectionCoord.Value != _runState.CurrentMapCoord.Value)
        {
            branch = GetLocalBranch();
        }

        if (branch == null && _runState.CurrentMapCoord.HasValue)
        {
            branch = Runtime.ActiveBatch.BranchGroups.FirstOrDefault(currentBranch => currentBranch.TargetCoord == _runState.CurrentMapCoord.Value);
        }

        if (branch == null && LocalContext.NetId.HasValue &&
            Runtime.Players.TryGetValue(LocalContext.NetId.Value, out PlayerBranchRuntime? localPlayerForBranch) &&
            localPlayerForBranch.CurrentBranchId.HasValue)
        {
            branch = Runtime.ActiveBatch.FindBranch(localPlayerForBranch.CurrentBranchId.Value);
        }

        branch ??= GetLocalBranch();
        if (branch == null)
        {
            return new HashSet<ulong>();
        }

        return branch.PlayerIds
            .Where(playerId => includeEliminated || IsPlayerActiveForBranch(playerId))
            .ToHashSet();
    }

    internal static IReadOnlyList<Player> GetCombatParticipantsForEntry(IRunState runState)
    {
        HashSet<ulong> participantIds = GetCurrentBranchPlayerIds(includeEliminated: true);
        if (participantIds.Count == 0)
        {
            return runState.Players.ToList();
        }

        return runState.Players.Where(player => participantIds.Contains(player.NetId)).ToList();
    }

    private static HashSet<ulong> GetVisiblePlayerIdsForCurrentView()
    {
        if (_runState == null)
        {
            return new HashSet<ulong>();
        }

        if (Runtime.ActiveBatch != null)
        {
            if (LocalContext.NetId.HasValue &&
                Runtime.Players.TryGetValue(LocalContext.NetId.Value, out PlayerBranchRuntime? localPlayer))
            {
                int? viewedBranchId = localPlayer.SpectatingBranchId ?? localPlayer.CurrentBranchId;
                if (viewedBranchId.HasValue)
                {
                    BranchGroupRuntime? viewedBranch = Runtime.ActiveBatch.FindBranch(viewedBranchId.Value);
                    if (viewedBranch != null)
                    {
                        return viewedBranch.PlayerIds.ToHashSet();
                    }
                }
            }

            if (_runState.CurrentMapCoord.HasValue)
            {
                BranchGroupRuntime? currentRoomBranch = Runtime.ActiveBatch.BranchGroups
                    .FirstOrDefault(branch => branch.TargetCoord == _runState.CurrentMapCoord.Value);
                if (currentRoomBranch != null)
                {
                    return currentRoomBranch.PlayerIds.ToHashSet();
                }
            }
        }

        if (_runState.CurrentMapCoord.HasValue)
        {
            return Runtime.Players.Values
                .Where(player => player.SelectionCoord.HasValue && player.SelectionCoord.Value == _runState.CurrentMapCoord.Value)
                .Select(player => player.PlayerId)
                .ToHashSet();
        }

        return new HashSet<ulong>();
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

    private static void ClearSpectatorOverlayContent()
    {
        _renderedSpectatorBranchId = null;
        _renderedSpectatorRevision = -1;
        _spectatorOverlay?.SetViewContent(null);
    }

    private static void RefreshMultiplayerPlayerVisibility()
    {
        if (NRun.Instance?.GlobalUi?.MultiplayerPlayerContainer == null)
        {
            return;
        }

        NormalizeResolvedMapRuntimeStateIfNeeded();

        HashSet<ulong>? visiblePlayerIds = null;
        if (IsSplitBatchInProgress)
        {
            HashSet<ulong> currentViewPlayerIds = GetVisiblePlayerIdsForCurrentView();
            if (currentViewPlayerIds.Count > 0)
            {
                visiblePlayerIds = currentViewPlayerIds;
            }
        }
        else if (HasEliminatedPlayers())
        {
            visiblePlayerIds = _runState?.Players
                .Where(player => !IsPlayerEliminated(player.NetId))
                .Select(player => player.NetId)
                .ToHashSet();
        }

        foreach (NMultiplayerPlayerState node in NRun.Instance.GlobalUi.MultiplayerPlayerContainer.GetChildren().OfType<NMultiplayerPlayerState>())
        {
            node.Visible = visiblePlayerIds == null || visiblePlayerIds.Contains(node.Player.NetId);
        }
    }

    private static void EnsureSpectatorOverlay()
    {
        if (NRun.Instance == null)
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
        NRun.Instance.AddChild(_spectatorOverlay);
        NRun.Instance.MoveChild(_spectatorOverlay, NRun.Instance.GetChildCount() - 1);
        Log.Info($"ForkedRoad created spectator overlay under run node: parent={_spectatorOverlay.GetParent()?.Name ?? "none"} childIndex={NRun.Instance.GetChildCount() - 1}");
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
            if (NRun.Instance?.GlobalUi != null)
            {
                NRun.Instance.GlobalUi.TopBar.Visible = true;
                NRun.Instance.GlobalUi.MultiplayerPlayerContainer.Visible = true;
            }
            ClearSpectatorOverlayContent();
            _spectatorOverlay.UpdateState(false, string.Empty, string.Empty, false, false);
            return;
        }

        ulong localPlayerId = LocalContext.NetId.Value;
        if (!Runtime.Players.TryGetValue(localPlayerId, out PlayerBranchRuntime? player))
        {
            if (NRun.Instance?.GlobalUi != null)
            {
                NRun.Instance.GlobalUi.TopBar.Visible = true;
                NRun.Instance.GlobalUi.MultiplayerPlayerContainer.Visible = true;
            }
            ClearSpectatorOverlayContent();
            _spectatorOverlay.UpdateState(false, string.Empty, string.Empty, false, false);
            return;
        }

        EnsureLocalSpectatorStateForCurrentPhase(localPlayerId, player);
        EnsureLocalSpectatorStateForCompletedBranch(localPlayerId, player);
        Runtime.Spectators.TryGetValue(localPlayerId, out SpectatorRuntimeState? spectatorState);
        bool hasSpectatorViews = spectatorState?.AvailableBranchIds.Count > 0;
        bool shouldShowOverlay = hasSpectatorViews ||
                                 player.Phase is RouteSplitPlayerPhase.FinishedWaiting or RouteSplitPlayerPhase.SpectatingOtherBranch;
        if (!shouldShowOverlay)
        {
            Log.Info($"ForkedRoad spectator overlay hidden: localPhase={player.Phase} availableViews={(spectatorState?.AvailableBranchIds.Count ?? 0)}");
            if (NRun.Instance?.GlobalUi != null)
            {
                NRun.Instance.GlobalUi.TopBar.Visible = true;
                NRun.Instance.GlobalUi.MultiplayerPlayerContainer.Visible = true;
            }
            ClearSpectatorOverlayContent();
            _spectatorOverlay.UpdateState(false, string.Empty, string.Empty, false, false);
            return;
        }

        string status = $"Batch #{Runtime.ActiveBatch.BatchId} | unresolved={Runtime.ActiveBatch.BranchGroups.Count(group => group.Phase != RouteSplitBranchPhase.Completed)} | localPhase={player.Phase}";
        string currentView = spectatorState?.CurrentBranchId.HasValue == true
            ? $"Current view: branch {spectatorState.CurrentBranchId.Value}"
            : "Current view: waiting / no branch selected";
        string details = string.Join("\n", Runtime.ActiveBatch.BranchGroups
            .OrderBy(group => group.BranchId)
            .Select(group => $"Branch {group.BranchId} @ ({group.TargetCoord.col},{group.TargetCoord.row}) room={group.RoomType?.ToString() ?? "Unknown"} entered={group.EnteredPlayerIds.Count}/{group.PlayerIds.Count} ready={group.ReadyPlayerIds.Count}/{group.PlayerIds.Count} phase={group.Phase}"));
        Log.Info($"ForkedRoad spectator overlay shown: localPhase={player.Phase} currentView={spectatorState?.CurrentBranchId?.ToString() ?? "self"} availableViews={(spectatorState?.AvailableBranchIds.Count ?? 0)}");
        _spectatorOverlay.UpdateState(true, status + "\n" + currentView, details, spectatorState?.CanSwitchLeft == true, spectatorState?.CanSwitchRight == true);
        if (NRun.Instance?.GlobalUi != null)
        {
            NRun.Instance.GlobalUi.TopBar.Visible = true;
            NRun.Instance.GlobalUi.MultiplayerPlayerContainer.Visible = true;
        }
        RefreshSpectatorViewContent(player, spectatorState);
    }

    private static (List<int> branchIds, List<ulong?> playerIds) BuildSpectatorTargets(int localBranchId, ulong localPlayerId, bool includeOnlyUncompletedRemoteBranches)
    {
        List<int> branchIds = new();
        List<ulong?> playerIds = new();

        branchIds.Add(localBranchId);
        playerIds.Add(localPlayerId);

        if (Runtime.ActiveBatch == null)
        {
            return (branchIds, playerIds);
        }

        foreach (BranchGroupRuntime branch in Runtime.ActiveBatch.BranchGroups
                     .Where(branch => branch.BranchId != localBranchId && (!includeOnlyUncompletedRemoteBranches || branch.Phase != RouteSplitBranchPhase.Completed))
                     .OrderBy(branch => branch.BranchId))
        {
            List<ulong> spectatablePlayers = branch.PlayerIds
                .Where(playerId => !IsPlayerEliminated(playerId))
                .ToList();
            if (spectatablePlayers.Count == 0)
            {
                spectatablePlayers = branch.PlayerIds.ToList();
            }

            if (spectatablePlayers.Count == 0)
            {
                branchIds.Add(branch.BranchId);
                playerIds.Add(null);
                continue;
            }

            foreach (ulong playerId in spectatablePlayers)
            {
                branchIds.Add(branch.BranchId);
                playerIds.Add(playerId);
            }
        }

        return (branchIds, playerIds);
    }

    private static void ApplySpectatorTargets(SpectatorRuntimeState state, List<int> availableBranchIds, List<ulong?> availablePlayerIds, int preferredIndex = 0)
    {
        int? previousBranchId = state.CurrentBranchId;
        ulong? previousPlayerId = state.CurrentPlayerId;

        state.AvailableBranchIds = availableBranchIds;
        state.AvailablePlayerIds = availablePlayerIds;
        if (state.AvailableBranchIds.Count == 0)
        {
            state.CurrentViewedBranchIndex = 0;
            return;
        }

        int preservedIndex = -1;
        if (previousBranchId.HasValue)
        {
            for (int index = 0; index < state.AvailableBranchIds.Count; index++)
            {
                ulong? candidatePlayerId = state.AvailablePlayerIds.Count > index ? state.AvailablePlayerIds[index] : null;
                if (state.AvailableBranchIds[index] == previousBranchId.Value && candidatePlayerId == previousPlayerId)
                {
                    preservedIndex = index;
                    break;
                }
            }
        }

        if (preservedIndex >= 0)
        {
            state.CurrentViewedBranchIndex = preservedIndex;
            return;
        }

        state.CurrentViewedBranchIndex = System.Math.Clamp(preferredIndex, 0, state.AvailableBranchIds.Count - 1);
    }

    private static void EnsureLocalSpectatorStateForCompletedBranch(ulong localPlayerId, PlayerBranchRuntime localPlayer)
    {
        if (Runtime.ActiveBatch == null)
        {
            return;
        }

        BranchGroupRuntime? localBranch = Runtime.ActiveBatch.FindBranchForPlayer(localPlayerId);
        if (localBranch == null || !localBranch.ReadyPlayerIds.Contains(localPlayerId))
        {
            return;
        }

        (List<int> availableBranches, List<ulong?> availablePlayers) = BuildSpectatorTargets(localBranch.BranchId, localPlayerId, includeOnlyUncompletedRemoteBranches: true);
        if (availableBranches.Count <= 1)
        {
            return;
        }

        if (!Runtime.Spectators.TryGetValue(localPlayerId, out SpectatorRuntimeState? state))
        {
            int preferredIndex = availableBranches.FindIndex(branchId => branchId != localBranch.BranchId);
            if (preferredIndex < 0)
            {
                preferredIndex = 0;
            }

            state = new SpectatorRuntimeState();
            ApplySpectatorTargets(state, availableBranches, availablePlayers, preferredIndex);
            Runtime.Spectators[localPlayerId] = state;
            Log.Info($"ForkedRoad spectator state synthesized from completed branch readiness: local={localPlayerId} branch={localBranch.BranchId} available=[{string.Join(",", availableBranches.Zip(availablePlayers, (branchId, playerId) => $"{branchId}:{playerId?.ToString() ?? "none"}"))}]");
        }
        else
        {
            ApplySpectatorTargets(state, availableBranches, availablePlayers);
        }

        int viewedBranchId = state.CurrentBranchId ?? localBranch.BranchId;
        if (viewedBranchId == localBranch.BranchId)
        {
            localPlayer.Phase = RouteSplitPlayerPhase.FinishedWaiting;
            localPlayer.SpectatingBranchId = null;
        }
        else
        {
            localPlayer.Phase = RouteSplitPlayerPhase.SpectatingOtherBranch;
            localPlayer.SpectatingBranchId = viewedBranchId;
        }
    }

    private static void EnsureLocalSpectatorStateForCurrentPhase(ulong localPlayerId, PlayerBranchRuntime localPlayer)
    {
        if (Runtime.ActiveBatch == null)
        {
            return;
        }

        if (Runtime.Spectators.ContainsKey(localPlayerId))
        {
            return;
        }

        if (localPlayer.CurrentBranchId is not int currentBranchId)
        {
            return;
        }

        if (localPlayer.Phase is not (RouteSplitPlayerPhase.FinishedWaiting or RouteSplitPlayerPhase.SpectatingOtherBranch))
        {
            return;
        }

        (List<int> availableBranches, List<ulong?> availablePlayers) = BuildSpectatorTargets(currentBranchId, localPlayerId, includeOnlyUncompletedRemoteBranches: true);
        if (availableBranches.Count <= 1)
        {
            return;
        }

        int currentViewedIndex = localPlayer.Phase == RouteSplitPlayerPhase.SpectatingOtherBranch && localPlayer.SpectatingBranchId.HasValue
            ? System.Math.Max(0, availableBranches.IndexOf(localPlayer.SpectatingBranchId.Value))
            : System.Math.Max(0, availableBranches.FindIndex(branchId => branchId != currentBranchId));
        if (currentViewedIndex < 0)
        {
            currentViewedIndex = 0;
        }

        SpectatorRuntimeState newState = new();
        ApplySpectatorTargets(newState, availableBranches, availablePlayers, currentViewedIndex);
        Runtime.Spectators[localPlayerId] = newState;
        Log.Info($"ForkedRoad spectator state synthesized during UI refresh: local={localPlayerId} currentBranch={currentBranchId} available=[{string.Join(",", availableBranches.Zip(availablePlayers, (branchId, playerId) => $"{branchId}:{playerId?.ToString() ?? "none"}"))}] viewedIndex={currentViewedIndex}");
    }

    private static void RefreshSpectatorViewContent(PlayerBranchRuntime player, SpectatorRuntimeState? spectatorState)
    {
        if (_spectatorOverlay == null || Runtime.ActiveBatch == null)
        {
            return;
        }

        int? viewedBranchId = spectatorState?.CurrentBranchId;
        ulong? viewedPlayerId = spectatorState?.CurrentPlayerId;
        if (!viewedBranchId.HasValue || viewedBranchId.Value == player.CurrentBranchId)
        {
            _renderedSpectatorBranchId = null;
            _renderedSpectatorPlayerId = null;
            _renderedSpectatorRevision = -1;
            _spectatorOverlay.SetViewContent(null);
            return;
        }

        BranchGroupRuntime? branch = Runtime.ActiveBatch.FindBranch(viewedBranchId.Value);
        if (branch == null)
        {
            _renderedSpectatorPlayerId = null;
            _spectatorOverlay.SetViewContent(null);
            return;
        }

        int revision = branch.LatestViewState?.Revision ?? 0;
        if (branch.LatestCombatState != null)
        {
            revision += 1000000;
        }

        if (_renderedSpectatorBranchId == branch.BranchId &&
            _renderedSpectatorPlayerId == viewedPlayerId &&
            _renderedSpectatorRevision == revision)
        {
            return;
        }

        _renderedSpectatorBranchId = branch.BranchId;
        _renderedSpectatorPlayerId = viewedPlayerId;
        _renderedSpectatorRevision = revision;
        _spectatorOverlay.SetViewContent(CreateSpectatorViewControl(branch, viewedPlayerId));
    }

    private static Control CreateSpectatorViewControl(BranchGroupRuntime branch, ulong? viewedPlayerId)
    {
        Control? fullScreenView = CreateSpectatorFullScreenView(branch, viewedPlayerId);
        return fullScreenView ?? CreateSpectatorFallbackLabel(branch.LatestViewState?.Description ?? $"Branch {branch.BranchId}");
    }

    private static Control? CreateSpectatorFullScreenView(BranchGroupRuntime branch, ulong? viewedPlayerId)
    {
        BranchSpectatorViewState? state = branch.LatestViewState;
        if (branch.LatestCombatState != null && branch.EncounterId != null)
        {
            return CreateSpectatorCombatSnapshotNode(branch, viewedPlayerId);
        }

        return branch.RoomType switch
        {
            RoomType.Event => CreateSpectatorEventView(branch, viewedPlayerId),
            RoomType.Shop => CreateSpectatorMerchantView(branch, viewedPlayerId),
            RoomType.RestSite => CreateSpectatorRestSiteView(branch, viewedPlayerId),
            RoomType.Treasure => CreateSpectatorTreasureView(branch, viewedPlayerId),
            _ => state?.Kind switch
            {
                SpectatorViewKind.Event => CreateSpectatorEventView(branch, viewedPlayerId),
                SpectatorViewKind.Treasure => CreateSpectatorTreasureView(branch, viewedPlayerId),
                _ => null
            }
        };
    }

    private static Control? CreateSpectatorCombatSnapshotNode(BranchGroupRuntime branch, ulong? viewedPlayerId)
    {
        if (_runState == null || branch.LatestCombatState == null || branch.EncounterId == null || !LocalContext.NetId.HasValue)
        {
            return null;
        }

        try
        {
            NetFullCombatState snapshot = branch.LatestCombatState;
            Dictionary<ulong, NetFullCombatState.CreatureState> allyCreatureStates = snapshot.Creatures
                .Take(System.Math.Clamp(branch.AlliedCreatureCount, 0, snapshot.Creatures.Count))
                .Where(static state => state.playerId.HasValue)
                .ToDictionary(state => state.playerId!.Value);
            NetFullCombatState.PlayerState? primaryState = ResolvePrimarySpectatorPlayerState(branch, snapshot, viewedPlayerId);
            if (primaryState == null)
            {
                return null;
            }
            NetFullCombatState.PlayerState primaryPlayerState = primaryState.Value;

            Dictionary<ulong, Player> actualPlayersById = _runState.Players.ToDictionary(player => player.NetId);
            if (!actualPlayersById.ContainsKey(primaryPlayerState.playerId) ||
                !allyCreatureStates.ContainsKey(primaryPlayerState.playerId))
            {
                return null;
            }

            EncounterModel encounter = ModelDb.GetById<EncounterModel>(branch.EncounterId).ToMutable();
            if (!encounter.HaveMonstersBeenGenerated)
            {
                encounter.GenerateMonstersWithSlots(_runState);
            }

            CombatState combatState = new(encounter, _runState, _runState.Modifiers, _runState.MultiplayerScalingModel);
            Dictionary<ulong, Player> roomPlayersByOriginalId = new();
            foreach (NetFullCombatState.PlayerState playerState in snapshot.Players)
            {
                if (!actualPlayersById.TryGetValue(playerState.playerId, out Player? actualPlayer) ||
                    !allyCreatureStates.TryGetValue(playerState.playerId, out NetFullCombatState.CreatureState creatureState))
                {
                    continue;
                }

                ulong clonedNetId = playerState.playerId == primaryPlayerState.playerId ? LocalContext.NetId.Value : playerState.playerId;
                Player clonedPlayer = CloneSpectatorPlayer(actualPlayer, clonedNetId);
                combatState.AddPlayer(clonedPlayer);
                ApplySpectatorCreatureSnapshot(clonedPlayer.Creature, creatureState);
                PopulateSpectatorPlayerCombatSnapshot(clonedPlayer, playerState, combatState);
                roomPlayersByOriginalId[playerState.playerId] = clonedPlayer;
            }

            if (!roomPlayersByOriginalId.TryGetValue(primaryPlayerState.playerId, out Player? uiPlayer))
            {
                return null;
            }

            IReadOnlyList<NetFullCombatState.CreatureState> creatures = snapshot.Creatures;
            int alliedCount = System.Math.Clamp(branch.AlliedCreatureCount, 0, creatures.Count);
            List<NetFullCombatState.CreatureState> allyStates = creatures.Take(alliedCount).ToList();
            List<NetFullCombatState.CreatureState> enemyStates = creatures.Skip(alliedCount).ToList();

            Player? fallbackPetOwner = roomPlayersByOriginalId.GetValueOrDefault(primaryPlayerState.playerId) ?? roomPlayersByOriginalId.Values.FirstOrDefault();
            foreach (NetFullCombatState.CreatureState allyState in allyStates.Where(static state => !state.playerId.HasValue && state.monsterId != null))
            {
                MonsterModel monster = ModelDb.GetById<MonsterModel>(allyState.monsterId!).ToMutable();
                Creature creature = combatState.CreateCreature(monster, CombatSide.Player, null);
                combatState.AddCreature(creature);
                if (fallbackPetOwner != null)
                {
                    creature.PetOwner = fallbackPetOwner;
                }
                ApplySpectatorCreatureSnapshot(creature, allyState);
            }

            Dictionary<ModelId, Queue<string?>> availableSlotsByMonsterId = encounter.MonstersWithSlots
                .GroupBy(tuple => tuple.Item1.Id)
                .ToDictionary(
                    grouping => grouping.Key,
                    grouping => new Queue<string?>(grouping.Select(tuple => tuple.Item2)));

            foreach (NetFullCombatState.CreatureState enemyState in enemyStates.Where(static state => state.monsterId != null))
            {
                ModelId monsterId = enemyState.monsterId!;
                MonsterModel monster = ModelDb.GetById<MonsterModel>(monsterId).ToMutable();
                string? slotName = null;
                if (availableSlotsByMonsterId.TryGetValue(monsterId, out Queue<string?>? slots) && slots.Count > 0)
                {
                    slotName = slots.Dequeue();
                }

                Creature creature = combatState.CreateCreature(monster, CombatSide.Enemy, slotName);
                combatState.AddCreature(creature);
                ApplySpectatorCreatureSnapshot(creature, enemyState);
            }

            combatState.SortEnemiesBySlotName();

            List<Player> sidebarPlayers = new();
            foreach (NetFullCombatState.PlayerState playerState in snapshot.Players)
            {
                if (!actualPlayersById.TryGetValue(playerState.playerId, out Player? actualPlayer) ||
                    !allyCreatureStates.TryGetValue(playerState.playerId, out NetFullCombatState.CreatureState creatureState))
                {
                    continue;
                }

                Player sidebarPlayer = CloneSpectatorPlayer(actualPlayer, playerState.playerId);
                ApplySpectatorCreatureSnapshot(sidebarPlayer.Creature, creatureState);
                PopulateSpectatorPlayerCombatSnapshot(sidebarPlayer, playerState, null);
                sidebarPlayers.Add(sidebarPlayer);
            }

            NCombatRoom? room = NCombatRoom.Create(
                new SnapshotCombatVisuals(encounter, combatState.Allies, combatState.Enemies, _runState.Act),
                CombatRoomMode.VisualOnly);
            if (room == null)
            {
                return null;
            }

            room.Name = $"ForkedRoadSpectatorCombat_{branch.BranchId}";
            room.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            room.OffsetLeft = 0f;
            room.OffsetTop = 0f;
            room.OffsetRight = 0f;
            room.OffsetBottom = 0f;
            room.MouseFilter = Control.MouseFilterEnum.Ignore;

            Control root = new()
            {
                Name = $"ForkedRoadSpectatorCombatRoot_{branch.BranchId}",
                MouseFilter = Control.MouseFilterEnum.Ignore,
                SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
                SizeFlagsVertical = Control.SizeFlags.ExpandFill
            };
            root.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            root.AddChild(room);
            Log.Info($"ForkedRoad spectator combat snapshot prepared: branch={branch.BranchId} primary={primaryPlayerState.playerId} roomPlayers={roomPlayersByOriginalId.Count} enemies={enemyStates.Count} hand={(uiPlayer.PlayerCombatState?.Hand.Cards.Count ?? 0)}.");

            void ConfigureRoom()
            {
                if (!room.IsInsideTree())
                {
                    return;
                }

                if (room.Background == null && _runState != null)
                {
                    room.SetUpBackground(_runState);
                }

                room.Ui.Activate(combatState);
                room.SetWaitingForOtherPlayersOverlayVisible(false);
                PopulateSpectatorHandUi(room.Ui, uiPlayer);
                RefreshSpectatorCombatRoomLayout(room);
                Log.Info($"ForkedRoad spectator combat ui activated: branch={branch.BranchId} uiPlayer={uiPlayer.NetId} hand={(uiPlayer.PlayerCombatState?.Hand.Cards.Count ?? 0)} creatures={combatState.Creatures.Count}.");
            }

            if (room.IsNodeReady())
            {
                ConfigureRoom();
            }
            else
            {
                room.Connect(Node.SignalName.Ready, Callable.From(ConfigureRoom));
            }

            Log.Info($"ForkedRoad created spectator combat room for branch {branch.BranchId} encounter={branch.EncounterId.Entry} primaryPlayer={primaryPlayerState.playerId}.");
            return root;
        }
        catch (System.Exception ex)
        {
            Log.Warn($"ForkedRoad failed to create spectator combat snapshot node for branch {branch.BranchId}: {ex}");
            return null;
        }
    }

    private static void ApplySpectatorCreatureSnapshot(Creature creature, NetFullCombatState.CreatureState state)
    {
        creature.Reset();
        creature.SetMaxHpInternal(state.maxHp);
        creature.SetCurrentHpInternal(state.currentHp);
        if (state.block > 0)
        {
            creature.GainBlockInternal(state.block);
        }

        foreach (NetFullCombatState.PowerState powerState in state.powers)
        {
            try
            {
                PowerModel power = (PowerModel)ModelDb.GetById<PowerModel>(powerState.id).MutableClone();
                power.Applier = creature;
                power.Target = creature;
                power.ApplyInternal(creature, powerState.amount, silent: true);
            }
            catch (System.Exception ex)
            {
                Log.Warn($"ForkedRoad failed to apply spectator power {powerState.id} to {creature.ModelId}: {ex}");
            }
        }
    }

    private static NetFullCombatState.PlayerState? ResolvePrimarySpectatorPlayerState(BranchGroupRuntime branch, NetFullCombatState snapshot, ulong? preferredPlayerId)
    {
        if (snapshot.Players.Count == 0)
        {
            return null;
        }

        if (preferredPlayerId.HasValue)
        {
            int preferredIndex = snapshot.Players.FindIndex(player => player.playerId == preferredPlayerId.Value);
            if (preferredIndex >= 0)
            {
                return snapshot.Players[preferredIndex];
            }
        }

        int authorityIndex = snapshot.Players.FindIndex(player => player.playerId == branch.AuthorityPlayerId);
        if (authorityIndex >= 0)
        {
            return snapshot.Players[authorityIndex];
        }

        return snapshot.Players[0];
    }

    private static Player CloneSpectatorPlayer(Player actualPlayer, ulong netId)
    {
        SerializablePlayer serializable = actualPlayer.ToSerializable();
        serializable.NetId = netId;
        Player clone = Player.FromSerializable(serializable);
        clone.RunState = _runState!;
        clone.ActivateHooks();
        return clone;
    }

    private static Player? ResolveViewedPlayerForBranch(BranchGroupRuntime branch, ulong? viewedPlayerId)
    {
        if (_runState == null)
        {
            return null;
        }

        ulong? preferredId = viewedPlayerId;
        if (preferredId.HasValue && branch.PlayerIds.Contains(preferredId.Value))
        {
            return _runState.GetPlayer(preferredId.Value);
        }

        if (branch.PlayerIds.Contains(branch.AuthorityPlayerId))
        {
            return _runState.GetPlayer(branch.AuthorityPlayerId);
        }

        if (branch.PlayerIds.Count == 0)
        {
            return null;
        }

        return _runState.GetPlayer(branch.PlayerIds[0]);
    }

    private static void PopulateSpectatorPlayerCombatSnapshot(Player player, NetFullCombatState.PlayerState playerState, CombatState? combatState)
    {
        player.Gold = playerState.gold;
        player.ResetCombatState();
        if (player.PlayerCombatState == null)
        {
            return;
        }

        PlayerCombatStateEnergyRef(player.PlayerCombatState) = playerState.energy;
        PlayerCombatStateStarsRef(player.PlayerCombatState) = playerState.stars;

        foreach (NetFullCombatState.CombatPileState pileState in playerState.piles)
        {
            CardPile? pile = CardPile.Get(pileState.pileType, player);
            if (pile == null)
            {
                continue;
            }

            pile.Clear(silent: true);
            foreach (NetFullCombatState.CardState cardState in pileState.cards)
            {
                CardModel card = CardModel.FromSerializable(cardState.card);
                if (combatState != null)
                {
                    combatState.AddCard(card, player);
                }
                pile.AddInternal(card, -1, silent: true);
            }
        }
    }

    private static void PopulateSpectatorHandUi(NCombatUi ui, Player uiPlayer)
    {
        if (uiPlayer.PlayerCombatState == null)
        {
            return;
        }

        int added = 0;
        foreach (CardModel card in uiPlayer.PlayerCombatState.Hand.Cards)
        {
            NCard? cardNode = NCard.Create(card);
            if (cardNode != null)
            {
                ui.Hand.Add(cardNode);
                added++;
            }
        }
        Log.Info($"ForkedRoad populated spectator hand ui for player {uiPlayer.NetId}: cards={added}.");
    }

    private static string GetSpectatorPlayerName(ulong playerId)
    {
        Player? player = _runState?.GetPlayer(playerId);
        string baseName = player?.Character.Title.GetFormattedText() ?? "Player";
        return $"{baseName} #{playerId % 10000}";
    }

    private static void RefreshSpectatorCombatRoomLayout(NCombatRoom room)
    {
        foreach (NCreature creatureNode in room.CreatureNodes)
        {
            try
            {
                CreatureNodeUpdateBoundsMethod?.Invoke(creatureNode, new object[] { creatureNode.Visuals });
                NCreatureStateDisplay? stateDisplay = creatureNode.GetNodeOrNull<NCreatureStateDisplay>("%HealthBar");
                if (stateDisplay != null)
                {
                    stateDisplay.SetCreatureBounds(creatureNode.Hitbox);
                    stateDisplay.AnimateIn(HealthBarAnimMode.FromHidden);
                }
            }
            catch (System.Exception ex)
            {
                Log.Warn($"ForkedRoad failed to refresh spectator creature layout for {creatureNode.Entity.ModelId}: {ex}");
            }
        }
    }

    private static Control CreateSpectatorCombatSidebar(IReadOnlyList<Player> players, CombatState combatState)
    {
        MarginContainer margin = new()
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            AnchorLeft = 0f,
            AnchorRight = 0f,
            AnchorTop = 0f,
            AnchorBottom = 1f,
            OffsetLeft = 18f,
            OffsetRight = 260f,
            OffsetTop = 140f,
            OffsetBottom = -120f
        };

        ScrollContainer scroll = new()
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        margin.AddChild(scroll);

        VBoxContainer stack = new()
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill
        };
        stack.AddThemeConstantOverride("separation", 8);
        scroll.AddChild(stack);

        foreach (Player player in players)
        {
            NMultiplayerPlayerState stateNode = NMultiplayerPlayerState.Create(player);
            stateNode.CustomMinimumSize = new Vector2(280f, 120f);
            stateNode.SizeFlagsHorizontal = Control.SizeFlags.ExpandFill;
            stateNode.MouseFilter = Control.MouseFilterEnum.Ignore;
            stack.AddChild(stateNode);

            void ActivateSnapshotStateNode()
            {
                if (!stateNode.IsInsideTree())
                {
                    return;
                }

                MultiplayerPlayerStateOnCombatSetUpMethod?.Invoke(stateNode, new object[] { combatState });
                MultiplayerPlayerStateRefreshCombatValuesMethod?.Invoke(stateNode, null);
            }

            if (stateNode.IsNodeReady())
            {
                ActivateSnapshotStateNode();
            }
            else
            {
                stateNode.Connect(Node.SignalName.Ready, Callable.From(ActivateSnapshotStateNode));
            }
        }

        return margin;
    }

    private static Control? CreateSpectatorEventView(BranchGroupRuntime branch, ulong? viewedPlayerId)
    {
        if (_runState == null || branch.ResolvedModelId == null)
        {
            return null;
        }

        BranchSpectatorViewState? state = branch.LatestViewState;
        try
        {
            Player? viewedPlayer = ResolveViewedPlayerForBranch(branch, viewedPlayerId);
            if (viewedPlayer == null)
            {
                return null;
            }

            IRunState scopedRunState = CreateScopedRunStateForBranch(branch);
            EventModel canonicalEvent = ModelDb.GetById<EventModel>(branch.ResolvedModelId);
            EventModel eventModel = canonicalEvent.ToMutable();
            InitializeSpectatorEventModel(eventModel, viewedPlayer);
            ApplySpectatorEventStateToEventModel(eventModel, state, branch.BranchId);
            NEventRoom? room = NEventRoom.Create(eventModel, scopedRunState, isPreFinished: false);
            if (room == null)
            {
                return null;
            }

            room.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            room.OffsetLeft = 0f;
            room.OffsetTop = 0f;
            room.OffsetRight = 0f;
            room.OffsetBottom = 0f;
            room.MouseFilter = Control.MouseFilterEnum.Ignore;
            Log.Info($"ForkedRoad created spectator event view for branch {branch.BranchId} event={branch.ResolvedModelId} player={viewedPlayer.NetId}.");
            return room;
        }
        catch (System.Exception ex)
        {
            Log.Warn($"ForkedRoad failed to create spectator event view for branch {branch.BranchId}: {ex}");
            return null;
        }
    }

    private static void InitializeSpectatorEventModel(EventModel eventModel, Player viewedPlayer)
    {
        if (EventModelOwnerBackingField == null)
        {
            throw new System.InvalidOperationException("Spectator event owner backing field was not found.");
        }

        EventModelOwnerBackingField.SetValue(eventModel, viewedPlayer);
    }

    private static void ApplySpectatorEventStateToEventModel(EventModel eventModel, BranchSpectatorViewState? state, int branchId)
    {
        if (state == null || EventModelSetEventStateMethod == null)
        {
            return;
        }

        LocString descriptionLoc = CreateTransientSpectatorEventLocString(
            $"forkedroad.spectator.branch{branchId}.description",
            string.IsNullOrWhiteSpace(state.Description)
                ? GetSafeSpectatorRawText(eventModel.Description, string.Empty)
                : state.Description);
        EventModelSetEventStateMethod.Invoke(eventModel, new object[] { descriptionLoc, CreateSpectatorEventOptions(eventModel, state, branchId) });
    }

    private static List<MegaCrit.Sts2.Core.Events.EventOption> CreateSpectatorEventOptions(EventModel eventModel, BranchSpectatorViewState state, int branchId)
    {
        List<MegaCrit.Sts2.Core.Events.EventOption> options = new();
        for (int i = 0; i < state.Options.Count; i++)
        {
            string optionTitle = state.Options[i];
            string optionDescription = i < state.OptionDescriptions.Count ? state.OptionDescriptions[i] : string.Empty;
            string optionKey = RegisterTransientSpectatorEventOptionText(branchId, i, optionTitle, optionDescription);
            bool isProceed = IsProceedSpectatorOption(optionTitle);
            options.Add(new MegaCrit.Sts2.Core.Events.EventOption(
                eventModel,
                static () => System.Threading.Tasks.Task.CompletedTask,
                optionKey,
                disableOnChosen: false,
                isProceed: isProceed));
        }

        return options;
    }

    private static bool IsProceedSpectatorOption(string optionTitle)
    {
        string proceedText = NProceedButton.ProceedLoc.GetFormattedText();
        return string.Equals(optionTitle?.Trim(), proceedText, System.StringComparison.OrdinalIgnoreCase);
    }

    private static string RegisterTransientSpectatorEventOptionText(int branchId, int optionIndex, string title, string description)
    {
        int textId = ++_spectatorTransientEventTextId;
        string keyBase = $"forkedroad.spectator.branch{branchId}.option{optionIndex}.{textId}";
        LocManager.Instance.GetTable("events").MergeWith(new Dictionary<string, string>
        {
            [$"{keyBase}.title"] = EscapeSpectatorLocText(title),
            [$"{keyBase}.description"] = EscapeSpectatorLocText(description)
        });
        return keyBase;
    }

    private static LocString CreateTransientSpectatorEventLocString(string keyPrefix, string text)
    {
        string key = $"{keyPrefix}.{++_spectatorTransientEventTextId}";
        LocManager.Instance.GetTable("events").MergeWith(new Dictionary<string, string>
        {
            [key] = EscapeSpectatorLocText(text)
        });
        return new LocString("events", key);
    }

    private static string EscapeSpectatorLocText(string? text)
    {
        if (string.IsNullOrEmpty(text))
        {
            return string.Empty;
        }

        return text.Replace("{", "{{").Replace("}", "}}");
    }

    private static Control? CreateSpectatorMerchantView(BranchGroupRuntime branch, ulong? viewedPlayerId)
    {
        if (_runState == null || !LocalContext.NetId.HasValue)
        {
            return null;
        }

        try
        {
            Player? viewedPlayer = ResolveViewedPlayerForBranch(branch, viewedPlayerId);
            if (viewedPlayer == null)
            {
                return null;
            }

            Player localShopPlayer = CloneSpectatorPlayer(viewedPlayer, LocalContext.NetId.Value);
            MerchantInventory inventory = MerchantInventory.CreateForNormalMerchant(localShopPlayer);
            MerchantRoom roomModel = new();
            MerchantRoomInventoryBackingField?.SetValue(roomModel, inventory);

            List<Player> visualPlayers = new() { localShopPlayer };
            foreach (ulong playerId in branch.PlayerIds.Where(id => id != viewedPlayer.NetId))
            {
                Player? player = _runState.GetPlayer(playerId);
                if (player != null)
                {
                    visualPlayers.Add(CloneSpectatorPlayer(player, player.NetId));
                }
            }

            NMerchantRoom? room = NMerchantRoom.Create(roomModel, visualPlayers);
            if (room == null)
            {
                return null;
            }

            room.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            room.OffsetLeft = 0f;
            room.OffsetTop = 0f;
            room.OffsetRight = 0f;
            room.OffsetBottom = 0f;
            room.MouseFilter = Control.MouseFilterEnum.Ignore;
            Log.Info($"ForkedRoad created spectator merchant view for branch {branch.BranchId} player={viewedPlayer.NetId}.");
            return room;
        }
        catch (System.Exception ex)
        {
            Log.Warn($"ForkedRoad failed to create spectator merchant view for branch {branch.BranchId}: {ex}");
            return null;
        }
    }

    private static Control? CreateSpectatorRestSiteView(BranchGroupRuntime branch, ulong? viewedPlayerId)
    {
        if (_runState == null || !LocalContext.NetId.HasValue)
        {
            return null;
        }

        try
        {
            Player? viewedPlayer = ResolveViewedPlayerForBranch(branch, viewedPlayerId);
            if (viewedPlayer == null)
            {
                return null;
            }

            Player localRestPlayer = CloneSpectatorPlayer(viewedPlayer, LocalContext.NetId.Value);
            List<Player> visualPlayers = new() { localRestPlayer };
            foreach (ulong playerId in branch.PlayerIds.Where(id => id != viewedPlayer.NetId))
            {
                Player? player = _runState.GetPlayer(playerId);
                if (player != null)
                {
                    visualPlayers.Add(CloneSpectatorPlayer(player, player.NetId));
                }
            }

            RunState visualRunState = RunState.CreateForTest(visualPlayers, _runState.Acts.Select(act => act.ToMutable()).ToList(), _runState.Modifiers.ToList(), _runState.AscensionLevel, _runState.Rng.StringSeed);
            visualRunState.CurrentActIndex = _runState.CurrentActIndex;
            RestSiteRoom roomModel = new();
            NRestSiteRoom? room = NRestSiteRoom.Create(roomModel, visualRunState);
            if (room == null)
            {
                return null;
            }

            room.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            room.OffsetLeft = 0f;
            room.OffsetTop = 0f;
            room.OffsetRight = 0f;
            room.OffsetBottom = 0f;
            room.MouseFilter = Control.MouseFilterEnum.Ignore;
            Log.Info($"ForkedRoad created spectator rest-site view for branch {branch.BranchId} player={viewedPlayer.NetId}.");
            return room;
        }
        catch (System.Exception ex)
        {
            Log.Warn($"ForkedRoad failed to create spectator rest-site view for branch {branch.BranchId}: {ex}");
            return null;
        }
    }

    private static Control? CreateSpectatorTreasureView(BranchGroupRuntime branch, ulong? viewedPlayerId)
    {
        if (_runState == null)
        {
            return null;
        }

        try
        {
            IRunState scopedRunState = CreateScopedRunStateForBranch(branch);
            NTreasureRoom? room = NTreasureRoom.Create(new TreasureRoom(_runState.CurrentActIndex), scopedRunState);
            if (room == null)
            {
                return null;
            }

            BranchSpectatorViewState? state = branch.LatestViewState;
            room.SetAnchorsPreset(Control.LayoutPreset.FullRect);
            room.OffsetLeft = 0f;
            room.OffsetTop = 0f;
            room.OffsetRight = 0f;
            room.OffsetBottom = 0f;
            room.MouseFilter = Control.MouseFilterEnum.Ignore;
            if (room.IsNodeReady())
            {
                ConfigureSpectatorTreasureView(room, state);
            }
            else
            {
                room.Connect(Node.SignalName.Ready, Callable.From(() => ConfigureSpectatorTreasureView(room, state)));
            }
            Log.Info($"ForkedRoad created spectator treasure view for branch {branch.BranchId}.");
            return room;
        }
        catch (System.Exception ex)
        {
            Log.Warn($"ForkedRoad failed to create spectator treasure view for branch {branch.BranchId}: {ex.Message}");
            return null;
        }
    }

    private static void ConfigureSpectatorTreasureView(NTreasureRoom room, BranchSpectatorViewState? state)
    {
        if (!room.IsInsideTree())
        {
            return;
        }

        bool relicSelectionOpen = (state?.Description?.Contains("selection", System.StringComparison.OrdinalIgnoreCase) ?? false) ||
                                  (state?.Options.Any(option => option.Contains("Choosing", System.StringComparison.OrdinalIgnoreCase)) ?? false);
        bool proceedVisible = (state?.Description?.Contains("Proceed", System.StringComparison.OrdinalIgnoreCase) ?? false) ||
                              (state?.Options.Any(option => option.Contains("Proceed", System.StringComparison.OrdinalIgnoreCase)) ?? false);

        if (room.GetNodeOrNull<NButton>("%Chest") is NButton chestButton)
        {
            chestButton.Disable();
            chestButton.MouseFilter = Control.MouseFilterEnum.Ignore;
        }

        if (room.GetNodeOrNull<NProceedButton>("%ProceedButton") is NProceedButton proceedButton)
        {
            proceedButton.Visible = proceedVisible;
            proceedButton.Disable();
            proceedButton.MouseFilter = Control.MouseFilterEnum.Ignore;
        }

        if (room.GetNodeOrNull<Control>("%RelicCollection") is Control relicCollection)
        {
            relicCollection.Visible = relicSelectionOpen;
            if (relicSelectionOpen && relicCollection.GetChildCount() == 0)
            {
                relicCollection.AddChild(CreateSpectatorCenteredStatusLabel(state?.Description ?? "Choosing relic..."));
            }
        }

        if (relicSelectionOpen)
        {
            room.AddChild(CreateSpectatorBottomStatusLabel(state?.Description ?? "Choosing relic..."));
        }
    }

    private static IRunState CreateScopedRunStateForBranch(BranchGroupRuntime branch)
    {
        if (_runState == null)
        {
            throw new System.InvalidOperationException("Run state unavailable for spectator view.");
        }

        IReadOnlyList<Player> players = _runState.Players.Where(player => branch.PlayerIds.Contains(player.NetId)).ToList();
        if (players.Count == 0 || players.Count == _runState.Players.Count)
        {
            return _runState;
        }

        return new BranchScopedRunState(_runState, players);
    }

    private static Control CreateSpectatorCenteredStatusLabel(string text)
    {
        CenterContainer container = new()
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        container.AddChild(new Label
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = Control.MouseFilterEnum.Ignore
        });
        return container;
    }

    private static Control CreateSpectatorBottomStatusLabel(string text)
    {
        MarginContainer margin = new()
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            AnchorLeft = 0f,
            AnchorRight = 1f,
            AnchorTop = 1f,
            AnchorBottom = 1f,
            OffsetLeft = 120f,
            OffsetRight = -120f,
            OffsetTop = -120f,
            OffsetBottom = -40f
        };
        PanelContainer panel = new()
        {
            MouseFilter = Control.MouseFilterEnum.Ignore
        };
        margin.AddChild(panel);
        MarginContainer inner = new();
        inner.SetAnchorsPreset(Control.LayoutPreset.FullRect);
        inner.OffsetLeft = 16f;
        inner.OffsetTop = 12f;
        inner.OffsetRight = -16f;
        inner.OffsetBottom = -12f;
        panel.AddChild(inner);
        inner.AddChild(new Label
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart,
            MouseFilter = Control.MouseFilterEnum.Ignore
        });
        return margin;
    }

    private static Control CreateSpectatorFallbackLabel(string text)
    {
        CenterContainer container = new()
        {
            MouseFilter = Control.MouseFilterEnum.Ignore,
            SizeFlagsHorizontal = Control.SizeFlags.ExpandFill,
            SizeFlagsVertical = Control.SizeFlags.ExpandFill
        };
        container.AddChild(new Label
        {
            Text = text,
            HorizontalAlignment = HorizontalAlignment.Center,
            AutowrapMode = TextServer.AutowrapMode.WordSmart
        });
        return container;
    }

    internal static string GetSafeSpectatorText(LocString? value, string fallback)
    {
        if (value == null)
        {
            return fallback;
        }

        try
        {
            if (!value.Exists())
            {
                return fallback;
            }

            return value.GetFormattedText();
        }
        catch
        {
            return fallback;
        }
    }

    internal static string GetSafeSpectatorRawText(LocString? value, string fallback)
    {
        if (value == null)
        {
            return fallback;
        }

        try
        {
            if (!value.Exists())
            {
                return fallback;
            }

            return value.GetRawText();
        }
        catch
        {
            return fallback;
        }
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
            IRunState scopedRunState = ScopeRunStateToLocalBranch(_runState);
            NetFullCombatState snapshot = NetFullCombatState.FromRun(scopedRunState, null);
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
            Log.Debug($"ForkedRoad broadcast combat snapshot for branch {branch.BranchId} via {source}: players={snapshot.Players.Count} creatures={snapshot.Creatures.Count} allied={message.alliedCreatureCount}.");
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
        branch.LatestViewState = new BranchSpectatorViewState
        {
            Kind = SpectatorViewKind.Combat,
            Title = message.encounterId.Entry,
            Description = $"Combat branch {message.branchId}",
            Options = new List<string>(),
            OptionDescriptions = new List<string>(),
            IsInteractionBlocked = true,
            Revision = branch.LatestViewState?.Revision + 1 ?? 1
        };
        Log.Info($"ForkedRoad stored combat snapshot for branch {branch.BranchId}: sender={senderId} players={message.snapshot.Players.Count} creatures={message.snapshot.Creatures.Count} allied={message.alliedCreatureCount}.");
        RefreshUiState();
    }

    internal static IReadOnlyList<(string Title, string Description)> GetDisplayedEventSpectatorOptions(EventModel eventModel)
    {
        if (eventModel.IsFinished)
        {
            return System.Array.Empty<(string Title, string Description)>();
        }

        return eventModel.CurrentOptions
            .Select(option => (
                GetSafeSpectatorRawText(option.Title, "Option"),
                GetSafeSpectatorRawText(option.Description, string.Empty)))
            .ToList();
    }

    internal static bool ShouldPublishLocalEventSpectatorState(NEventRoom room)
    {
        return ReferenceEquals(NEventRoom.Instance, room) && _runState?.CurrentRoom is EventRoom;
    }

    internal static void PublishLocalEventSpectatorState(string title, string description, IReadOnlyList<(string Title, string Description)> options)
    {
        PublishLocalSpectatorState(
            SpectatorViewKind.Event,
            title,
            description,
            options.Select(static option => option.Title).ToList(),
            options.Select(static option => option.Description).ToList());
    }

    internal static void PublishLocalTreasureSpectatorState(string title, string description, IReadOnlyList<string> options)
    {
        PublishLocalSpectatorState(SpectatorViewKind.Treasure, title, description, options, null);
    }

    private static void PublishLocalSpectatorState(SpectatorViewKind kind, string title, string description, IReadOnlyList<string> options, IReadOnlyList<string>? optionDescriptions)
    {
        if (_netService == null || Runtime.ActiveBatch == null)
        {
            return;
        }

        BranchGroupRuntime? branch = GetLocalBranch();
        if (branch == null)
        {
            return;
        }

        BranchSpectatorViewState state = branch.LatestViewState ?? new BranchSpectatorViewState();
        state.Kind = kind;
        state.Title = title;
        state.Description = description;
        state.Options = options.ToList();
        state.OptionDescriptions = optionDescriptions?.ToList() ?? new List<string>();
        state.IsInteractionBlocked = true;
        state.Revision++;
        branch.LatestViewState = state;

        ForkedRoadBranchSpectatorStateMessage message = new()
        {
            actIndex = Runtime.ActiveBatch.ActIndex,
            batchId = Runtime.ActiveBatch.BatchId,
            branchId = branch.BranchId,
            kind = kind,
            title = title,
            description = description,
            options = options.ToList(),
            optionDescriptions = state.OptionDescriptions.ToList(),
            isInteractionBlocked = true,
            revision = state.Revision
        };
        _netService.SendMessage(message);
        HandleBranchSpectatorStateMessage(message, _netService.NetId);
    }

    private static void HandleBranchSpectatorStateMessage(ForkedRoadBranchSpectatorStateMessage message, ulong senderId)
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

        if (branch.LatestViewState != null && branch.LatestViewState.Revision > message.revision)
        {
            return;
        }

        branch.LatestViewState = new BranchSpectatorViewState
        {
            Kind = message.kind,
            Title = message.title ?? string.Empty,
            Description = message.description ?? string.Empty,
            Options = message.options?.ToList() ?? new List<string>(),
            OptionDescriptions = message.optionDescriptions?.ToList() ?? new List<string>(),
            IsInteractionBlocked = message.isInteractionBlocked,
            Revision = message.revision
        };
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
            RunLocation location = new(branch.TargetCoord, batch.ActIndex);
            visitedLocations.Add(location);
            SeededSplitLocations.Add(location);
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
        EnsureCurrentRunLocationVisited();
    }

    internal static bool ShouldDisplayActionInLocalBranchUi(GameAction action)
    {
        if (!IsSplitBatchInProgress)
        {
            return HasReadyCombatActionUi(action);
        }

        HashSet<ulong> activeIds = GetActivePlayerIdsForCurrentBranch();
        if (activeIds.Count == 0)
        {
            return HasReadyCombatActionUi(action);
        }

        return activeIds.Contains(action.OwnerId) && HasReadyCombatActionUi(action);
    }

    private static bool HasReadyCombatActionUi(GameAction action)
    {
        if (action is not PlayCardAction playCardAction)
        {
            return true;
        }

        NCombatRoom? combatRoom = NCombatRoom.Instance;
        if (combatRoom?.Ui == null)
        {
            Log.Debug($"ForkedRoad suppressing play-card queue ui because combat room ui is unavailable for owner={action.OwnerId} action={action}.");
            return false;
        }

        if (LocalContext.IsMe(playCardAction.Player))
        {
            bool hasLocalHand = NPlayerHand.Instance != null;
            if (!hasLocalHand)
            {
                Log.Debug($"ForkedRoad suppressing local play-card queue ui because player hand is unavailable for owner={action.OwnerId} action={action}.");
            }

            return hasLocalHand;
        }

        bool hasRemoteIntent = combatRoom.GetCreatureNode(playCardAction.Player.Creature)?.PlayerIntentHandler != null;
        if (!hasRemoteIntent)
        {
            Log.Debug($"ForkedRoad suppressing remote play-card queue ui because creature intent ui is unavailable for owner={action.OwnerId} action={action}.");
        }

        return hasRemoteIntent;
    }

    internal static bool ShouldRelayForeignBranchAction(RunLocation location)
    {
        if (!IsSplitBatchInProgress || _netService?.Type != NetGameType.Host || _runState == null || Runtime.ActiveBatch == null)
        {
            return false;
        }

        if (!location.coord.HasValue)
        {
            return false;
        }

        if (_runState.CurrentLocation == location)
        {
            return false;
        }

        return Runtime.ActiveBatch.BranchGroups.Any(branch => branch.TargetCoord == location.coord.Value);
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

        NormalizeResolvedMapRuntimeStateIfNeeded();

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
            return IsSplitBatchInProgress;
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

            RunLocation location = new(player.SelectionCoord.Value, _runState!.CurrentActIndex);
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
        EnsureCurrentRunLocationVisited();
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

        return message.location != _runState.CurrentLocation;
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

        return location != _runState.CurrentLocation;
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
        return Runtime.RequiresAuthoritativeRoomPlans;
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

    private static void NotifyRunLocationChangedAfterDebugEntry()
    {
        if (_runState == null || RunManager.Instance == null)
        {
            return;
        }

        if (RunManagerAfterLocationChangedMethod != null)
        {
            RunManagerAfterLocationChangedMethod.Invoke(RunManager.Instance, Array.Empty<object>());
            return;
        }

        RunManager.Instance.MapSelectionSynchronizer.OnRunLocationChanged(_runState.CurrentLocation);
        RunManager.Instance.RunLocationTargetedBuffer.OnRunLocationChanged(_runState.CurrentLocation);
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
        NotifyRunLocationChangedAfterDebugEntry();
        NormalizeRunLocationBuffer(_runState.CurrentLocation);
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
        if (ShouldIgnorePlayerEliminatedMessage(message))
        {
            return;
        }

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

    private static bool ShouldIgnorePlayerEliminatedMessage(ForkedRoadPlayerEliminatedMessage message)
    {
        if (message.batchId <= 0)
        {
            return Runtime.ActiveBatch == null;
        }

        if (Runtime.ActiveBatch == null || Runtime.ActiveBatch.BatchId != message.batchId)
        {
            return true;
        }

        if (message.branchId.HasValue)
        {
            BranchGroupRuntime? branch = Runtime.ActiveBatch.FindBranch(message.branchId.Value);
            return branch == null || !branch.PlayerIds.Contains(message.playerId);
        }

        return Runtime.ActiveBatch.FindBranchForPlayer(message.playerId) == null;
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
            HashSet<ulong> fallbackIds = GetCurrentBranchPlayerIds(includeEliminated: true);
            return fallbackIds.Count == 0
                ? new BranchPlayerCollection(Array.Empty<Player>())
                : new BranchPlayerCollection(_runState.Players.Where(player => fallbackIds.Contains(player.NetId)));
        }

        return new BranchPlayerCollection(_runState.Players.Where(player => activeIds.Contains(player.NetId)));
    }

    private static void NormalizeResolvedMapRuntimeStateIfNeeded()
    {
        if (_runState == null || Runtime.ActiveBatch != null || _runState.CurrentRoom is not MapRoom)
        {
            return;
        }

        foreach (PlayerBranchRuntime player in Runtime.Players.Values)
        {
            player.IsEliminated = false;
            player.CurrentBranchId = null;
            player.SpectatingBranchId = null;
            player.MapVoteDestinationCoord = null;
            if (player.Phase is RouteSplitPlayerPhase.FinishedWaiting or RouteSplitPlayerPhase.SpectatingOtherBranch)
            {
                player.Phase = RouteSplitPlayerPhase.ChoosingRoute;
            }
        }
    }

    internal static bool ShouldScopeCombatParticipants(IRunState runState)
    {
        return IsSplitBatchInProgress || HasEliminatedPlayers() || runState.Players.Any(static player => player.Creature.IsDead);
    }

    internal static bool TryHandleSpectatorCombatRoomNavigation(NCombatRoom room)
    {
        if (ReferenceEquals(room, NCombatRoom.Instance) || room.Ui == null)
        {
            return false;
        }

        List<NCreature> interactable = room.CreatureNodes
            .Where(node => node.IsInteractable)
            .OrderBy(node => node.GlobalPosition.X)
            .ToList();

        for (int index = 0; index < interactable.Count; index++)
        {
            Control hitbox = interactable[index].Hitbox;
            hitbox.FocusNeighborLeft = (index > 0 ? interactable[index - 1].Hitbox.GetPath() : interactable[^1].Hitbox.GetPath());
            hitbox.FocusNeighborRight = (index < interactable.Count - 1 ? interactable[index + 1].Hitbox.GetPath() : interactable[0].Hitbox.GetPath());
            hitbox.FocusNeighborBottom = room.Ui.Hand.CardHolderContainer.GetPath();
            hitbox.FocusNeighborTop = hitbox.GetPath();
            interactable[index].UpdateNavigation();
        }

        room.Ui.Hand.CardHolderContainer.FocusNeighborTop = interactable.FirstOrDefault()?.Hitbox.GetPath() ?? room.Ui.Hand.CardHolderContainer.GetPath();
        return true;
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
        return branch != null ? branch.DeathClearTriggered : _runState?.CurrentLocation == _deathClearedCombatLocation;
    }

    private static void MarkDeathClearTriggered(BranchGroupRuntime? branch)
    {
        if (_runState == null)
        {
            return;
        }

        _deathClearedCombatLocation = _runState.CurrentLocation;
        _suppressedCombatRewardLocation = _runState.CurrentLocation;
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
            _runState.CurrentLocation == _suppressedCombatRewardLocation.Value;
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

