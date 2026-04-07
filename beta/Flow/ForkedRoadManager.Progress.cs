using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using MegaCrit.Sts2.Core.Context;
using MegaCrit.Sts2.Core.Entities.Players;
using MegaCrit.Sts2.Core.Helpers;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Nodes.Screens.Map;

namespace ForkedRoad;

internal static partial class ForkedRoadManager
{
    internal static void NotifyLocalBranchCompleted(string source)
    {
        if (_runState == null || _netService == null || !LocalContext.NetId.HasValue || Runtime.ActiveBatch == null)
        {
            return;
        }

        BranchGroupRuntime? branch = GetLocalBranch();
        if (branch == null)
        {
            return;
        }

        ulong localPlayerId = LocalContext.NetId.Value;
        if (branch.ReadyPlayerIds.Contains(localPlayerId))
        {
            return;
        }

        Log.Info($"ForkedRoad local branch completion requested via {source}: batch={Runtime.ActiveBatch.BatchId} branch={branch.BranchId}");
        ForkedRoadBranchRoomCompletedMessage message = new()
        {
            actIndex = Runtime.ActiveBatch.ActIndex,
            batchId = Runtime.ActiveBatch.BatchId,
            branchId = branch.BranchId
        };
        _netService.SendMessage(message);
        HandleBranchRoomCompletedMessage(message, localPlayerId);
    }

    internal static bool TryCycleLocalSpectatorBranch(int delta)
    {
        if (!LocalContext.NetId.HasValue || _netService == null || Runtime.ActiveBatch == null)
        {
            return false;
        }

        ulong localPlayerId = LocalContext.NetId.Value;
        if (!Runtime.Spectators.TryGetValue(localPlayerId, out SpectatorRuntimeState? state) || state.AvailableBranchIds.Count == 0)
        {
            return false;
        }

        int count = state.AvailableBranchIds.Count;
        state.CurrentViewedBranchIndex = (state.CurrentViewedBranchIndex + delta) % count;
        if (state.CurrentViewedBranchIndex < 0)
        {
            state.CurrentViewedBranchIndex += count;
        }

        if (Runtime.Players.TryGetValue(localPlayerId, out PlayerBranchRuntime? player))
        {
            player.Phase = RouteSplitPlayerPhase.SpectatingOtherBranch;
            player.SpectatingBranchId = state.CurrentBranchId;
        }

        ForkedRoadPlayerSpectateTargetChangedMessage message = new()
        {
            actIndex = Runtime.ActiveBatch.ActIndex,
            batchId = Runtime.ActiveBatch.BatchId,
            branchId = state.CurrentBranchId
        };
        _netService.SendMessage(message);
        RefreshUiState();
        return true;
    }

    private static void HandleBranchRoomCompletedMessage(ForkedRoadBranchRoomCompletedMessage message, ulong senderId)
    {
        if (Runtime.ActiveBatch?.BatchId != message.batchId)
        {
            return;
        }

        BranchGroupRuntime? branch = Runtime.ActiveBatch.FindBranch(message.branchId);
        if (branch == null || !branch.PlayerIds.Contains(senderId) || !branch.ReadyPlayerIds.Add(senderId))
        {
            return;
        }

        if (Runtime.Players.TryGetValue(senderId, out PlayerBranchRuntime? playerState))
        {
            playerState.Phase = playerState.IsEliminated ? RouteSplitPlayerPhase.ReadyForNextBatch : RouteSplitPlayerPhase.FinishedWaiting;
            playerState.SpectatingBranchId = null;
            playerState.CurrentBranchId = branch.BranchId;
            playerState.SelectionCoord = branch.TargetCoord;
            playerState.MapVoteDestinationCoord = null;
        }

        if (branch.ReadyPlayerIds.Count >= GetRequiredReadyCount(branch))
        {
            branch.Phase = RouteSplitBranchPhase.Completed;
            branch.CompletionOrder = Runtime.ActiveBatch.BranchGroups.Count(static group => group.CompletionOrder.HasValue) + 1;
            UpdateSpectatorStatesForCompletedBranch(branch.BranchId);
            if (branch.PlayerIds.Contains(LocalContext.NetId ?? 0ul))
            {
                ActivateLocalSpectatorState(branch.BranchId);
            }
        }

        if (_netService?.Type == NetGameType.Host && Runtime.ActiveBatch.BranchGroups.All(static group => group.Phase == RouteSplitBranchPhase.Completed))
        {
            ForkedRoadBatchAllCompletedMessage completedMessage = new()
            {
                actIndex = Runtime.ActiveBatch.ActIndex,
                batchId = Runtime.ActiveBatch.BatchId
            };
            _netService.SendMessage(completedMessage);
            HandleBatchAllCompletedMessage(completedMessage, _netService.NetId);
            return;
        }

        RefreshUiState();
    }

    private static void UpdateSpectatorStatesForCompletedBranch(int branchId)
    {
        foreach (KeyValuePair<ulong, SpectatorRuntimeState> pair in Runtime.Spectators.ToList())
        {
            SpectatorRuntimeState state = pair.Value;
            int index = state.AvailableBranchIds.IndexOf(branchId);
            if (index < 0)
            {
                continue;
            }

            state.AvailableBranchIds.RemoveAt(index);
            if (state.CurrentViewedBranchIndex >= state.AvailableBranchIds.Count)
            {
                state.CurrentViewedBranchIndex = System.Math.Max(0, state.AvailableBranchIds.Count - 1);
            }

            if (Runtime.Players.TryGetValue(pair.Key, out PlayerBranchRuntime? player))
            {
                if (state.CurrentBranchId.HasValue)
                {
                    player.SpectatingBranchId = state.CurrentBranchId.Value;
                    player.Phase = RouteSplitPlayerPhase.SpectatingOtherBranch;
                }
                else
                {
                    player.SpectatingBranchId = null;
                    player.Phase = RouteSplitPlayerPhase.ReadyForNextBatch;
                }
            }
        }
    }

    private static void ActivateLocalSpectatorState(int completedBranchId)
    {
        if (!LocalContext.NetId.HasValue || Runtime.ActiveBatch == null || !Runtime.Players.TryGetValue(LocalContext.NetId.Value, out PlayerBranchRuntime? localPlayer))
        {
            return;
        }

        List<int> availableBranches = Runtime.ActiveBatch.BranchGroups
            .Where(group => group.Phase != RouteSplitBranchPhase.Completed && group.BranchId != completedBranchId)
            .Select(group => group.BranchId)
            .ToList();

        if (availableBranches.Count == 0)
        {
            localPlayer.Phase = RouteSplitPlayerPhase.ReadyForNextBatch;
            localPlayer.SpectatingBranchId = null;
            Runtime.Spectators.Remove(LocalContext.NetId.Value);
            return;
        }

        Runtime.Spectators[LocalContext.NetId.Value] = new SpectatorRuntimeState
        {
            AvailableBranchIds = availableBranches,
            CurrentViewedBranchIndex = 0
        };
        localPlayer.Phase = RouteSplitPlayerPhase.SpectatingOtherBranch;
        localPlayer.SpectatingBranchId = availableBranches[0];
    }

    private static void HandlePlayerSpectateTargetChangedMessage(ForkedRoadPlayerSpectateTargetChangedMessage message, ulong senderId)
    {
        if (Runtime.ActiveBatch?.BatchId != message.batchId || !message.branchId.HasValue)
        {
            return;
        }

        if (!Runtime.Spectators.TryGetValue(senderId, out SpectatorRuntimeState? state))
        {
            return;
        }

        int index = state.AvailableBranchIds.IndexOf(message.branchId.Value);
        if (index >= 0)
        {
            state.CurrentViewedBranchIndex = index;
            if (Runtime.Players.TryGetValue(senderId, out PlayerBranchRuntime? player))
            {
                player.SpectatingBranchId = message.branchId.Value;
                player.Phase = RouteSplitPlayerPhase.SpectatingOtherBranch;
            }
        }

        RefreshUiState();
    }

    private static void HandleBatchAllCompletedMessage(ForkedRoadBatchAllCompletedMessage message, ulong senderId)
    {
        if (Runtime.ActiveBatch?.BatchId != message.batchId)
        {
            return;
        }

        Runtime.Phase = RouteSplitRunPhase.BatchResolved;
        foreach (BranchGroupRuntime branch in Runtime.ActiveBatch.BranchGroups)
        {
            branch.Phase = RouteSplitBranchPhase.Completed;
            foreach (ulong playerId in branch.PlayerIds)
            {
                if (Runtime.Players.TryGetValue(playerId, out PlayerBranchRuntime? player))
                {
                    player.SelectionCoord = branch.TargetCoord;
                }
            }
        }

        foreach (PlayerBranchRuntime player in Runtime.Players.Values)
        {
            if (player.IsEliminated && _runState != null)
            {
                Player? runPlayer = _runState.GetPlayer(player.PlayerId);
                if (runPlayer?.Creature.IsDead == true)
                {
                    runPlayer.Creature.HealInternal(1m);
                }

                player.IsEliminated = false;
            }

            player.CurrentBranchId = null;
            player.SpectatingBranchId = null;
            player.MapVoteDestinationCoord = null;
            player.Phase = RouteSplitPlayerPhase.ReadyForNextBatch;
        }

        Runtime.Spectators.Clear();
        Runtime.ActiveBatch = null;
        ClearSeededSplitLocations();
        SeedMapVoteLocationsIfNeeded();
        Runtime.Phase = RouteSplitRunPhase.SharedMapSelection;
        RestoreAllHookActivation();
        NormalizeRunLocationBuffer(_runState!.RunLocation);
        RefreshUiState();
        _ = TaskHelper.RunSafely(OpenMapAfterBatchResolvedAsync());
    }

    private static async Task OpenMapAfterBatchResolvedAsync()
    {
        await Task.Delay(50);
        if (NMapScreen.Instance != null)
        {
            NMapScreen.Instance.SetTravelEnabled(enabled: true);
            NMapScreen.Instance.Open();
            SyncMapPlayerMarkers(NMapScreen.Instance);
            NMapScreen.Instance.RefreshAllMapPointVotes();
        }
    }
}
