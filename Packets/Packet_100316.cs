namespace PacketCapture;

public sealed class EntityStatePacket : Packet
{
    public static readonly int[] TYPE = [100316];

    public string UsedBy { get; init; } = string.Empty;
    public uint StateId { get; init; } // 상태 ID (899395665)
    public int Type { get; init; } // 타입 (2)

    /// <summary>
    ///
    /// </summary>
    /// <param name="usedBy"></param>
    /// <param name="stateId"></param>
    /// <param name="type"></param>
    /// <exception cref="ArgumentNullException"></exception>
    public EntityStatePacket(string usedBy, uint stateId, int type)
    {
        UsedBy = usedBy ?? throw new ArgumentNullException(nameof(usedBy));
        StateId = stateId;
        Type = type;
    }

    public static EntityStatePacket Parse(ReadOnlySpan<byte> content)
    {
        if (content.Length < 20)
            throw new ArgumentException("패킷 데이터가 너무 짧습니다.", nameof(content));

        string usedBy = content[..4].To_hex();
        content = content[8..]; // 8바이트 후 이동

        uint stateId = content[..4].from_bytes<uint>("little");
        content = content[4..];

        int type = content[..4].from_bytes<int>("little");

        return new EntityStatePacket(usedBy, stateId, type);
    }
}
