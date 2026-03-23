using System.Collections.Generic;
using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;

namespace ForkedRoad;

public struct ForkedRoadBranchStartMessage : INetMessage
{
    public int actIndex;

    public MapCoord coord;

    public int branchSequence;

    public int remainingBranches;

    public List<ulong> playerIds;

    public bool ShouldBroadcast => true;

    public NetTransferMode Mode => NetTransferMode.Reliable;

    public LogLevel LogLevel => LogLevel.Info;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteInt(actIndex);
        writer.Write(coord);
        writer.WriteInt(branchSequence);
        writer.WriteInt(remainingBranches);
        writer.WriteInt(playerIds.Count);
        foreach (ulong playerId in playerIds)
        {
            writer.WriteULong(playerId);
        }
    }

    public void Deserialize(PacketReader reader)
    {
        actIndex = reader.ReadInt();
        coord = reader.Read<MapCoord>();
        branchSequence = reader.ReadInt();
        remainingBranches = reader.ReadInt();
        int count = reader.ReadInt();
        playerIds = new List<ulong>(count);
        for (int i = 0; i < count; i++)
        {
            playerIds.Add(reader.ReadULong());
        }
    }
}
