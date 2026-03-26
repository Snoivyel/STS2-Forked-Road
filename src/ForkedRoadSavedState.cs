using System.Collections.Generic;
using System.Text.Json.Serialization;
using MegaCrit.Sts2.Core.Map;

namespace ForkedRoad;

internal sealed class ForkedRoadSavedState
{
    [JsonPropertyName("act_index")]
    public int ActIndex { get; set; }

    [JsonPropertyName("split_batch_in_progress")]
    public bool SplitBatchInProgress { get; set; }

    [JsonPropertyName("active_branch_sequence")]
    public int ActiveBranchSequence { get; set; }

    [JsonPropertyName("remaining_branches_after_current")]
    public int RemainingBranchesAfterCurrent { get; set; }

    [JsonPropertyName("active_group")]
    public ForkedRoadSavedBranchGroup? ActiveGroup { get; set; }

    [JsonPropertyName("pending_groups")]
    public List<ForkedRoadSavedBranchGroup> PendingGroups { get; set; } = new();

    [JsonPropertyName("completed_branch_coords")]
    public List<MapCoord> CompletedBranchCoords { get; set; } = new();

    [JsonPropertyName("branch_player_counts")]
    public List<ForkedRoadSavedBranchCount> BranchPlayerCounts { get; set; } = new();

    [JsonPropertyName("players")]
    public List<ForkedRoadSavedPlayerState> Players { get; set; } = new();
}

internal sealed class ForkedRoadSavedPlayerState
{
    [JsonPropertyName("player_id")]
    public ulong PlayerId { get; set; }

    [JsonPropertyName("coord")]
    public MapCoord? Coord { get; set; }

    [JsonPropertyName("visited_coords")]
    public List<MapCoord> VisitedCoords { get; set; } = new();
}

internal sealed class ForkedRoadSavedBranchGroup
{
    [JsonPropertyName("coord")]
    public MapCoord Coord { get; set; }

    [JsonPropertyName("player_ids")]
    public List<ulong> PlayerIds { get; set; } = new();
}

internal sealed class ForkedRoadSavedBranchCount
{
    [JsonPropertyName("coord")]
    public MapCoord Coord { get; set; }

    [JsonPropertyName("count")]
    public int Count { get; set; }
}
