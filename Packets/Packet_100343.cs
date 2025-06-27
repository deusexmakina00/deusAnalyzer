namespace PacketCapture;

public sealed class Packet_100343 : Packet
{
    public static readonly int[] TYPE = [100343];

    public string UsedBy { get; init; } = string.Empty;
    public int Value { get; init; }

    public Packet_100343(string usedBy, int mana)
    {
        UsedBy = usedBy ?? throw new ArgumentNullException(nameof(usedBy));
        Value = mana;
    }

    public override string ToString() => $"Usage {UsedBy} : Mana : {Value}";

    public static Packet_100343 Parse(ReadOnlySpan<byte> content)
    {
        if (content.Length < 16)
            throw new ArgumentException("패킷 데이터가 너무 짧습니다.", nameof(content));

        string usedBy = content[..4].To_hex();
        content = content[8..]; // 8바이트 후 이동

        int value = content[..4].from_bytes<int>("little");

        return new Packet_100343(usedBy, value);
    }
}
