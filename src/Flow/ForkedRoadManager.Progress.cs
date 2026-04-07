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

        ActivateLocalSpectatorState(branch.BranchId);
        RefreshUiState();

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
            if (state.CurrentBranchId == player.CurrentBranchId)
            {
                player.Phase = RouteSplitPlayerPhase.FinishedWaiting;
                player.SpectatingBranchId = null;
            }
            else
            {
                player.Phase = RouteSplitPlayerPhase.SpectatingOtherBranch;
                player.SpectatingBranchId = state.CurrentBranchId;
            }
        }

        ForkedRoadPlayerSpectateTargetChangedMessage message = new()
        {
            actIndex = Runtime.ActiveBatch.ActIndex,
            batchId = Runtime.ActiveBatch.BatchId,
            branchId = Runtime.Players.TryGetValue(localPlayerId, out PlayerBranchRuntime? localPlayer) && state.CurrentBranchId == localPlayer.CurrentBranchId
                ? null
                : state.CurrentBranchId
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

        if ((LocalContext.NetId ?? 0ul) == senderId)
        {
            ActivateLocalSpectatorState(branch.BranchId);
        }

        if (branch.ReadyPlayerIds.Count >= GetRequiredReadyCount(branch))
        {
            branch.Phase = RouteSplitBranchPhase.Completed;
            branch.CompletionOrder = Runtime.ActiveBatch.BranchGroups.Count(static group => group.CompletionOrder.HasValue) + 1;
            UpdateSpectatorStatesForCompletedBranch(branch.BranchId);
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
            bool isOwnCompletedBranch = Runtime.Players.TryGetValue(pair.Key, out PlayerBranchRuntime? completedPlayer) &&
                                        completedPlayer.CurrentBranchId == branchId;
            if (!isOwnCompletedBranch)
            {
                for (int index = state.AvailableBranchIds.Count - 1; index >= 0; index--)
                {
                    if (state.AvailableBranchIds[index] != branchId)
                    {
                        continue;
                    }

                    state.AvailableBranchIds.RemoveAt(index);
                    if (state.AvailablePlayerIds.Count > index)
                    {
                        state.AvailablePlayerIds.RemoveAt(index);
                    }
                }
            }
            if (state.CurrentViewedBranchIndex >= state.AvailableBranchIds.Count)
            {
                state.CurrentViewedBranchIndex = System.Math.Max(0, state.AvailableBranchIds.Count - 1);
            }

            if (Runtime.Players.TryGetValue(pair.Key, out PlayerBranchRuntime? player))
            {
                if (state.CurrentBranchId.HasValue && state.CurrentBranchId != player.CurrentBranchId)
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

        (List<int> availableBranches, List<ulong?> availablePlayers) = BuildSpectatorTargets(completedBranchId, LocalContext.NetId.Value, includeOnlyUncompletedRemoteBranches: true);
        Log.Info($"ForkedRoad activating spectator state for local player {LocalContext.NetId.Value}: completedBranch={completedBranchId} available=[{string.Join(",", availableBranches.Zip(availablePlayers, (branchId, playerId) => $"{branchId}:{playerId?.ToString() ?? "none"}"))}]");

        if (availableBranches.Count <= 1)
        {
            localPlayer.Phase = RouteSplitPlayerPhase.ReadyForNextBatch;
            localPlayer.SpectatingBranchId = null;
            Runtime.Spectators.Remove(LocalContext.NetId.Value);
            Log.Info("ForkedRoad spectator state skipped because there are no alternate branches to view.");
            return;
        }

        int preferredIndex = availableBranches.FindIndex(branchId => branchId != completedBranchId);
        if (preferredIndex < 0)
        {
            preferredIndex = 0;
        }

        SpectatorRuntimeState state = new();
        ApplySpectatorTargets(state, availableBranches, availablePlayers, preferredIndex);
        Runtime.Spectators[LocalContext.NetId.Value] = state;
        int viewedBranchId = availableBranches[preferredIndex];
        if (viewedBranchId == completedBranchId)
        {
            localPlayer.Phase = RouteSplitPlayerPhase.FinishedWaiting;
            localPlayer.SpectatingBranchId = null;
        }
        else
        {
            localPlayer.Phase = RouteSplitPlayerPhase.SpectatingOtherBranch;
            localPlayer.SpectatingBranchId = viewedBranchId;
        }
        Log.Info($"ForkedRoad spectator state active: localPhase={localPlayer.Phase} viewedBranch={viewedBranchId} currentBranch={localPlayer.CurrentBranchId}");
    }

    private static void HandlePlayerSpectateTargetChangedMessage(ForkedRoadPlayerSpectateTargetChangedMessage message, ulong senderId)
    {
        if (Runtime.ActiveBatch?.BatchId != message.batchId)
        {
            return;
        }

        if (!Runtime.Spectators.TryGetValue(senderId, out SpectatorRuntimeState? state))
        {
            return;
        }

        if (!message.branchId.HasValue)
        {
            if (Runtime.Players.TryGetValue(senderId, out PlayerBranchRuntime? player))
            {
                int selfIndex = -1;
                for (int index = 0; index < state.AvailableBranchIds.Count; index++)
                {
                    ulong? playerId = state.AvailablePlayerIds.Count > index ? state.AvailablePlayerIds[index] : null;
                    if (state.AvailableBranchIds[index] == (player.CurrentBranchId ?? -1) &&
                        (!playerId.HasValue || playerId.Value == senderId))
                    {
                        selfIndex = index;
                        break;
                    }
                }
                if (selfIndex >= 0)
                {
                    state.CurrentViewedBranchIndex = selfIndex;
                }

                player.SpectatingBranchId = null;
                player.Phase = RouteSplitPlayerPhase.FinishedWaiting;
            }
        }
        else
        {
            int index = state.AvailableBranchIds.FindIndex(branchId => branchId == message.branchId.Value);
            if (index >= 0)
            {
                state.CurrentViewedBranchIndex = index;
                if (Runtime.Players.TryGetValue(senderId, out PlayerBranchRuntime? player))
                {
                    player.SpectatingBranchId = message.branchId.Value;
                    player.Phase = RouteSplitPlayerPhase.SpectatingOtherBranch;
                }
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
        if (_netService?.Type == NetGameType.Host)
        {
            if (HasDivergedPlayerLocations())
            {
                CaptureSaveRestoreSnapshotForCurrentRun();
            }
            else
            {
                DeleteSaveRestoreSnapshotFile();
            }
        }
        NormalizeRunLocationBuffer(_runState!.CurrentLocation);
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
