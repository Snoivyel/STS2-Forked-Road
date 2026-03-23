using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Map;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;

namespace ForkedRoad;

public struct ForkedRoadBranchMergeMessage : INetMessage
{
    public int actIndex;

    public ulong playerId;

    public MapCoord targetCoord;

    public bool ShouldBroadcast => true;

    public NetTransferMode Mode => NetTransferMode.Reliable;

    public LogLevel LogLevel => LogLevel.Info;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteInt(actIndex);
        writer.WriteULong(playerId);
        writer.Write(targetCoord);
    }

    public void Deserialize(PacketReader reader)
    {
        actIndex = reader.ReadInt();
        playerId = reader.ReadULong();
        targetCoord = reader.Read<MapCoord>();
    }
}
