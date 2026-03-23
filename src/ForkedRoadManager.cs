using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Godot;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.Multiplayer;
using MegaCrit.Sts2.Core.Nodes;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Runs.History;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.TestSupport;

namespace ForkedRoad;

internal static class ForkedRoadManager
{
    private sealed class BranchGroup
    {
        public required MapCoord Coord { get; init; }

        public required ulong[] PlayerIds { get; init; }
    }

    private static readonly Dictionary<ulong, MapCoord?> PlayerCoords = new();

    private static readonly Dictionary<ulong, List<MapCoord>> PlayerVisitedCoords = new();

    private static readonly Queue<BranchGroup> PendingGroups = new();

    private static RunState? _runState;

    private static INetGameService? _netService;

    private static bool _branchStartHandlerRegistered;

    private static bool _branchContinueHandlerRegistered;

    private static bool _branchCompleteHandlerRegistered;

    private static bool _merchantSceneReadyHandlerRegistered;

    private static bool _branchMergeHandlerRegistered;

    private static bool _branchMergeResolvedHandlerRegistered;

    private static BranchGroup? _activeGroup;

    private static bool _splitBatchInProgress;

    private static int _activeBranchSequence;

    private static int _remainingBranchesAfterCurrent;

    private static int _trackedActIndex = -1;

    private static readonly HashSet<ulong> ReadyPlayers = new();

    private static readonly Dictionary<ulong, MapCoord> PlayerMergeTargets = new();

    private static readonly HashSet<ulong> PlayersSkippingCurrentRoomRewards = new();

    private static readonly HashSet<ulong> PlayersFollowingMergedBranch = new();

    private static readonly List<MapCoord> CompletedBranchCoords = new();

    private static readonly Dictionary<MapCoord, int> BranchPlayerCounts = new();

    private static bool _branchEndedByMerge;

    private static int _mergeCleanupBranchSequence = -1;

    private static int _pendingReadyBranchSequence = -1;

    public static bool IsSplitBatchInProgress => _splitBatchInProgress;

    public static int ActiveBranchSequence => _activeBranchSequence;

    public static bool IsLocalPlayerActiveInCurrentBranch
    {
        get
        {
            if (!_splitBatchInProgress || _activeGroup == null || !LocalContext.NetId.HasValue)
            {
                return true;
            }
            return _activeGroup.PlayerIds.Contains(LocalContext.NetId.Value);
        }
    }

    public static int ActivePlayerCount
    {
        get
        {
            if (_activeGroup != null)
            {
                return _activeGroup.PlayerIds.Length;
            }
            return _runState?.Players.Count ?? 1;
        }
    }

    public static bool IsMainBranchActive => false;

    public static bool IsSupportBranchActive => _splitBatchInProgress && _activeGroup != null && TryGetMergeTargetCoord(_activeGroup.Coord, out _);

    public static bool IsCurrentBranchEndingByMerge => _branchEndedByMerge;

    private static bool RequiresAllPlayersReadyForCurrentBranch()
    {
        if (!_splitBatchInProgress || _activeGroup == null || _runState == null)
        {
            return false;
        }

        MapPoint? point = _runState.Map.GetPoint(_activeGroup.Coord);
        return point?.PointType is MapPointType.Monster or MapPointType.Elite or MapPointType.Boss or MapPointType.Treasure;
    }

    private static IReadOnlyCollection<ulong> GetPlayersRequiredToAdvanceCurrentBranch()
    {
        if (_runState == null)
        {
            return Array.Empty<ulong>();
        }

        if (_branchEndedByMerge)
        {
            // ForkedRoad: a branch-ending merge must wait for every client to
            // finish unwinding the old room stack before the next branch starts.
            return _runState.Players.Select(static player => player.NetId).ToArray();
        }

        if (RequiresAllPlayersReadyForCurrentBranch())
        {
            return _runState.Players.Select(static player => player.NetId).ToArray();
        }

        return _activeGroup?.PlayerIds ?? Array.Empty<ulong>();
    }

    public static void InitializeForRun(RunState? runState, INetGameService? netService)
    {
        if (runState == null || netService == null)
        {
            return;
        }

        if (!ReferenceEquals(_runState, runState))
        {
            ClearRuntimeState();
            _runState = runState;
            _trackedActIndex = runState.CurrentActIndex;
        }

        if (_trackedActIndex != runState.CurrentActIndex && runState.CurrentMapCoord == null)
        {
            _trackedActIndex = runState.CurrentActIndex;
            PendingGroups.Clear();
            _activeGroup = null;
            _splitBatchInProgress = false;
            _activeBranchSequence = 0;
            _remainingBranchesAfterCurrent = 0;
            foreach (Player player in runState.Players)
            {
                PlayerCoords[player.NetId] = null;
                PlayerVisitedCoords[player.NetId] = new List<MapCoord>();
            }
        }

        _runState = runState;
        foreach (Player player2 in runState.Players)
        {
            if (!PlayerCoords.ContainsKey(player2.NetId))
            {
                PlayerCoords[player2.NetId] = runState.CurrentMapCoord;
            }
            if (!PlayerVisitedCoords.ContainsKey(player2.NetId))
            {
                PlayerVisitedCoords[player2.NetId] = runState.CurrentMapCoord.HasValue
                    ? new List<MapCoord> { runState.CurrentMapCoord.Value }
                    : new List<MapCoord>();
            }
        }

        if (_netService != null && !ReferenceEquals(_netService, netService) && _branchStartHandlerRegistered)
        {
            _netService.UnregisterMessageHandler<ForkedRoadBranchStartMessage>(HandleBranchStartMessage);
            _branchStartHandlerRegistered = false;
        }

        _netService = netService;
        if (!_branchStartHandlerRegistered)
        {
            _netService.RegisterMessageHandler<ForkedRoadBranchStartMessage>(HandleBranchStartMessage);
            _branchStartHandlerRegistered = true;
        }
        if (!_branchContinueHandlerRegistered)
        {
            _netService.RegisterMessageHandler<ForkedRoadBranchContinueMessage>(HandleBranchContinueMessage);
            _branchContinueHandlerRegistered = true;
        }
        if (!_branchCompleteHandlerRegistered)
        {
            _netService.RegisterMessageHandler<ForkedRoadBranchCompleteMessage>(HandleBranchCompleteMessage);
            _branchCompleteHandlerRegistered = true;
        }
        if (!_merchantSceneReadyHandlerRegistered)
        {
            _netService.RegisterMessageHandler<ForkedRoadMerchantSceneReadyMessage>(HandleMerchantSceneReadyMessage);
            _merchantSceneReadyHandlerRegistered = true;
        }
        if (!_branchMergeHandlerRegistered)
        {
            _netService.RegisterMessageHandler<ForkedRoadBranchMergeMessage>(HandleBranchMergeMessage);
            _branchMergeHandlerRegistered = true;
        }
        if (!_branchMergeResolvedHandlerRegistered)
        {
            _netService.RegisterMessageHandler<ForkedRoadBranchMergeResolvedMessage>(HandleBranchMergeResolvedMessage);
            _branchMergeResolvedHandlerRegistered = true;
        }
    }

    public static void Reset()
    {
        if (_netService != null && _branchStartHandlerRegistered)
        {
            _netService.UnregisterMessageHandler<ForkedRoadBranchStartMessage>(HandleBranchStartMessage);
            _branchStartHandlerRegistered = false;
        }
        if (_netService != null && _branchContinueHandlerRegistered)
        {
            _netService.UnregisterMessageHandler<ForkedRoadBranchContinueMessage>(HandleBranchContinueMessage);
            _branchContinueHandlerRegistered = false;
        }
        if (_netService != null && _branchCompleteHandlerRegistered)
        {
            _netService.UnregisterMessageHandler<ForkedRoadBranchCompleteMessage>(HandleBranchCompleteMessage);
            _branchCompleteHandlerRegistered = false;
        }
        if (_netService != null && _merchantSceneReadyHandlerRegistered)
        {
            _netService.UnregisterMessageHandler<ForkedRoadMerchantSceneReadyMessage>(HandleMerchantSceneReadyMessage);
            _merchantSceneReadyHandlerRegistered = false;
        }
        if (_netService != null && _branchMergeHandlerRegistered)
        {
            _netService.UnregisterMessageHandler<ForkedRoadBranchMergeMessage>(HandleBranchMergeMessage);
            _branchMergeHandlerRegistered = false;
        }
        if (_netService != null && _branchMergeResolvedHandlerRegistered)
        {
            _netService.UnregisterMessageHandler<ForkedRoadBranchMergeResolvedMessage>(HandleBranchMergeResolvedMessage);
            _branchMergeResolvedHandlerRegistered = false;
        }

        _netService = null;
        _runState = null;
        _trackedActIndex = -1;
        ClearRuntimeState();
    }

    public static MapCoord? GetPlayerCoord(Player player)
    {
        return GetPlayerCoord(player.NetId, player.RunState);
    }

    public static MapCoord? GetPlayerCoord(ulong playerId, IRunState? fallbackState = null)
    {
        if (PlayerCoords.TryGetValue(playerId, out MapCoord? coord))
        {
            return coord;
        }
        return fallbackState?.CurrentMapCoord;
    }

    public static MapCoord? GetLocalPlayerCoord(IRunState runState)
    {
        if (!LocalContext.NetId.HasValue)
        {
            return runState.CurrentMapCoord;
        }
        return GetPlayerCoord(LocalContext.NetId.Value, runState);
    }

    public static IReadOnlyList<MapCoord> GetPlayerVisitedCoords(ulong playerId, IRunState? fallbackState = null)
    {
        if (PlayerVisitedCoords.TryGetValue(playerId, out List<MapCoord>? coords))
        {
            return coords;
        }

        if (fallbackState?.CurrentMapCoord is MapCoord coord)
        {
            return new[] { coord };
        }

        return Array.Empty<MapCoord>();
    }

    public static IReadOnlyCollection<MapCoord> GetKnownBranchCoords(IRunState? fallbackState = null)
    {
        HashSet<MapCoord> coords = new();
        foreach (Player player in fallbackState?.Players ?? _runState?.Players ?? Array.Empty<Player>())
        {
            MapCoord? coord = GetPlayerCoord(player.NetId, fallbackState);
            if (coord.HasValue)
            {
                coords.Add(coord.Value);
            }
        }

        return coords;
    }

    private static void RecordVisitedCoord(ulong playerId, MapCoord coord)
    {
        if (!PlayerVisitedCoords.TryGetValue(playerId, out List<MapCoord>? history))
        {
            history = new List<MapCoord>();
            PlayerVisitedCoords[playerId] = history;
        }

        if (history.Count == 0 || history[^1] != coord)
        {
            history.Add(coord);
        }
    }

    private static void ReplaceCurrentVisitedCoord(ulong playerId, MapCoord coord)
    {
        if (!PlayerVisitedCoords.TryGetValue(playerId, out List<MapCoord>? history))
        {
            history = new List<MapCoord>();
            PlayerVisitedCoords[playerId] = history;
        }

        if (history.Count == 0)
        {
            history.Add(coord);
        }
        else
        {
            history[^1] = coord;
        }
    }

    public static bool TryGetMergeTargetCoord(MapCoord currentCoord, out MapCoord targetCoord)
    {
        // ForkedRoad: a dead branch merges into the most-populated other branch first.
        List<MapCoord> candidateCoords = new();
        candidateCoords.AddRange(PendingGroups
            .Select(static group => group.Coord)
            .Where(coord => coord != currentCoord));
        candidateCoords.AddRange(CompletedBranchCoords.Where(coord => coord != currentCoord));

        MapCoord? selected = candidateCoords
            .Distinct()
            .OrderByDescending(coord => _runState?.Players.Count(player =>
                GetPlayerCoord(player.NetId, _runState) == coord &&
                !player.Creature.IsDead) ?? 0)
            .ThenByDescending(coord => BranchPlayerCounts.TryGetValue(coord, out int count) ? count : 0)
            .ThenBy(coord => coord.col)
            .ThenBy(coord => coord.row)
            .Cast<MapCoord?>()
            .FirstOrDefault();

        if (selected.HasValue)
        {
            targetCoord = selected.Value;
            return true;
        }

        targetCoord = default;
        return false;
    }

    public static IEnumerable<Player> GetActivePlayers(IRunState runState)
    {
        if (!_splitBatchInProgress || _activeGroup == null)
        {
            return runState.Players;
        }
        return runState.Players.Where((Player player) => _activeGroup.PlayerIds.Contains(player.NetId));
    }

    public static IReadOnlyCollection<ulong> GetActivePlayerIds()
    {
        return _activeGroup?.PlayerIds ?? Array.Empty<ulong>();
    }

    public static bool IsPlayerActive(Player player)
    {
        if (!_splitBatchInProgress || _activeGroup == null)
        {
            return true;
        }
        return _activeGroup.PlayerIds.Contains(player.NetId);
    }

    public static IEnumerable<string> GetActivePlayerNames(IRunState runState)
    {
        return GetActivePlayers(runState).Select(static player => player.Character.Id.Entry);
    }

    public static bool ShouldSkipRoomEndRewards(Player player)
    {
        return PlayersSkippingCurrentRoomRewards.Remove(player.NetId);
    }

    private static bool IsMergedFollower(ulong playerId)
    {
        return PlayersFollowingMergedBranch.Contains(playerId);
    }

    private static void ApplyVote(MapSelectionSynchronizer synchronizer, RunState runState, List<MapVote?> votes, Player player, MapVote? destination)
    {
        int playerSlotIndex = runState.GetPlayerSlotIndex(player);
        MapVote? oldVote = votes[playerSlotIndex];
        votes[playerSlotIndex] = destination;
        ForkedRoadPatches.InvokePlayerVoteChanged(synchronizer, player, oldVote, destination);
    }

    private static void MirrorVoteToMergedFollowers(MapSelectionSynchronizer synchronizer, RunState runState, List<MapVote?> votes, Player leader, MapVote? destination)
    {
        MapCoord? leaderCoord = GetPlayerCoord(leader.NetId, runState);
        if (!leaderCoord.HasValue)
        {
            return;
        }

        foreach (Player follower in runState.Players)
        {
            if (!IsMergedFollower(follower.NetId) || follower.NetId == leader.NetId)
            {
                continue;
            }
            if (GetPlayerCoord(follower.NetId, runState) != leaderCoord)
            {
                continue;
            }

            ApplyVote(synchronizer, runState, votes, follower, destination);
            Log.Info($"ForkedRoad mirrored map vote from {leader.NetId} to merged follower {follower.NetId}: {leaderCoord.Value}->{destination?.coord}");
        }
    }

    public static void MarkPlayerForBranchMerge(Player player)
    {
        if (!_splitBatchInProgress || _activeGroup == null || !TryGetMergeTargetCoord(_activeGroup.Coord, out MapCoord targetCoord))
        {
            return;
        }
        if (PlayerMergeTargets.TryGetValue(player.NetId, out MapCoord existingTarget) && existingTarget == targetCoord)
        {
            return;
        }

        PlayerMergeTargets[player.NetId] = targetCoord;
        // ForkedRoad: always send the merge target through the net service.
        // On host this broadcasts to peers; on client this reports the merge to host,
        // which then re-broadcasts because the message has ShouldBroadcast=true.
        if (_netService != null && _runState != null)
        {
        _netService.SendMessage(new ForkedRoadBranchMergeMessage
        {
            actIndex = _runState.CurrentActIndex,
            playerId = player.NetId,
            targetCoord = targetCoord
        });
        }
        PlayersSkippingCurrentRoomRewards.Add(player.NetId);
        Log.Info($"ForkedRoad player {player.NetId} marked to merge into branch {targetCoord} after death.");
    }

    public static void BroadcastCurrentBranchMergeResolved()
    {
        if (!_splitBatchInProgress || _activeGroup == null || _runState == null || _netService == null)
        {
            return;
        }

        _branchEndedByMerge = true;
        _netService.SendMessage(new ForkedRoadBranchMergeResolvedMessage
        {
            actIndex = _runState.CurrentActIndex,
            branchSequence = _activeBranchSequence,
            coord = _activeGroup.Coord
        });
        Log.Info($"ForkedRoad broadcast branch merge resolution for branch {_activeBranchSequence} at {_activeGroup.Coord}.");
    }

    public static bool TryBeginCurrentBranchMergeCleanup()
    {
        if (!_splitBatchInProgress || _activeGroup == null)
        {
            return false;
        }

        _branchEndedByMerge = true;
        if (_mergeCleanupBranchSequence == _activeBranchSequence)
        {
            return false;
        }

        _mergeCleanupBranchSequence = _activeBranchSequence;
        return true;
    }

    public static void CompleteBranchMergeCleanup(int branchSequence)
    {
        if (_mergeCleanupBranchSequence == branchSequence)
        {
            _mergeCleanupBranchSequence = -1;
        }
    }

    public static Player? GetPerspectivePlayer(IEnumerable<Player> players)
    {
        List<Player> playerList = players.ToList();
        if (playerList.Count == 0)
        {
            return null;
        }

        Player? localPlayer = null;
        if (LocalContext.NetId.HasValue)
        {
            localPlayer = playerList.FirstOrDefault((Player p) => p.NetId == LocalContext.NetId.Value);
        }

        if (!_splitBatchInProgress || _activeGroup == null)
        {
            return localPlayer ?? playerList.First();
        }

        if (localPlayer != null && _activeGroup.PlayerIds.Contains(localPlayer.NetId))
        {
            return localPlayer;
        }

        return playerList.FirstOrDefault((Player p) => _activeGroup.PlayerIds.Contains(p.NetId)) ?? localPlayer ?? playerList.First();
    }

    public static bool IsSpectatingBranch()
    {
        if (!_splitBatchInProgress || _activeGroup == null || !LocalContext.NetId.HasValue)
        {
            return false;
        }

        return !_activeGroup.PlayerIds.Contains(LocalContext.NetId.Value);
    }

    public static void RefreshDisplayedPlayers()
    {
        NRun? run = NRun.Instance;
        if (run?.GlobalUi?.MultiplayerPlayerContainer == null)
        {
            return;
        }

        foreach (NMultiplayerPlayerState node in run.GlobalUi.MultiplayerPlayerContainer.GetChildren().OfType<NMultiplayerPlayerState>())
        {
            node.Visible = !_splitBatchInProgress || _activeGroup == null || _activeGroup.PlayerIds.Contains(node.Player.NetId);
        }
    }

    public static bool ShouldSuppressManualMapSelection(IRunState runState)
    {
        if (_splitBatchInProgress)
        {
            return true;
        }

        if (!LocalContext.NetId.HasValue)
        {
            return false;
        }

        return PlayersFollowingMergedBranch.Contains(LocalContext.NetId.Value);
    }

    public static bool ShouldReuseExistingFloorHistory(RunState runState)
    {
        return _splitBatchInProgress && _activeGroup != null && ReferenceEquals(_runState, runState) && _activeBranchSequence > 1;
    }

    public static void AppendRoomToExistingFloorHistory(RunState runState, RoomType roomType, ModelId? roomModelId)
    {
        MapPointHistoryEntry? entry = runState.CurrentMapPointHistoryEntry;
        if (entry == null)
        {
            return;
        }
        entry.Rooms.Add(new MapPointRoomHistoryEntry
        {
            RoomType = roomType,
            ModelId = roomModelId
        });
    }

    public static void BeforeEnterMapCoord(RunManager runManager, MapCoord coord)
    {
        InitializeForRun(ForkedRoadPatches.GetRunManagerState(runManager), runManager.NetService);
        if (_runState == null)
        {
            return;
        }

        if (_splitBatchInProgress && _activeGroup != null)
        {
            return;
        }

        foreach (Player player in _runState.Players)
        {
            PlayerCoords[player.NetId] = coord;
            RecordVisitedCoord(player.NetId, coord);
            PlayersFollowingMergedBranch.Remove(player.NetId);
        }
    }

    public static async Task HandleMapRoomEnteredAsync(RunState? runState)
    {
        InitializeForRun(runState, RunManager.Instance.NetService);
        await Task.CompletedTask;
    }

    public static void NotifyLocalTerminalProceed()
    {
        if (!_splitBatchInProgress || _activeGroup == null || _netService == null || _runState == null || !LocalContext.NetId.HasValue)
        {
            return;
        }

        if (_pendingReadyBranchSequence == _activeBranchSequence)
        {
            return;
        }

        ulong localPlayerId = LocalContext.NetId.Value;
        if (!_activeGroup.PlayerIds.Contains(localPlayerId) && !RequiresAllPlayersReadyForCurrentBranch())
        {
            return;
        }

        _pendingReadyBranchSequence = _activeBranchSequence;
        TaskHelper.RunSafely(NotifyLocalTerminalProceedAsync(localPlayerId, _activeBranchSequence));
    }

    private static async Task NotifyLocalTerminalProceedAsync(ulong localPlayerId, int branchSequence)
    {
        try
        {
            Task? currentSaveTask = SaveManager.Instance.CurrentRunSaveTask;
            if (currentSaveTask != null)
            {
                await currentSaveTask;
            }

            await Task.Delay(50);

            if (!_splitBatchInProgress || _activeGroup == null || _netService == null || _runState == null || _activeBranchSequence != branchSequence)
            {
                return;
            }
            if (!_activeGroup.PlayerIds.Contains(localPlayerId) && !RequiresAllPlayersReadyForCurrentBranch())
            {
                return;
            }

            MarkPlayerReady(localPlayerId, shouldSendToHost: _netService.Type == NetGameType.Client);
        }
        finally
        {
            if (_pendingReadyBranchSequence == branchSequence)
            {
                _pendingReadyBranchSequence = -1;
            }
        }
    }

    public static void NotifyMerchantSceneReady()
    {
        if (!_splitBatchInProgress || _activeGroup == null || _netService == null || _runState == null || !LocalContext.NetId.HasValue)
        {
            return;
        }

        ulong localPlayerId = LocalContext.NetId.Value;
        if (!_activeGroup.PlayerIds.Contains(localPlayerId))
        {
            return;
        }

        _netService.SendMessage(new ForkedRoadMerchantSceneReadyMessage
        {
            actIndex = _runState.CurrentActIndex,
            branchSequence = _activeBranchSequence
        });
    }

    public static bool TryHandleVote(MapSelectionSynchronizer synchronizer, Player player, RunLocation source, MapVote? destination)
    {
        RunState runState = ForkedRoadPatches.MapSelectionRunStateRef(synchronizer);
        INetGameService netService = ForkedRoadPatches.MapSelectionNetServiceRef(synchronizer);
        InitializeForRun(runState, netService);
        if (runState.Players.Count <= 1)
        {
            return false;
        }

        if (_splitBatchInProgress)
        {
            Log.Info($"ForkedRoad ignored map vote during active branch batch from {player.NetId}: {source}->{destination}");
            return true;
        }

        RunLocation expectedSource = new RunLocation(GetPlayerCoord(player), runState.CurrentActIndex);
        if (source != expectedSource)
        {
            Log.Warn($"ForkedRoad rejected vote from {player.NetId}: expected {expectedSource}, got {source}");
            return true;
        }

        if (destination?.mapGenerationCount < synchronizer.MapGenerationCount)
        {
            Log.Warn($"ForkedRoad received stale vote from {player.NetId} for map generation {destination?.mapGenerationCount}");
        }

        List<MapVote?> votes = ForkedRoadPatches.MapSelectionVotesRef(synchronizer);
        if (IsMergedFollower(player.NetId))
        {
            Log.Info($"ForkedRoad ignored independent map vote from merged follower {player.NetId}: {source}->{destination}");
            return true;
        }

        ApplyVote(synchronizer, runState, votes, player, destination);
        MirrorVoteToMergedFollowers(synchronizer, runState, votes, player, destination);

        if (votes.All((MapVote? vote) => vote.HasValue && vote.Value.mapGenerationCount == synchronizer.MapGenerationCount) && netService.Type != NetGameType.Client)
        {
            if (ShouldSplitVotes(runState, votes))
            {
                BeginSplitBatch(runState, votes);
            }
            else if (ShouldUseMergeBatch(runState, votes))
            {
                MapCoord destinationCoord = votes.First(static vote => vote.HasValue)!.Value.coord;
                Log.Info($"ForkedRoad starting convergence batch for shared destination {destinationCoord}.");
                BeginSplitBatch(runState, votes, forceSingleGroupBatch: true);
            }
            else
            {
                _splitBatchInProgress = false;
                PendingGroups.Clear();
                _activeGroup = null;
                _activeBranchSequence = 0;
                _remainingBranchesAfterCurrent = 0;
                ForkedRoadPatches.InvokeSharedMoveToMapCoord(synchronizer);
            }
        }

        return true;
    }

    private static bool ShouldSplitVotes(RunState runState, IReadOnlyList<MapVote?> votes)
    {
        List<MapCoord> uniqueCoords = votes.Where(static vote => vote.HasValue)
            .Select(static vote => vote!.Value.coord)
            .Distinct()
            .ToList();
        if (uniqueCoords.Count <= 1)
        {
            return false;
        }

        foreach (MapCoord coord in uniqueCoords)
        {
            MapPoint? point = runState.Map.GetPoint(coord);
            if (point == null)
            {
                return false;
            }
        }

        return true;
    }

    private static bool ShouldUseMergeBatch(RunState runState, IReadOnlyList<MapVote?> votes)
    {
        List<MapCoord> uniqueDestinationCoords = votes.Where(static vote => vote.HasValue)
            .Select(static vote => vote!.Value.coord)
            .Distinct()
            .ToList();
        if (uniqueDestinationCoords.Count != 1)
        {
            return false;
        }

        List<MapCoord> uniqueSourceCoords = runState.Players
            .Select(player => GetPlayerCoord(player.NetId, runState))
            .Where(static coord => coord.HasValue)
            .Select(static coord => coord!.Value)
            .Distinct()
            .ToList();

        return uniqueSourceCoords.Count > 1;
    }

    private static void BeginSplitBatch(RunState runState, IReadOnlyList<MapVote?> votes, bool forceSingleGroupBatch = false)
    {
        PendingGroups.Clear();
        PlayerMergeTargets.Clear();
        PlayersSkippingCurrentRoomRewards.Clear();
        PlayersFollowingMergedBranch.Clear();
        CompletedBranchCoords.Clear();
        BranchPlayerCounts.Clear();
        _branchEndedByMerge = false;

        List<BranchGroup> groups = new();
        foreach (IGrouping<MapCoord, Player> group in votes.Select((MapVote? vote, int index) => new
        {
            Vote = vote,
            Player = runState.Players[index]
        }).Where(static item => item.Vote.HasValue)
            .GroupBy(static item => item.Vote!.Value.coord, static item => item.Player)
            .OrderBy(static group => group.Key.row)
            .ThenBy(static group => group.Key.col))
        {
            groups.Add(new BranchGroup
            {
                Coord = group.Key,
                PlayerIds = group.Select(static currentPlayer => currentPlayer.NetId).ToArray()
            });
            BranchPlayerCounts[group.Key] = group.Count();
        }

        foreach (BranchGroup group in groups)
        {
            PendingGroups.Enqueue(group);
        }

        _splitBatchInProgress = PendingGroups.Count > 1 || forceSingleGroupBatch;
        _activeGroup = null;
        _activeBranchSequence = 0;
        _remainingBranchesAfterCurrent = 0;

        if (_splitBatchInProgress)
        {
            SendNextBranchStartMessage();
        }
    }

    private static void SendNextBranchStartMessage()
    {
        if (_runState == null || _netService == null || _netService.Type == NetGameType.Client || PendingGroups.Count == 0)
        {
            return;
        }

        BranchGroup next = PendingGroups.Dequeue();
        List<ulong> playerIds = next.PlayerIds.ToList();
        foreach ((ulong playerId, MapCoord targetCoord) in PlayerMergeTargets.ToArray())
        {
            if (targetCoord != next.Coord)
            {
                continue;
            }

            if (!playerIds.Contains(playerId))
            {
                playerIds.Add(playerId);
            }

            PlayerMergeTargets.Remove(playerId);
            PlayersSkippingCurrentRoomRewards.Remove(playerId);
            PlayerCoords[playerId] = next.Coord;
            ReplaceCurrentVisitedCoord(playerId, next.Coord);
        }

        int branchSequence = _activeBranchSequence + 1;
        ForkedRoadBranchStartMessage message = new ForkedRoadBranchStartMessage
        {
            actIndex = _runState.CurrentActIndex,
            coord = next.Coord,
            branchSequence = branchSequence,
            remainingBranches = PendingGroups.Count,
            playerIds = playerIds
        };
        _netService.SendMessage(message);
        HandleBranchStartMessage(message, _netService.NetId);
    }

    private static void HandleBranchStartMessage(ForkedRoadBranchStartMessage message, ulong _senderId)
    {
        InitializeForRun(ForkedRoadPatches.GetRunManagerState(RunManager.Instance), RunManager.Instance.NetService);
        if (_runState == null || message.actIndex != _runState.CurrentActIndex)
        {
            return;
        }

        Dictionary<ulong, MapCoord?> previousCoords = message.playerIds.ToDictionary(static playerId => playerId, playerId => GetPlayerCoord(playerId, _runState));
        _splitBatchInProgress = true;
        // ForkedRoad: a new branch start means any prior merge-ending cleanup is complete.
        // If we keep this flag set, the new combat room suppresses StartTurn and the merged
        // player enters revived but unable to draw/play/end turn.
        _branchEndedByMerge = false;
        _mergeCleanupBranchSequence = -1;
        _pendingReadyBranchSequence = -1;
        _activeBranchSequence = message.branchSequence;
        _remainingBranchesAfterCurrent = message.remainingBranches;
        ReadyPlayers.Clear();
        _activeGroup = new BranchGroup
        {
            Coord = message.coord,
            PlayerIds = message.playerIds.ToArray()
        };
        Log.Info($"ForkedRoad starting branch {message.branchSequence} at {message.coord} with players [{string.Join(",", message.playerIds)}].");
        foreach (ulong playerId in _activeGroup.PlayerIds)
        {
            if (PlayerMergeTargets.Remove(playerId))
            {
                PlayersSkippingCurrentRoomRewards.Remove(playerId);
                Log.Info($"ForkedRoad consumed pending merge target for player {playerId} on entering branch {message.coord}.");
            }
            if (PlayersFollowingMergedBranch.Remove(playerId))
            {
                Log.Info($"ForkedRoad cleared merged-follower lock for player {playerId} on entering branch {message.coord}.");
            }
            PlayerCoords[playerId] = message.coord;
            RecordVisitedCoord(playerId, message.coord);
        }

        RefreshDisplayedPlayers();

        TaskHelper.RunSafely(PrepareAndBeginBranchAsync(message.coord, previousCoords));
    }

    private static async Task PrepareAndBeginBranchAsync(MapCoord coord, IReadOnlyDictionary<ulong, MapCoord?> previousCoords)
    {
        bool localWasDeadBeforeMerge = false;
        if (_runState != null)
        {
            foreach ((ulong playerId, MapCoord? previousCoord) in previousCoords)
            {
                if (previousCoord == coord)
                {
                    continue;
                }

                Player? player = _runState.Players.FirstOrDefault(player => player.NetId == playerId);
                if (player?.Creature.IsDead == true)
                {
                    if (LocalContext.NetId.HasValue && LocalContext.NetId.Value == playerId)
                    {
                        localWasDeadBeforeMerge = true;
                    }
                    player.ActivateHooks();
                    await player.ReviveBeforeCombatEnd();
                }
            }
        }

        if (localWasDeadBeforeMerge)
        {
            Log.Info($"ForkedRoad directly entering merged branch room at {coord} for revived player.");
            NMapScreen.Instance?.Close(animateOut: false);
            await RunManager.Instance.EnterMapCoord(coord);
            return;
        }

        await BeginBranchWhenReadyAsync(coord);
    }

    private static async Task BeginBranchWhenReadyAsync(MapCoord coord)
    {
        if (TestMode.IsOn)
        {
            await RunManager.Instance.EnterMapCoord(coord);
            return;
        }

        MapPoint? point = _runState?.Map.GetPoint(coord);
        if (point?.PointType == MapPointType.Shop)
        {
            Log.Info($"ForkedRoad merchant branch entering shared shop scene: branch={_activeBranchSequence} coord={coord} localActive={IsLocalPlayerActiveInCurrentBranch} spectating={IsSpectatingBranch()}");
            NMapScreen.Instance?.Close(animateOut: false);
            await RunManager.Instance.EnterMapCoord(coord);
            return;
        }

        if (point?.PointType == MapPointType.Treasure)
        {
            Log.Info($"ForkedRoad treasure branch entering shared treasure scene: branch={_activeBranchSequence} coord={coord} localActive={IsLocalPlayerActiveInCurrentBranch} spectating={IsSpectatingBranch()}");
            NMapScreen.Instance?.Close(animateOut: false);
            await RunManager.Instance.EnterMapCoord(coord);
            return;
        }

        if (NMapScreen.Instance != null && !NMapScreen.Instance.IsOpen)
        {
            NMapScreen.Instance.Open();
        }

        for (int i = 0; i < 120; i++)
        {
            if (NMapScreen.Instance != null && NMapScreen.Instance.IsOpen)
            {
                break;
            }
            await Task.Delay(25);
        }

        if (NMapScreen.Instance != null)
        {
            await NMapScreen.Instance.TravelToMapCoord(coord);
        }
        else
        {
            await RunManager.Instance.EnterMapCoord(coord);
        }
    }

    private static void HandleBranchContinueMessage(ForkedRoadBranchContinueMessage message, ulong senderId)
    {
        if (_runState == null || _netService == null || _netService.Type == NetGameType.Client)
        {
            return;
        }
        if (!_splitBatchInProgress || _activeGroup == null)
        {
            return;
        }
        if (message.actIndex != _runState.CurrentActIndex || message.branchSequence != _activeBranchSequence)
        {
            return;
        }

        MarkPlayerReady(senderId, shouldSendToHost: false);
    }

    private static void HandleBranchCompleteMessage(ForkedRoadBranchCompleteMessage message, ulong _senderId)
    {
        if (_runState == null || message.actIndex != _runState.CurrentActIndex)
        {
            return;
        }

        CompleteBatchLocal();
    }

    private static void HandleMerchantSceneReadyMessage(ForkedRoadMerchantSceneReadyMessage message, ulong _senderId)
    {
        if (_runState == null || message.actIndex != _runState.CurrentActIndex || !_splitBatchInProgress || _activeGroup == null)
        {
            return;
        }
        if (message.branchSequence != _activeBranchSequence || !IsSpectatingBranch())
        {
            return;
        }

        MapPoint? point = _runState.Map.GetPoint(_activeGroup.Coord);
        if (point?.PointType != MapPointType.Shop)
        {
            return;
        }
        if (NRun.Instance?.MerchantRoom != null)
        {
            Log.Info($"ForkedRoad merchant spectator scene-ready ignored because merchant room already exists: branch={message.branchSequence}");
            return;
        }

        Log.Info($"ForkedRoad merchant spectator switching to mirrored shop scene: branch={message.branchSequence} coord={_activeGroup.Coord}");
        TaskHelper.RunSafely(ForkedRoadPatches.ShowSpectatorMerchantAsync(_runState));
    }

    private static void HandleBranchMergeMessage(ForkedRoadBranchMergeMessage message, ulong _senderId)
    {
        if (_runState == null || message.actIndex != _runState.CurrentActIndex)
        {
            return;
        }

        PlayerMergeTargets[message.playerId] = message.targetCoord;
        Log.Info($"ForkedRoad synchronized merge target for player {message.playerId} into branch {message.targetCoord}.");
    }

    private static void HandleBranchMergeResolvedMessage(ForkedRoadBranchMergeResolvedMessage message, ulong _senderId)
    {
        if (_runState == null || !_splitBatchInProgress || _activeGroup == null)
        {
            return;
        }
        if (message.actIndex != _runState.CurrentActIndex || message.branchSequence != _activeBranchSequence || message.coord != _activeGroup.Coord)
        {
            return;
        }
        if (!TryBeginCurrentBranchMergeCleanup())
        {
            return;
        }

        Log.Info($"ForkedRoad received branch merge resolution for branch {message.branchSequence} at {message.coord}; unwinding local room stack.");
        TaskHelper.RunSafely(HandleBranchMergeResolvedAsync(message.branchSequence));
    }

    private static async Task HandleBranchMergeResolvedAsync(int branchSequence)
    {
        try
        {
            if (_runState != null)
            {
                await ForkedRoadPatches.TransitionMergedBranchToMapAsync(_runState);
            }
            NotifyLocalTerminalProceed();
        }
        finally
        {
            CompleteBranchMergeCleanup(branchSequence);
        }
    }

    private static void MarkPlayerReady(ulong playerId, bool shouldSendToHost)
    {
        if (_runState == null || _netService == null || _activeGroup == null)
        {
            return;
        }
        if (!ReadyPlayers.Add(playerId))
        {
            return;
        }

        IReadOnlyCollection<ulong> requiredPlayers = GetPlayersRequiredToAdvanceCurrentBranch();
        Log.Info($"ForkedRoad player ready for next branch: {playerId} branch={_activeBranchSequence} ready={requiredPlayers.Count(id => ReadyPlayers.Contains(id))}/{requiredPlayers.Count} requiresAll={RequiresAllPlayersReadyForCurrentBranch()}");

        if (shouldSendToHost)
        {
            _netService.SendMessage(new ForkedRoadBranchContinueMessage
            {
                actIndex = _runState.CurrentActIndex,
                branchSequence = _activeBranchSequence
            });
        }

        if (_netService.Type != NetGameType.Client)
        {
            TryAdvanceBranch();
        }
    }

    private static void TryAdvanceBranch()
    {
        if (_netService == null || _activeGroup == null)
        {
            return;
        }

        IReadOnlyCollection<ulong> requiredPlayers = GetPlayersRequiredToAdvanceCurrentBranch();
        if (requiredPlayers.Any((ulong id) => !ReadyPlayers.Contains(id)))
        {
            return;
        }

        if (!_branchEndedByMerge && !CompletedBranchCoords.Contains(_activeGroup.Coord))
        {
            CompletedBranchCoords.Add(_activeGroup.Coord);
        }

        ReadyPlayers.Clear();
        _activeGroup = null;
        _branchEndedByMerge = false;
        _mergeCleanupBranchSequence = -1;
        if (PendingGroups.Count > 0)
        {
            SendNextBranchStartMessage();
        }
        else
        {
            ForkedRoadBranchCompleteMessage message = new ForkedRoadBranchCompleteMessage
            {
                actIndex = _runState!.CurrentActIndex
            };
            _netService.SendMessage(message);
            CompleteBatchLocal();
        }
    }

    private static void CompleteBatchLocal()
    {
        foreach ((ulong playerId, MapCoord targetCoord) in PlayerMergeTargets.ToArray())
        {
            PlayerCoords[playerId] = targetCoord;
            ReplaceCurrentVisitedCoord(playerId, targetCoord);
            PlayersFollowingMergedBranch.Add(playerId);
            Log.Info($"ForkedRoad keeping merged follower {playerId} locked to branch {targetCoord} until the next room is entered.");
            Player? player = _runState?.Players.FirstOrDefault(currentPlayer => currentPlayer.NetId == playerId);
            if (player?.Creature.IsDead == true)
            {
                player.ActivateHooks();
                TaskHelper.RunSafely(player.ReviveBeforeCombatEnd());
            }
        }
        PlayerMergeTargets.Clear();

        if (_runState != null)
        {
            ForkedRoadPatches.RestoreLocalRunLocation(_runState);
        }

        PendingGroups.Clear();
        _activeGroup = null;
        _splitBatchInProgress = false;
        _activeBranchSequence = 0;
        _remainingBranchesAfterCurrent = 0;
        ReadyPlayers.Clear();
        PlayersSkippingCurrentRoomRewards.Clear();
        CompletedBranchCoords.Clear();
        BranchPlayerCounts.Clear();
        _branchEndedByMerge = false;
        _mergeCleanupBranchSequence = -1;
        _pendingReadyBranchSequence = -1;
        RefreshDisplayedPlayers();
        if (NMapScreen.Instance != null)
        {
            NMapScreen.Instance.SetTravelEnabled(enabled: true);
            NMapScreen.Instance.Open();
        }
    }

    private static void ClearRuntimeState()
    {
        PlayerCoords.Clear();
        PlayerVisitedCoords.Clear();
        PendingGroups.Clear();
        _activeGroup = null;
        _splitBatchInProgress = false;
        _activeBranchSequence = 0;
        _remainingBranchesAfterCurrent = 0;
        ReadyPlayers.Clear();
        PlayerMergeTargets.Clear();
        PlayersSkippingCurrentRoomRewards.Clear();
        PlayersFollowingMergedBranch.Clear();
        CompletedBranchCoords.Clear();
        BranchPlayerCounts.Clear();
        _branchEndedByMerge = false;
        _mergeCleanupBranchSequence = -1;
        _pendingReadyBranchSequence = -1;
        RefreshDisplayedPlayers();
    }
}
