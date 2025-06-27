namespace PacketCapture;

public sealed class Packet_100433 : Packet
{
    public static readonly int[] TYPE = [100433];

    public string UsedBy { get; init; } = string.Empty;
    public int Value { get; init; } = 0;

    public Packet_100433(string usedBy, int value)
    {
        UsedBy = usedBy ?? throw new ArgumentNullException(nameof(usedBy));
        Value = value;
    }

    public override string ToString() => $"{UsedBy} : {Value}";

    public static Packet_100433 Parse(ReadOnlySpan<byte> content)
    {
        if (content.Length < 20) // 최소 패킷 크기 확인
            throw new ArgumentException("패킷 데이터가 너무 짧습니다.", nameof(content));

        // 사용자 ID 추출 (4바이트, 4바이트 패딩 후)
        string usedBy = content[..4].To_hex();
        content = content[8..];

        int value = content[..4].from_bytes<int>("little");

        return new Packet_100433(usedBy, value);
    }
}
