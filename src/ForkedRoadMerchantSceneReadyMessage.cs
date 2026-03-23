using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;

namespace ForkedRoad;

public struct ForkedRoadMerchantSceneReadyMessage : INetMessage
{
    public int actIndex;

    public int branchSequence;

    public bool ShouldBroadcast => true;

    public NetTransferMode Mode => NetTransferMode.Reliable;

    public LogLevel LogLevel => LogLevel.Info;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteInt(actIndex);
        writer.WriteInt(branchSequence);
    }

    public void Deserialize(PacketReader reader)
    {
        actIndex = reader.ReadInt();
        branchSequence = reader.ReadInt();
    }
}
