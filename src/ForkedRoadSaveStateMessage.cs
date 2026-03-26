using MegaCrit.Sts2.Core.Logging;
using MegaCrit.Sts2.Core.Multiplayer.Serialization;
using MegaCrit.Sts2.Core.Multiplayer.Transport;

namespace ForkedRoad;

public struct ForkedRoadSaveStateMessage : INetMessage
{
    public int actIndex;

    public string stateJson;

    public bool ShouldBroadcast => true;

    public NetTransferMode Mode => NetTransferMode.Reliable;

    public LogLevel LogLevel => LogLevel.Info;

    public void Serialize(PacketWriter writer)
    {
        writer.WriteInt(actIndex);
        writer.WriteString(stateJson ?? string.Empty);
    }

    public void Deserialize(PacketReader reader)
    {
        actIndex = reader.ReadInt();
        stateJson = reader.ReadString();
    }
}
