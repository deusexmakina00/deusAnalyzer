namespace PacketCapture;

/// <summary>
/// 스킬 패킷 데이터를 처리하는 클래스
/// 최신 C# 기능과 Span<T>를 활용한 고성능 구현
/// </summary>
public sealed class SkillInfoPacket : Packet
{
    /// <summary>지원되는 스킬 패킷 타입들</summary>
    public static readonly int[] TYPE = [10446];

    /// <summary>스킬 사용자 ID</summary>
    public string UsedBy { get; init; } = string.Empty;

    /// <summary>스킬 대상 ID</summary>
    public string Target { get; init; } = string.Empty;
    public string Owner { get; init; } = string.Empty;

    /// <summary>스킬 이름</summary>
    public string SkillName { get; init; } = string.Empty;

    /// <summary>
    /// 스킬 패킷을 생성합니다.
    /// </summary>
    /// <param name="usedBy">사용자 ID</param>
    /// <param name="target">대상 ID</param>
    /// <param name="action">액션</param>
    /// <param name="skillName">스킬 이름</param>
    public SkillInfoPacket(string usedBy, string target, string owner, string skillName)
    {
        UsedBy = usedBy ?? throw new ArgumentNullException(nameof(usedBy));
        Target = target ?? throw new ArgumentNullException(nameof(target));
        Owner = owner ?? throw new ArgumentNullException(nameof(owner));
        SkillName = skillName ?? throw new ArgumentNullException(nameof(skillName));
    }

    public override string ToString() =>
        $"{UsedBy} -> {Target} | Owner: {Owner} | Skill Name: {SkillName}";

    /// <summary>
    /// ReadOnlySpan을 사용한 고성능 스킬 패킷 파싱
    /// </summary>
    /// <param name="content">패킷 데이터</param>
    /// <returns>파싱된 스킬 패킷</returns>
    public static SkillInfoPacket Parse(ReadOnlySpan<byte> content)
    {
        if (content.Length < 24) // 최소 패킷 크기 확인
            throw new ArgumentException("패킷 데이터가 너무 짧습니다.", nameof(content));

        // 사용자 ID 추출 (4바이트, 4바이트 패딩 후)
        string usedBy = content[..4].To_hex();
        content = content[8..];
        // 대상 ID 추출 (4바이트, 4바이트 패딩 후)
        string target = content[..4].To_hex();
        content = content[8..];
        // 소유자 ID 추출 (4바이트, 4바이트 패딩 후)
        string owner = content[..4].To_hex();
        content = content[8..];

        // 스킬 이름 추출
        (string skillName, int nameLength) = ExtractName(content);
        content = content[nameLength..]; // 스킬 이름 이후 데이터 건너뛰기

        float x = content[..4].from_bytes<float>("little");
        content = content[4..];

        content = content[4..]; // 패딩 건너뛰기

        float y = content[..4].from_bytes<float>("little");
        content = content[4..];

        content = content[4..]; // 패딩 건너뛰기

        int additionalValue = content[..4].from_bytes<int>("little");

        // 4바이트 패딩 건너뛰기
        return new SkillInfoPacket(usedBy, target, owner, skillName);
    }
}
