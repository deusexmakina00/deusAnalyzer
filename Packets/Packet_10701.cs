using System.Buffers.Binary;
using System.Collections.Concurrent;

namespace PacketCapture;

/// <summary>
/// 데미지 패킷 데이터를 처리하는 봉인된 클래스
/// 최신 C# 기능과 Span<T>를 활용한 고성능 구현
/// </summary>
public sealed partial class SkillDamagePacket : Packet
{
    /// <summary>지원되는 데미지 패킷 타입들</summary>
    public static readonly int[] TYPE = [10701];

    /// <summary>데미지 사용자 ID</summary>
    public string UsedBy { get; init; } = string.Empty;

    /// <summary>데미지 대상 ID</summary>
    public string Target { get; init; } = string.Empty;

    /// <summary>데미지 양</summary>
    public uint Damage { get; init; }

    /// <summary>스킬 ID</summary>
    public int SkillId { get; init; }

    /// <summary>스킬 이름</summary>
    public string SkillName { get; set; } = string.Empty;

    /// <summary>데미지 플래그들</summary>
    public FlagBits Flags { get; init; }

    /// <summary>원시 플래그 바이트 배열</summary>
    public byte[] FlagBytes { get; init; } = [];

    /// <summary>
    /// 데미지 패킷을 생성합니다.
    /// </summary>
    /// <param name="usedBy">사용자 ID</param>
    /// <param name="target">대상 ID</param>
    /// <param name="damage">데미지 양</param>
    /// <param name="skillId">스킬 ID</param>
    /// <param name="flags">데미지 플래그들</param>
    /// <param name="flagBytes">원시 플래그 바이트</param>
    /// <param name="skillName">스킬 이름 (선택사항)</param>
    public SkillDamagePacket(
        string usedBy,
        string target,
        uint damage,
        int skillId,
        FlagBits flags,
        byte[] flagBytes,
        string skillName = ""
    )
    {
        UsedBy = usedBy ?? string.Empty;
        Target = target ?? string.Empty;
        Damage = damage;
        SkillId = skillId;
        SkillName = skillName ?? string.Empty;
        Flags = flags;
        FlagBytes = flagBytes ?? [];
    }

    public override string ToString() =>
        $"{UsedBy} -> {Target} | Damage: {Damage} | Skill ID: {SkillId} | Skill Name: {SkillName} | Flags: {Flags}";

    /// <summary>
    /// 로그 형식으로 데이터를 변환합니다.
    /// </summary>
    /// <returns>로그 문자열</returns>
    public string ToLog()
    {
        string effectiveSkillName = !string.IsNullOrEmpty(SkillName)
            ? SkillName
            : Flags.GenerateSkillName();

        // 컬렉션 표현식으로 값들을 배열로 생성
        object[] values =
        [
            DateTimeOffset.Now.ToUnixTimeMilliseconds(),
            UsedBy,
            Target,
            effectiveSkillName,
            Damage,
            Flags.Crit ? 1 : 0,
            Flags.AddHit ? 1 : 0,
            Flags.Unguarded ? 1 : 0,
            Flags.Broken ? 1 : 0,
            Flags.FirstHit ? 1 : 0,
            Flags.DefaultAttack ? 1 : 0,
            Flags.MultiHit ? 1 : 0,
            Flags.PowerHit ? 1 : 0,
            Flags.FastHit ? 1 : 0,
            Flags.Dot ? 1 : 0,
            Flags.Ice ? 1 : 0,
            Flags.Fire ? 1 : 0,
            Flags.Electric ? 1 : 0,
            Flags.Holy ? 1 : 0,
            Flags.Dark ? 1 : 0,
            Flags.Bleed ? 1 : 0,
            Flags.Poison ? 1 : 0,
            Flags.Mind ? 1 : 0,
            SkillId,
        ];

        return string.Join("|", values);
    }

    /// <summary>
    /// DOT(Damage Over Time) 데미지인지 확인합니다.
    /// </summary>
    /// <returns>DOT 여부</returns>
    public bool IsDot() => string.IsNullOrEmpty(SkillName) && Flags.Dot;

    /// <summary>
    /// ReadOnlySpan을 사용한 고성능 데미지 패킷 파싱
    /// </summary>
    /// <param name="content">패킷 데이터</param>
    /// <param name="type">패킷 타입 (10701 또는 100088)</param>
    /// <returns>파싱된 데미지 패킷</returns>
    public static SkillDamagePacket Parse(ReadOnlySpan<byte> content, int type = 10701)
    {
        if (content.Length < 34) // 최소 패킷 크기 확인
            throw new ArgumentException("패킷 데이터가 너무 짧습니다.", nameof(content));

        // 사용자 ID 추출 (4바이트, 4바이트 패딩 후)
        string usedBy = content[..4].To_hex();
        content = content[8..]; // 4바이트 데이터 + 4바이트 패딩

        // 대상 ID 추출 (4바이트, 4바이트 패딩 후)
        string target = content[..4].To_hex();
        content = content[8..]; // 4바이트 데이터 + 4바이트 패딩
        // 데미지 (4바이트, Little Endian)
        uint damage = content[..4].from_bytes<uint>("little");
        content = content[4..];

        // Unknown 12바이트 건너뛰기
        content = content[12..];

        // Flag 6바이트
        var flagBytes = content[..6].ToArray();
        content = content[6..];

        // Skill ID 4바이트 (Little Endian)
        int skillId = content[..4].from_bytes<int>("little");

        // 플래그 파싱
        var flags = FlagBits.ParseFlags(flagBytes);

        return new SkillDamagePacket(usedBy, target, damage, skillId, flags, flagBytes);
    }
}
