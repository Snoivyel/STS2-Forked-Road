using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;
using MegaCrit.Sts2.Core.Rooms;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Saves;
using MegaCrit.Sts2.Core.TestSupport;

namespace ForkedRoad;

internal static partial class ForkedRoadManager
{
    internal static bool TryHandleSplitMapResolution(MapSelectionSynchronizer synchronizer)
    {
        if (_runState == null || _netService == null || _netService.Type != NetGameType.Host || IsSplitBatchInProgress)
        {
            return false;
        }

        List<MapVote?> votes = MapSelectionVotesRef(synchronizer);
        if (votes.Count != _runState.Players.Count)
        {
            return false;
        }

        IReadOnlyList<Player> routeChoosingPlayers = GetRouteChoosingPlayers();
        if (routeChoosingPlayers.Count == 0)
        {
            return false;
        }

        List<(Player player, MapCoord destinationCoord)> resolvedVotes = new();
        foreach (Player player in routeChoosingPlayers)
        {
            if (!Runtime.Players.TryGetValue(player.NetId, out PlayerBranchRuntime? runtimePlayer) || !runtimePlayer.MapVoteDestinationCoord.HasValue)
            {
                return false;
            }

            resolvedVotes.Add((player, runtimePlayer.MapVoteDestinationCoord.Value));
        }

        List<IGrouping<MapCoord, (Player player, MapCoord destinationCoord)>> groupedVotes = resolvedVotes
            .GroupBy(static pair => pair.destinationCoord)
            .OrderBy(static group => group.Key.row)
            .ThenBy(static group => group.Key.col)
            .ToList();

        bool hasMultipleSourceCoords = Runtime.Players.Values
            .Where(player => player.SelectionCoord.HasValue)
            .Select(player => player.SelectionCoord!.Value)
            .Distinct()
            .Take(2)
            .Count() > 1;
        bool shouldRunCustomBatch = groupedVotes.Count > 1 || hasMultipleSourceCoords || Runtime.RequiresAuthoritativeRoomPlans || HasEliminatedPlayers();
        if (!shouldRunCustomBatch)
        {
            return false;
        }

        int batchId = Runtime.NextBatchId++;
        BranchBatchRuntime batch = new()
        {
            BatchId = batchId,
            ActIndex = _runState.CurrentActIndex,
            Phase = RouteSplitRunPhase.BatchLocked
        };

        foreach (Player player in _runState.Players)
        {
            PlayerBranchRuntime runtimePlayer = Runtime.Players[player.NetId];
            if (runtimePlayer.SelectionCoord.HasValue)
            {
                batch.SourceCoords.Add(runtimePlayer.SelectionCoord.Value);
            }
        }

        int branchOffset = 0;
        foreach (IGrouping<MapCoord, (Player player, MapCoord destinationCoord)> group in groupedVotes)
        {
            ulong authorityPlayerId = group.Any(entry => entry.player.NetId == _netService.NetId)
                ? _netService.NetId
                : group.Select(static entry => entry.player.NetId).OrderBy(static id => id).First();
            batch.BranchGroups.Add(new BranchGroupRuntime
            {
                BranchId = batchId * 100 + branchOffset,
                TargetCoord = group.Key,
                AuthorityPlayerId = authorityPlayerId,
                PlayerIds = group.Select(static entry => entry.player.NetId).ToList(),
                Phase = RouteSplitBranchPhase.PendingEnter
            });
            branchOffset++;
        }

        IReadOnlyList<Player> eliminatedPlayers = _runState.Players.Where(player => IsPlayerEliminated(player.NetId)).ToList();
        foreach (Player eliminatedPlayer in eliminatedPlayers)
        {
            Runtime.Players.TryGetValue(eliminatedPlayer.NetId, out PlayerBranchRuntime? runtimePlayer);
            HashSet<MapCoord> aliveChosenCoords = resolvedVotes.Select(static pair => pair.destinationCoord).ToHashSet();
            BranchGroupRuntime? assignedBranch = null;
            if (runtimePlayer?.MapVoteDestinationCoord.HasValue == true && aliveChosenCoords.Contains(runtimePlayer.MapVoteDestinationCoord.Value))
            {
                assignedBranch = batch.BranchGroups.FirstOrDefault(branch => branch.TargetCoord == runtimePlayer.MapVoteDestinationCoord.Value);
            }

            if (runtimePlayer?.SelectionCoord.HasValue == true)
            {
                HashSet<ulong> sameSourceAlivePlayers = routeChoosingPlayers
                    .Where(player => Runtime.Players.TryGetValue(player.NetId, out PlayerBranchRuntime? alivePlayer) &&
                                     alivePlayer.SelectionCoord.HasValue &&
                                     alivePlayer.SelectionCoord.Value == runtimePlayer.SelectionCoord.Value)
                    .Select(player => player.NetId)
                    .ToHashSet();
                if (sameSourceAlivePlayers.Count > 0)
                {
                    assignedBranch = batch.BranchGroups.FirstOrDefault(branch => branch.PlayerIds.Any(sameSourceAlivePlayers.Contains));
                }
            }

            assignedBranch ??= batch.BranchGroups
                .Where(branch => branch.PlayerIds.Any(playerId => !IsPlayerEliminated(playerId)))
                .OrderBy(branch => branch.TargetCoord.row)
                .ThenBy(branch => branch.TargetCoord.col)
                .FirstOrDefault();
            assignedBranch ??= batch.BranchGroups.FirstOrDefault();
            assignedBranch?.PlayerIds.Add(eliminatedPlayer.NetId);
        }

        Runtime.RequiresAuthoritativeRoomPlans = true;

        synchronizer.OnRunLocationChanged(_runState.CurrentLocation);
        ForkedRoadBatchLockedMessage message = new()
        {
            actIndex = batch.ActIndex,
            batchId = batch.BatchId,
            mapGenerationCount = synchronizer.MapGenerationCount,
            requiresAuthoritativeRoomPlan = Runtime.RequiresAuthoritativeRoomPlans,
            sourceCoords = batch.SourceCoords.Distinct().ToList(),
            branches = batch.BranchGroups.Select(static branch => new ForkedRoadBranchDescriptor
            {
                branchId = branch.BranchId,
                targetCoord = branch.TargetCoord,
                authorityPlayerId = branch.AuthorityPlayerId,
                playerIds = branch.PlayerIds.ToList()
            }).ToList()
        };

        _netService.SendMessage(message);
        HandleBatchLockedMessage(message, _netService.NetId);
        Log.Info($"ForkedRoad locked split batch {batch.BatchId} with {batch.BranchGroups.Count} branches.");
        return true;
    }

    private static void HandleBatchLockedMessage(ForkedRoadBatchLockedMessage message, ulong senderId)
    {
        if (_runState == null)
        {
            return;
        }

        EnsureRuntimePlayers();
        BranchBatchRuntime batch = new()
        {
            BatchId = message.batchId,
            ActIndex = message.actIndex,
            Phase = RouteSplitRunPhase.BatchLocked
        };
        batch.SourceCoords.AddRange(message.sourceCoords);
        foreach (ForkedRoadBranchDescriptor descriptor in message.branches)
        {
            batch.BranchGroups.Add(new BranchGroupRuntime
            {
                BranchId = descriptor.branchId,
                TargetCoord = descriptor.targetCoord,
                AuthorityPlayerId = descriptor.authorityPlayerId,
                PlayerIds = descriptor.playerIds.ToList(),
                Phase = RouteSplitBranchPhase.PendingEnter
            });
        }

        Runtime.ActiveBatch = batch;
        Runtime.Phase = RouteSplitRunPhase.BatchLocked;
        Runtime.RequiresAuthoritativeRoomPlans = message.requiresAuthoritativeRoomPlan;
        Runtime.Spectators.Clear();
        ClearSeededSplitLocations();
        SeedBranchRunLocations(batch);

        foreach (PlayerBranchRuntime player in Runtime.Players.Values)
        {
            BranchGroupRuntime? branch = batch.FindBranchForPlayer(player.PlayerId);
            if (branch != null && player.IsEliminated)
            {
                player.IsEliminated = false;
                Player? runPlayer = _runState.GetPlayer(player.PlayerId);
                if (runPlayer?.Creature.IsDead == true)
                {
                    runPlayer.Creature.HealInternal(1m);
                }
            }

            player.CurrentBranchId = branch?.BranchId;
            player.SelectionCoord = branch?.TargetCoord ?? player.SelectionCoord;
            player.SpectatingBranchId = null;
            player.MapVoteDestinationCoord = null;
            player.Phase = branch != null
                ? (player.IsEliminated ? RouteSplitPlayerPhase.FinishedWaiting : RouteSplitPlayerPhase.InOwnBranchRoom)
                : RouteSplitPlayerPhase.ChoosingRoute;
        }

        ApplyHookActivationForLocalBranch();
        ApplyPlayerChoiceNamespacesForActiveBatch();
        NormalizeRunLocationBuffer(_runState.CurrentLocation);
        RefreshUiState();

        BranchGroupRuntime? localBranch = GetLocalBranch();
        if (localBranch != null)
        {
            _ = TaskHelper.RunSafely(EnterAssignedBranchAsync(localBranch));
        }
    }

    private static async Task EnterAssignedBranchAsync(BranchGroupRuntime branch)
    {
        if (_runState == null || Runtime.ActiveBatch == null)
        {
            return;
        }

        await Task.Delay(50);
        if (Runtime.ActiveBatch.FindBranch(branch.BranchId) == null)
        {
            return;
        }

        RevivePlayersForUpcomingBranch(branch);
        Log.Info($"ForkedRoad entering assigned branch {branch.BranchId} at {branch.TargetCoord}.");
        if (ShouldUseAuthoritativeRoomPlan(branch))
        {
            if (LocalContext.NetId == branch.AuthorityPlayerId)
            {
                ResolvedBranchRoomPlan plan = ResolveRoomPlanForBranch(branch);
                ApplyResolvedRoomPlan(branch, plan);
                BroadcastResolvedRoomPlan(branch, plan);
                await EnterResolvedRoomAsync(branch, plan);
                return;
            }

            if (await WaitForResolvedRoomPlanAsync(branch) && TryGetResolvedRoomPlan(branch, out ResolvedBranchRoomPlan resolvedPlan))
            {
                await EnterResolvedRoomAsync(branch, resolvedPlan);
                return;
            }

            Log.Warn($"ForkedRoad timed out waiting for resolved room plan for branch {branch.BranchId}, falling back to local entry.");
        }

        if (TestMode.IsOn)
        {
            await RunManager.Instance.EnterMapCoord(branch.TargetCoord);
            return;
        }

        if (NMapScreen.Instance?.IsOpen == true)
        {
            await NMapScreen.Instance.TravelToMapCoord(branch.TargetCoord);
        }
        else
        {
            await RunManager.Instance.EnterMapCoord(branch.TargetCoord);
        }
    }

    private static void HandleLocalRoomEntered()
    {
        if (_runState == null || _netService == null || !LocalContext.NetId.HasValue || Runtime.ActiveBatch == null)
        {
            return;
        }

        BranchGroupRuntime? localBranch = GetLocalBranch();
        AbstractRoom? room = _runState.CurrentRoom;
        if (localBranch == null || room == null || room.RoomType == RoomType.Map)
        {
            return;
        }

        ulong localPlayerId = LocalContext.NetId.Value;
        if (!localBranch.EnteredPlayerIds.Add(localPlayerId))
        {
            return;
        }

        if (Runtime.Players.TryGetValue(localPlayerId, out PlayerBranchRuntime? player))
        {
            player.SelectionCoord = localBranch.TargetCoord;
            player.Phase = RouteSplitPlayerPhase.InOwnBranchRoom;
        }

        localBranch.RoomType = room.RoomType;
        localBranch.Phase = RouteSplitBranchPhase.InProgress;
        Runtime.Phase = RouteSplitRunPhase.ParallelRoomsRunning;

        ForkedRoadBranchRoomEnteredMessage message = new()
        {
            actIndex = Runtime.ActiveBatch.ActIndex,
            batchId = Runtime.ActiveBatch.BatchId,
            branchId = localBranch.BranchId,
            coord = localBranch.TargetCoord,
            roomType = room.RoomType
        };
        _netService.SendMessage(message);

        if (room is CombatRoom combatRoom)
        {
            localBranch.EncounterId = combatRoom.Encounter.Id;
        }

        if (_netService.Type == NetGameType.Host)
        {
            TaskHelper.RunSafely(SaveManager.Instance.SaveRun(null, saveProgress: false));
        }

        RefreshUiState();
    }

    private static void HandleBranchRoomEnteredMessage(ForkedRoadBranchRoomEnteredMessage message, ulong senderId)
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

        branch.RoomType = message.roomType;
        branch.EnteredPlayerIds.Add(senderId);
        branch.Phase = RouteSplitBranchPhase.InProgress;
        foreach (ulong playerId in branch.PlayerIds)
        {
            if (Runtime.Players.TryGetValue(playerId, out PlayerBranchRuntime? player))
            {
                player.SelectionCoord = message.coord;
                player.MapVoteDestinationCoord = null;
            }
        }
        Runtime.Phase = RouteSplitRunPhase.ParallelRoomsRunning;
        RefreshUiState();
    }
}
