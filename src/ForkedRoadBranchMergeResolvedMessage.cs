using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;

namespace ForkedRoad;

public struct ForkedRoadBranchMergeResolvedMessage : INetMessage
{
    public int actIndex;

    public int branchSequence;

    public MapCoord coord;

    public bool ShouldBroadcast => true;

    public NetTransferMode Mode => NetTransferMode.Reliable;

    public LogLevel LogLevel => LogLevel.Info;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteInt(actIndex);
        writer.WriteInt(branchSequence);
        writer.Write(coord);
    }

    public void Deserialize(PacketReader reader)
    {
        actIndex = reader.ReadInt();
        branchSequence = reader.ReadInt();
        coord = reader.Read<MapCoord>();
    }
}
