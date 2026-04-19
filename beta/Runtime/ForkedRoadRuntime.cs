using System.Collections.Generic;
using System.Linq;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Rooms;

namespace ForkedRoad;

internal enum RouteSplitRunPhase
{
    SharedMapSelection,
    BatchLocked,
    ParallelRoomsRunning,
    BatchResolved
}

internal enum RouteSplitPlayerPhase
{
    ChoosingRoute,
    InOwnBranchRoom,
    FinishedWaiting,
    SpectatingOtherBranch,
    ReadyForNextBatch
}

internal enum RouteSplitBranchPhase
{
    PendingEnter,
    InProgress,
    Completed
}

public enum SpectatorViewKind
{
    None,
    Combat,
    Event,
    Treasure
}

internal sealed class BranchSpectatorViewState
{
    public SpectatorViewKind Kind { get; set; }

    public string Title { get; set; } = string.Empty;

    public string Description { get; set; } = string.Empty;

    public List<string> Options { get; set; } = new();

    public List<string> OptionDescriptions { get; set; } = new();

    public bool IsInteractionBlocked { get; set; } = true;

    public int Revision { get; set; }
}

internal sealed class PlayerBranchRuntime
{
    public required ulong PlayerId { get; init; }

    public int? CurrentBranchId { get; set; }

    public MapCoord? SelectionCoord { get; set; }

    public RouteSplitPlayerPhase Phase { get; set; } = RouteSplitPlayerPhase.ChoosingRoute;

    public int? SpectatingBranchId { get; set; }

    public MapCoord? MapVoteDestinationCoord { get; set; }

    public bool IsEliminated { get; set; }
}

internal sealed class SpectatorRuntimeState
{
    public List<int> AvailableBranchIds { get; set; } = new();

    public List<ulong?> AvailablePlayerIds { get; set; } = new();

    public int CurrentViewedBranchIndex { get; set; }

    public bool CanSwitchLeft => AvailableBranchIds.Count > 1;

    public bool CanSwitchRight => AvailableBranchIds.Count > 1;

    public int? CurrentBranchId
    {
        get
        {
            if (AvailableBranchIds.Count == 0)
            {
                return null;
            }

            if (CurrentViewedBranchIndex < 0 || CurrentViewedBranchIndex >= AvailableBranchIds.Count)
            {
                return null;
            }

            return AvailableBranchIds[CurrentViewedBranchIndex];
        }
    }

    public ulong? CurrentPlayerId
    {
        get
        {
            if (AvailablePlayerIds.Count == 0)
            {
                return null;
            }

            if (CurrentViewedBranchIndex < 0 || CurrentViewedBranchIndex >= AvailablePlayerIds.Count)
            {
                return null;
            }

            return AvailablePlayerIds[CurrentViewedBranchIndex];
        }
    }
}

internal sealed class BranchGroupRuntime
{
    public required int BranchId { get; init; }

    public required MapCoord TargetCoord { get; init; }

    public ulong AuthorityPlayerId { get; set; }

    public required List<ulong> PlayerIds { get; init; } = new();

    public HashSet<ulong> EnteredPlayerIds { get; } = new();

    public HashSet<ulong> ReadyPlayerIds { get; } = new();

    public HashSet<ulong> EliminatedPlayerIds { get; } = new();

    public RoomType? RoomType { get; set; }

    public MapPointType? PointType { get; set; }

    public ModelId? ResolvedModelId { get; set; }

    public RouteSplitBranchPhase Phase { get; set; } = RouteSplitBranchPhase.PendingEnter;

    public int? CompletionOrder { get; set; }

    public ModelId? EncounterId { get; set; }

    public NetFullCombatState? LatestCombatState { get; set; }

    public BranchSpectatorViewState? LatestViewState { get; set; }

    public int AlliedCreatureCount { get; set; }

    public bool SuppressCombatRewards { get; set; }

    public bool DeathClearTriggered { get; set; }
}

internal sealed class BranchBatchRuntime
{
    public required int BatchId { get; init; }

    public required int ActIndex { get; init; }

    public RouteSplitRunPhase Phase { get; set; } = RouteSplitRunPhase.BatchLocked;

    public List<MapCoord> SourceCoords { get; } = new();

    public List<BranchGroupRuntime> BranchGroups { get; } = new();

    public BranchGroupRuntime? FindBranch(int branchId)
    {
        return BranchGroups.FirstOrDefault(group => group.BranchId == branchId);
    }

    public BranchGroupRuntime? FindBranchForPlayer(ulong playerId)
    {
        return BranchGroups.FirstOrDefault(group => group.PlayerIds.Contains(playerId));
    }
}

internal sealed class RouteSplitRuntime
{
    public RouteSplitRunPhase Phase { get; set; } = RouteSplitRunPhase.SharedMapSelection;

    public int NextBatchId { get; set; } = 1;

    public bool RequiresAuthoritativeRoomPlans { get; set; }

    public BranchBatchRuntime? ActiveBatch { get; set; }

    public Dictionary<ulong, PlayerBranchRuntime> Players { get; } = new();

    public Dictionary<ulong, SpectatorRuntimeState> Spectators { get; } = new();
}

