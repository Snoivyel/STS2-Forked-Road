using System.Collections.Generic;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Models;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;
using MegaCrit.Sts2.Core.Rooms;

namespace ForkedRoad;

public struct ForkedRoadBranchDescriptor : IPacketSerializable
{
    public int branchId;

    public MapCoord targetCoord;

    public ulong authorityPlayerId;

    public List<ulong> playerIds;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteInt(branchId);
        writer.Write(targetCoord);
        writer.WriteULong(authorityPlayerId);
        writer.WriteInt(playerIds.Count);
        foreach (ulong playerId in playerIds)
        {
            writer.WriteULong(playerId);
        }
    }

    public void Deserialize(PacketReader reader)
    {
        branchId = reader.ReadInt();
        targetCoord = reader.Read<MapCoord>();
        authorityPlayerId = reader.ReadULong();
        int count = reader.ReadInt();
        playerIds = new List<ulong>(count);
        for (int i = 0; i < count; i++)
        {
            playerIds.Add(reader.ReadULong());
        }
    }
}

public struct ForkedRoadBatchLockedMessage : INetMessage
{
    public int actIndex;

    public int batchId;

    public int mapGenerationCount;

    public bool requiresAuthoritativeRoomPlan;

    public List<MapCoord> sourceCoords;

    public List<ForkedRoadBranchDescriptor> branches;

    public bool ShouldBroadcast => true;

    public NetTransferMode Mode => NetTransferMode.Reliable;

    public LogLevel LogLevel => LogLevel.Info;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteInt(actIndex);
        writer.WriteInt(batchId);
        writer.WriteInt(mapGenerationCount);
        writer.WriteBool(requiresAuthoritativeRoomPlan);
        writer.WriteList(sourceCoords);
        writer.WriteList(branches);
    }

    public void Deserialize(PacketReader reader)
    {
        actIndex = reader.ReadInt();
        batchId = reader.ReadInt();
        mapGenerationCount = reader.ReadInt();
        requiresAuthoritativeRoomPlan = reader.ReadBool();
        sourceCoords = reader.ReadList<MapCoord>();
        branches = reader.ReadList<ForkedRoadBranchDescriptor>();
    }
}

public struct ForkedRoadBranchRoomPlanMessage : INetMessage
{
    public int actIndex;

    public int batchId;

    public int branchId;

    public MapCoord coord;

    public MapPointType pointType;

    public RoomType roomType;

    public bool hasModelId;

    public string? modelCategory;

    public string? modelEntry;

    public bool ShouldBroadcast => true;

    public NetTransferMode Mode => NetTransferMode.Reliable;

    public LogLevel LogLevel => LogLevel.Info;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteInt(actIndex);
        writer.WriteInt(batchId);
        writer.WriteInt(branchId);
        writer.Write(coord);
        writer.WriteEnum(pointType);
        writer.WriteEnum(roomType);
        writer.WriteBool(hasModelId);
        if (hasModelId)
        {
            writer.WriteString(modelCategory ?? string.Empty);
            writer.WriteString(modelEntry ?? string.Empty);
        }
    }

    public void Deserialize(PacketReader reader)
    {
        actIndex = reader.ReadInt();
        batchId = reader.ReadInt();
        branchId = reader.ReadInt();
        coord = reader.Read<MapCoord>();
        pointType = reader.ReadEnum<MapPointType>();
        roomType = reader.ReadEnum<RoomType>();
        hasModelId = reader.ReadBool();
        if (hasModelId)
        {
            modelCategory = reader.ReadString();
            modelEntry = reader.ReadString();
        }
        else
        {
            modelCategory = null;
            modelEntry = null;
        }
    }
}

public struct ForkedRoadBranchRoomEnteredMessage : INetMessage
{
    public int actIndex;

    public int batchId;

    public int branchId;

    public MapCoord coord;

    public RoomType roomType;

    public bool ShouldBroadcast => true;

    public NetTransferMode Mode => NetTransferMode.Reliable;

    public LogLevel LogLevel => LogLevel.Info;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteInt(actIndex);
        writer.WriteInt(batchId);
        writer.WriteInt(branchId);
        writer.Write(coord);
        writer.WriteEnum(roomType);
    }

    public void Deserialize(PacketReader reader)
    {
        actIndex = reader.ReadInt();
        batchId = reader.ReadInt();
        branchId = reader.ReadInt();
        coord = reader.Read<MapCoord>();
        roomType = reader.ReadEnum<RoomType>();
    }
}

public struct ForkedRoadBranchRoomCompletedMessage : INetMessage
{
    public int actIndex;

    public int batchId;

    public int branchId;

    public bool ShouldBroadcast => true;

    public NetTransferMode Mode => NetTransferMode.Reliable;

    public LogLevel LogLevel => LogLevel.Info;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteInt(actIndex);
        writer.WriteInt(batchId);
        writer.WriteInt(branchId);
    }

    public void Deserialize(PacketReader reader)
    {
        actIndex = reader.ReadInt();
        batchId = reader.ReadInt();
        branchId = reader.ReadInt();
    }
}

public struct ForkedRoadPlayerEliminatedMessage : INetMessage
{
    public int actIndex;

    public int batchId;

    public ulong playerId;

    public int? branchId;

    public bool ShouldBroadcast => true;

    public NetTransferMode Mode => NetTransferMode.Reliable;

    public LogLevel LogLevel => LogLevel.Info;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteInt(actIndex);
        writer.WriteInt(batchId);
        writer.WriteULong(playerId);
        writer.WriteBool(branchId.HasValue);
        if (branchId.HasValue)
        {
            writer.WriteInt(branchId.Value);
        }
    }

    public void Deserialize(PacketReader reader)
    {
        actIndex = reader.ReadInt();
        batchId = reader.ReadInt();
        playerId = reader.ReadULong();
        if (reader.ReadBool())
        {
            branchId = reader.ReadInt();
        }
        else
        {
            branchId = null;
        }
    }
}

public struct ForkedRoadBranchCombatSnapshotMessage : INetMessage
{
    public int actIndex;

    public int batchId;

    public int branchId;

    public MapCoord coord;

    public ModelId encounterId;

    public int alliedCreatureCount;

    public NetFullCombatState snapshot;

    public bool ShouldBroadcast => true;

    public NetTransferMode Mode => NetTransferMode.Reliable;

    public LogLevel LogLevel => LogLevel.VeryDebug;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteInt(actIndex);
        writer.WriteInt(batchId);
        writer.WriteInt(branchId);
        writer.Write(coord);
        writer.WriteModelEntry(encounterId);
        writer.WriteInt(alliedCreatureCount);
        writer.Write(snapshot);
    }

    public void Deserialize(PacketReader reader)
    {
        actIndex = reader.ReadInt();
        batchId = reader.ReadInt();
        branchId = reader.ReadInt();
        coord = reader.Read<MapCoord>();
        encounterId = reader.ReadModelIdAssumingType<EncounterModel>();
        alliedCreatureCount = reader.ReadInt();
        snapshot = reader.Read<NetFullCombatState>();
    }
}

public struct ForkedRoadPlayerSpectateTargetChangedMessage : INetMessage
{
    public int actIndex;

    public int batchId;

    public int? branchId;

    public bool ShouldBroadcast => true;

    public NetTransferMode Mode => NetTransferMode.Reliable;

    public LogLevel LogLevel => LogLevel.Info;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteInt(actIndex);
        writer.WriteInt(batchId);
        writer.WriteBool(branchId.HasValue);
        if (branchId.HasValue)
        {
            writer.WriteInt(branchId.Value);
        }
    }

    public void Deserialize(PacketReader reader)
    {
        actIndex = reader.ReadInt();
        batchId = reader.ReadInt();
        if (reader.ReadBool())
        {
            branchId = reader.ReadInt();
        }
        else
        {
            branchId = null;
        }
    }
}

public struct ForkedRoadBatchAllCompletedMessage : INetMessage
{
    public int actIndex;

    public int batchId;

    public bool ShouldBroadcast => true;

    public NetTransferMode Mode => NetTransferMode.Reliable;

    public LogLevel LogLevel => LogLevel.Info;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteInt(actIndex);
        writer.WriteInt(batchId);
    }

    public void Deserialize(PacketReader reader)
    {
        actIndex = reader.ReadInt();
        batchId = reader.ReadInt();
    }
}

internal struct ForkedRoadSavedPlayerSnapshot : IPacketSerializable
{
    public ulong playerId;

    public bool hasCurrentBranchId;

    public int currentBranchId;

    public bool hasSelectionCoord;

    public MapCoord selectionCoord;

    public RouteSplitPlayerPhase phase;

    public bool hasSpectatingBranchId;

    public int spectatingBranchId;

    public bool hasMapVoteDestinationCoord;

    public MapCoord mapVoteDestinationCoord;

    public bool isEliminated;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteULong(playerId);
        writer.WriteBool(hasCurrentBranchId);
        if (hasCurrentBranchId)
        {
            writer.WriteInt(currentBranchId);
        }

        writer.WriteBool(hasSelectionCoord);
        if (hasSelectionCoord)
        {
            writer.Write(selectionCoord);
        }

        writer.WriteEnum(phase);
        writer.WriteBool(hasSpectatingBranchId);
        if (hasSpectatingBranchId)
        {
            writer.WriteInt(spectatingBranchId);
        }

        writer.WriteBool(hasMapVoteDestinationCoord);
        if (hasMapVoteDestinationCoord)
        {
            writer.Write(mapVoteDestinationCoord);
        }

        writer.WriteBool(isEliminated);
    }

    public void Deserialize(PacketReader reader)
    {
        playerId = reader.ReadULong();
        hasCurrentBranchId = reader.ReadBool();
        if (hasCurrentBranchId)
        {
            currentBranchId = reader.ReadInt();
        }

        hasSelectionCoord = reader.ReadBool();
        if (hasSelectionCoord)
        {
            selectionCoord = reader.Read<MapCoord>();
        }

        phase = reader.ReadEnum<RouteSplitPlayerPhase>();
        hasSpectatingBranchId = reader.ReadBool();
        if (hasSpectatingBranchId)
        {
            spectatingBranchId = reader.ReadInt();
        }

        hasMapVoteDestinationCoord = reader.ReadBool();
        if (hasMapVoteDestinationCoord)
        {
            mapVoteDestinationCoord = reader.Read<MapCoord>();
        }

        isEliminated = reader.ReadBool();
    }
}

internal struct ForkedRoadSavedBranchSnapshot : IPacketSerializable
{
    public int branchId;

    public MapCoord targetCoord;

    public ulong authorityPlayerId;

    public List<ulong> playerIds;

    public List<ulong> enteredPlayerIds;

    public List<ulong> readyPlayerIds;

    public List<ulong> eliminatedPlayerIds;

    public bool hasRoomType;

    public RoomType roomType;

    public bool hasPointType;

    public MapPointType pointType;

    public bool hasResolvedModelId;

    public string? resolvedModelCategory;

    public string? resolvedModelEntry;

    public RouteSplitBranchPhase phase;

    public bool hasCompletionOrder;

    public int completionOrder;

    public bool suppressCombatRewards;

    public bool deathClearTriggered;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteInt(branchId);
        writer.Write(targetCoord);
        writer.WriteULong(authorityPlayerId);
        writer.WriteInt(playerIds.Count);
        foreach (ulong playerId in playerIds)
        {
            writer.WriteULong(playerId);
        }

        writer.WriteInt(enteredPlayerIds.Count);
        foreach (ulong playerId in enteredPlayerIds)
        {
            writer.WriteULong(playerId);
        }

        writer.WriteInt(readyPlayerIds.Count);
        foreach (ulong playerId in readyPlayerIds)
        {
            writer.WriteULong(playerId);
        }

        writer.WriteInt(eliminatedPlayerIds.Count);
        foreach (ulong playerId in eliminatedPlayerIds)
        {
            writer.WriteULong(playerId);
        }

        writer.WriteBool(hasRoomType);
        if (hasRoomType)
        {
            writer.WriteEnum(roomType);
        }

        writer.WriteBool(hasPointType);
        if (hasPointType)
        {
            writer.WriteEnum(pointType);
        }

        writer.WriteBool(hasResolvedModelId);
        if (hasResolvedModelId)
        {
            writer.WriteString(resolvedModelCategory ?? string.Empty);
            writer.WriteString(resolvedModelEntry ?? string.Empty);
        }

        writer.WriteEnum(phase);
        writer.WriteBool(hasCompletionOrder);
        if (hasCompletionOrder)
        {
            writer.WriteInt(completionOrder);
        }

        writer.WriteBool(suppressCombatRewards);
        writer.WriteBool(deathClearTriggered);
    }

    public void Deserialize(PacketReader reader)
    {
        branchId = reader.ReadInt();
        targetCoord = reader.Read<MapCoord>();
        authorityPlayerId = reader.ReadULong();
        int playerCount = reader.ReadInt();
        playerIds = new List<ulong>(playerCount);
        for (int i = 0; i < playerCount; i++)
        {
            playerIds.Add(reader.ReadULong());
        }

        int enteredCount = reader.ReadInt();
        enteredPlayerIds = new List<ulong>(enteredCount);
        for (int i = 0; i < enteredCount; i++)
        {
            enteredPlayerIds.Add(reader.ReadULong());
        }

        int readyCount = reader.ReadInt();
        readyPlayerIds = new List<ulong>(readyCount);
        for (int i = 0; i < readyCount; i++)
        {
            readyPlayerIds.Add(reader.ReadULong());
        }

        int eliminatedCount = reader.ReadInt();
        eliminatedPlayerIds = new List<ulong>(eliminatedCount);
        for (int i = 0; i < eliminatedCount; i++)
        {
            eliminatedPlayerIds.Add(reader.ReadULong());
        }

        hasRoomType = reader.ReadBool();
        if (hasRoomType)
        {
            roomType = reader.ReadEnum<RoomType>();
        }

        hasPointType = reader.ReadBool();
        if (hasPointType)
        {
            pointType = reader.ReadEnum<MapPointType>();
        }

        hasResolvedModelId = reader.ReadBool();
        if (hasResolvedModelId)
        {
            resolvedModelCategory = reader.ReadString();
            resolvedModelEntry = reader.ReadString();
        }
        else
        {
            resolvedModelCategory = null;
            resolvedModelEntry = null;
        }

        phase = reader.ReadEnum<RouteSplitBranchPhase>();
        hasCompletionOrder = reader.ReadBool();
        if (hasCompletionOrder)
        {
            completionOrder = reader.ReadInt();
        }

        suppressCombatRewards = reader.ReadBool();
        deathClearTriggered = reader.ReadBool();
    }
}

internal struct ForkedRoadSavedRunSnapshot : IPacketSerializable
{
    public int version;

    public int currentActIndex;

    public bool hasSharedCurrentCoord;

    public MapCoord sharedCurrentCoord;

    public RouteSplitRunPhase phase;

    public int nextBatchId;

    public bool requiresAuthoritativeRoomPlans;

    public bool hasActiveBatch;

    public int batchId;

    public int batchActIndex;

    public List<MapCoord> sourceCoords;

    public List<ForkedRoadSavedPlayerSnapshot> players;

    public List<ForkedRoadSavedBranchSnapshot> branches;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteInt(version);
        writer.WriteInt(currentActIndex);
        writer.WriteBool(hasSharedCurrentCoord);
        if (hasSharedCurrentCoord)
        {
            writer.Write(sharedCurrentCoord);
        }

        writer.WriteEnum(phase);
        writer.WriteInt(nextBatchId);
        writer.WriteBool(requiresAuthoritativeRoomPlans);
        writer.WriteBool(hasActiveBatch);
        if (hasActiveBatch)
        {
            writer.WriteInt(batchId);
            writer.WriteInt(batchActIndex);
            writer.WriteList(sourceCoords);
            writer.WriteList(branches);
        }

        writer.WriteList(players);
    }

    public void Deserialize(PacketReader reader)
    {
        version = reader.ReadInt();
        currentActIndex = reader.ReadInt();
        hasSharedCurrentCoord = reader.ReadBool();
        if (hasSharedCurrentCoord)
        {
            sharedCurrentCoord = reader.Read<MapCoord>();
        }

        phase = reader.ReadEnum<RouteSplitRunPhase>();
        nextBatchId = reader.ReadInt();
        requiresAuthoritativeRoomPlans = reader.ReadBool();
        hasActiveBatch = reader.ReadBool();
        if (hasActiveBatch)
        {
            batchId = reader.ReadInt();
            batchActIndex = reader.ReadInt();
            sourceCoords = reader.ReadList<MapCoord>();
            branches = reader.ReadList<ForkedRoadSavedBranchSnapshot>();
        }
        else
        {
            sourceCoords = new List<MapCoord>();
            branches = new List<ForkedRoadSavedBranchSnapshot>();
        }

        players = reader.ReadList<ForkedRoadSavedPlayerSnapshot>();
    }
}

internal struct ForkedRoadSaveRestoreAvailabilityMessage : INetMessage
{
    public bool hasRestoreState;

    public bool ShouldBroadcast => true;

    public NetTransferMode Mode => NetTransferMode.Reliable;

    public LogLevel LogLevel => LogLevel.Info;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteBool(hasRestoreState);
    }

    public void Deserialize(PacketReader reader)
    {
        hasRestoreState = reader.ReadBool();
    }
}

internal struct ForkedRoadSaveRestoreStateMessage : INetMessage
{
    public ForkedRoadSavedRunSnapshot snapshot;

    public bool ShouldBroadcast => true;

    public NetTransferMode Mode => NetTransferMode.Reliable;

    public LogLevel LogLevel => LogLevel.Info;

    public void Serialize(PacketWriter writer)
    {
        writer.Write(snapshot);
    }

    public void Deserialize(PacketReader reader)
    {
        snapshot = reader.Read<ForkedRoadSavedRunSnapshot>();
    }
}
