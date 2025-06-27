namespace PacketCapture;

public sealed class Packet_100835 : Packet
{
    public static readonly int[] TYPE = [100835];
    public string UsedBy { get; init; } = string.Empty;

    public static Packet_100835 Parse(ReadOnlySpan<byte> content)
    {
        if (content.Length < 8)
            throw new ArgumentException("패킷 데이터가 너무 짧습니다.", nameof(content));

        string usedBy = content[..4].To_hex();
        content = content[8..]; // 8바이트 후 이동
        byte[] unknown = content[..8].ToArray();
        content = content[8..];
        byte[] unknown2 = content[..8].ToArray();
        content = content[8..];
        float value = content[..4].from_bytes<float>("little");
        content = content[4..];
        var state = content[..4].To_hex();
        content = content[4..];
        var unknown3 = content[..4].To_hex();
        content = content[4..];
        return new Packet_100835 { UsedBy = usedBy };
    }
}
