using PacketCapture;

public sealed class Packet_10739 : Packet
{
    public static readonly int[] TYPE = [10739];

    public string UsedBy { get; init; } = string.Empty;
    public string SkillName { get; init; } = string.Empty;
    public int AdditionalValue { get; init; }

    public Packet_10739(string usedBy, string skillName, int additionalValue)
    {
        UsedBy = usedBy;
        SkillName = skillName;
        AdditionalValue = additionalValue;
    }

    public static Packet_10739 Parse(ReadOnlySpan<byte> content)
    {
        if (content.Length < 12)
            return new Packet_10739(string.Empty, string.Empty, 0);

        // UsedBy 추출
        string usedBy = content[..4].To_hex();
        content = content[8..];

        // 스킬 이름 추출
        (string skillName, int nameLength) = ExtractName(content);
        content = content[nameLength..]; // 스킬 이름 이후 데이터 건너뛰기

        // 추가 값 추출
        int additionalValue = content.Length >= 4 ? content[..4].from_bytes<int>("little") : 0;

        return new Packet_10739(usedBy, skillName, additionalValue);
    }

    public override string ToString() =>
        $"{UsedBy} | 스킬 이름: {SkillName} | 추가 값: {AdditionalValue}";
}
