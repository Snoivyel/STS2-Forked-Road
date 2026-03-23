using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;

namespace ForkedRoad;

public struct ForkedRoadBranchCompleteMessage : INetMessage
{
    public int actIndex;

    public bool ShouldBroadcast => true;

    public NetTransferMode Mode => NetTransferMode.Reliable;

    public LogLevel LogLevel => LogLevel.Info;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteInt(actIndex);
    }

    public void Deserialize(PacketReader reader)
    {
        actIndex = reader.ReadInt();
    }
}
