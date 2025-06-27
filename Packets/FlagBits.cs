namespace PacketCapture;

/// <summary>
/// 데미지 플래그 비트들을 나타내는 불변 레코드 (.NET 8 record 사용)
/// </summary>
public record struct FlagBits
{
    /// <summary>크리티컬 히트 여부</summary>
    public bool Crit { get; init; }

    /// <summary>무방비 상태 여부</summary>
    public bool Unguarded { get; init; }

    /// <summary>방어구 파괴 여부</summary>
    public bool Broken { get; init; }

    /// <summary>첫 번째 히트 여부</summary>
    public bool FirstHit { get; init; }

    /// <summary>멀티 히트 여부</summary>
    public bool MultiHit { get; init; }

    /// <summary>빠른 히트 여부</summary>
    public bool FastHit { get; init; }

    /// <summary>강력한 히트 여부</summary>
    public bool PowerHit { get; init; }

    /// <summary>추가 히트 여부</summary>
    public bool AddHit { get; init; }

    /// <summary>기본 공격 여부</summary>
    public bool DefaultAttack { get; init; }

    /// <summary>지속 데미지 여부</summary>
    public bool Dot { get; init; }

    /// <summary>얼음 속성 여부</summary>
    public bool Ice { get; init; }

    /// <summary>불 속성 여부</summary>
    public bool Fire { get; init; }

    /// <summary>전기 속성 여부</summary>
    public bool Electric { get; init; }

    /// <summary>출혈 상태 여부</summary>
    public bool Bleed { get; init; }

    /// <summary>독 상태 여부</summary>
    public bool Poison { get; init; }

    /// <summary>정신 속성 여부</summary>
    public bool Mind { get; init; }

    /// <summary>신성 속성 여부</summary>
    public bool Holy { get; init; }

    /// <summary>암흑 속성 여부</summary>
    public bool Dark { get; init; }

    /// <summary>
    /// 플래그를 기반으로 스킬 이름을 생성합니다.
    /// </summary>
    /// <returns>생성된 스킬 이름</returns>
    public readonly string GenerateSkillName()
    {
        const string prefix = "DOT";
        const string unknown = "UNKNOWN";

        if (!Dot)
            return unknown;

        // 속성 플래그들을 컬렉션 표현식으로 수집
        List<string> activeFlags = [];

        if (Ice)
            activeFlags.Add("ICE");
        if (Fire)
            activeFlags.Add("FIRE");
        if (Electric)
            activeFlags.Add("ELECTRIC");
        if (Bleed)
            activeFlags.Add("BLEED");
        if (Poison)
            activeFlags.Add("POISON");
        if (Mind)
            activeFlags.Add("MIND");
        if (Holy)
            activeFlags.Add("HOLY");
        if (Dark)
            activeFlags.Add("DARK");

        return activeFlags.Count > 0 ? $"{prefix}_{string.Join("_", activeFlags)}" : prefix;
    }

    /// <summary>
    /// 플래그 바이트를 FlagBits 구조체로 파싱합니다.
    /// </summary>
    /// <param name="flagBytes">플래그 바이트 배열</param>
    /// <returns>파싱된 플래그</returns>
    public static FlagBits ParseFlags(ReadOnlySpan<byte> flagBytes)
    {
        if (flagBytes.Length < 6)
            throw new ArgumentException("플래그 바이트 배열이 너무 짧습니다.", nameof(flagBytes));

        return new FlagBits
        {
            Crit = (flagBytes[0] & 0x01) != 0,
            Unguarded = (flagBytes[0] & 0x04) != 0,
            Broken = (flagBytes[0] & 0x08) != 0,
            FirstHit = (flagBytes[0] & 0x40) != 0,
            DefaultAttack = (flagBytes[0] & 0x80) != 0,

            MultiHit = (flagBytes[1] & 0x01) != 0,
            PowerHit = (flagBytes[1] & 0x02) != 0,
            FastHit = (flagBytes[1] & 0x04) != 0,
            Dot = (flagBytes[1] & 0x08) != 0,

            AddHit = (flagBytes[3] & 0x08) != 0,
            Bleed = (flagBytes[3] & 0x10) != 0,
            Dark = (flagBytes[3] & 0x20) != 0,
            Fire = (flagBytes[3] & 0x40) != 0,
            Holy = (flagBytes[3] & 0x80) != 0,

            Ice = (flagBytes[4] & 0x01) != 0,
            Electric = (flagBytes[4] & 0x02) != 0,
            Poison = (flagBytes[4] & 0x04) != 0,
            Mind = (flagBytes[4] & 0x08) != 0,
        };
    }

    public override readonly string ToString() =>
        $"crit: {Crit}, unguarded: {Unguarded}, broken: {Broken}, first_hit: {FirstHit}, "
        + $"multi_hit: {MultiHit}, fast_hit: {FastHit}, power_hit: {PowerHit}, add_hit: {AddHit}, "
        + $"default_attack: {DefaultAttack}, dot: {Dot}, ice: {Ice}, fire: {Fire}, "
        + $"electric: {Electric}, bleed: {Bleed}, poison: {Poison}, mind: {Mind}, "
        + $"holy: {Holy}, dark: {Dark}";

    public object[] ToValues()
    {
        return
        [
            Crit ? 1 : 0,
            AddHit ? 1 : 0,
            Unguarded ? 1 : 0,
            Broken ? 1 : 0,
            FirstHit ? 1 : 0,
            DefaultAttack ? 1 : 0,
            MultiHit ? 1 : 0,
            PowerHit ? 1 : 0,
            FastHit ? 1 : 0,
            Dot ? 1 : 0,
            Ice ? 1 : 0,
            Fire ? 1 : 0,
            Electric ? 1 : 0,
            Holy ? 1 : 0,
            Dark ? 1 : 0,
            Bleed ? 1 : 0,
            Poison ? 1 : 0,
            Mind ? 1 : 0,
        ];
    }
}
