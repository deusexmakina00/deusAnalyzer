namespace PacketCapture;

public sealed class EntityPositionPacket : Packet
{
    public static readonly int[] TYPE = [100327];

    public string UsedBy { get; init; } = string.Empty;
    public short Type { get; init; }
    public float X { get; init; }
    public float Y { get; init; }
    public float Z { get; init; }
    public int Flag { get; init; }

    /// <summary>
    /// 엔티티 위치 패킷을 생성합니다.
    /// </summary>
    /// <param name="usedBy"></param>
    /// <param name="type"></param>
    /// <param name="x"></param>
    /// <param name="y"></param>
    /// <param name="z"></param>
    /// <param name="flag"></param>
    /// <exception cref="ArgumentNullException"></exception>
    public EntityPositionPacket(string usedBy, short type, float x, float y, float z, int flag)
    {
        UsedBy = usedBy ?? throw new ArgumentNullException(nameof(usedBy));
        Type = type;
        X = x;
        Y = y;
        Z = z;
        Flag = flag;
    }

    public static EntityPositionPacket Parse(ReadOnlySpan<byte> content)
    {
        if (content.Length < 26)
            throw new ArgumentException("패킷 데이터가 너무 짧습니다.", nameof(content));

        string usedBy = content[..4].To_hex();
        content = content[8..]; // 8바이트 후 이동

        short type = content[..2].from_bytes<short>("little");
        content = content[2..];

        float x = content[..4].from_bytes<float>("little");
        content = content[4..];
        float y = content[..4].from_bytes<float>("little");
        content = content[4..];
        float z = content[..4].from_bytes<float>("little");
        content = content[4..];

        int flag = content[..4].from_bytes<int>("little");

        return new EntityPositionPacket(usedBy, type, x, y, z, flag);
    }
}
