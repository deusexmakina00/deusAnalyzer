using PacketCapture;

public class DamageModel
{
    public string UsedBy { get; set; } = string.Empty;
    public string Target { get; set; } = string.Empty;
    public int Damage { get; set; } = 0;
    public string SkillName { get; set; } = string.Empty;
    public FlagBits Flags { get; set; } = new FlagBits();
    public byte[] FlagBytes { get; set; } = Array.Empty<byte>();

    // 사용자의 데미지 여부
    public int SelfTarget { get; set; } = 0;

    public override string ToString() =>
        $"{UsedBy} -> {Target} | Damage: {Damage} | Skill Name: {SkillName} | Flags: {Flags}";

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
            SelfTarget,
        ];

        return string.Join("|", values);
    }
}
