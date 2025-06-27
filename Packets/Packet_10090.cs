namespace PacketCapture;

public sealed class Packet_10090 : Packet
{
    public static readonly int[] TYPE = [10090];

    public string UsedBy { get; init; } = string.Empty;

    public Packet_10090(string usedBy)
    {
        UsedBy = usedBy ?? throw new ArgumentNullException(nameof(usedBy));
    }

    public override string ToString() => $"{UsedBy}";

    public static Packet_10090 Parse(ReadOnlySpan<byte> content)
    {
        if (content.Length < 8)
            throw new ArgumentException("패킷 데이터가 너무 짧습니다.", nameof(content));

        string usedBy = content[..4].To_hex();
        return new Packet_10090(usedBy);
    }
}
