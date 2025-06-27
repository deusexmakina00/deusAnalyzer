using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Text;

namespace PacketCapture;

/// <summary>
/// 패킷 데이터를 담는 불변 레코드 (.NET 8 record 사용으로 성능 향상)
/// </summary>
/// <param name="Data">패킷 데이터</param>
/// <param name="RelSeq">상대 시퀀스 번호</param>
/// <param name="At">수신 시각</param>
public record PacketPayload(byte[] Data, uint RelSeq, DateTime At);

public class Packet
{
    /// <summary>
    /// 패킷에서 이름을 추출합니다.
    /// </summary>
    protected static (string skillName, int byteLength) ExtractName(ReadOnlySpan<byte> content)
    {
        if (content.Length < 4)
            return (string.Empty, 0);

        // 이름 길이 읽기 (Little Endian)
        int nameLength = content[..4].from_bytes<int>("little");

        if (nameLength <= 0 || nameLength < 4 + content.Length)
            return (string.Empty, 4);

        // 실제 바이트 길이 = 길이필드(4) + 이름바이트(nameLength)
        int totalByteLength = 4 + nameLength;
        // 이름 바이트 추출
        var nameBytes = content.Slice(4, nameLength);

        // null 바이트 필터링 및 UTF-8 디코딩
        Span<byte> asciiBytes = stackalloc byte[nameLength];
        int asciiLength = 0;
        for (int i = 0; i < nameBytes.Length; i++)
        {
            byte b = nameBytes[i];
            if (b != 0 && b >= 0x20 && b <= 0x7E)
                asciiBytes[asciiLength++] = b;
        }
        string cleanName =
            asciiLength > 0
                ? Encoding.UTF8.GetString(asciiBytes.Slice(0, asciiLength))
                : string.Empty;
        return (cleanName, totalByteLength);
    }
}

public sealed class EntityStatePacket : Packet
{
    public static readonly int[] TYPE = [100316];

    public string UsedBy { get; init; } = string.Empty;
    public uint StateId { get; init; } // 상태 ID (899395665)
    public int Type { get; init; } // 타입 (2)

    /// <summary>
    ///
    /// </summary>
    /// <param name="usedBy"></param>
    /// <param name="stateId"></param>
    /// <param name="type"></param>
    /// <exception cref="ArgumentNullException"></exception>
    public EntityStatePacket(string usedBy, uint stateId, int type)
    {
        UsedBy = usedBy ?? throw new ArgumentNullException(nameof(usedBy));
        StateId = stateId;
        Type = type;
    }

    public static EntityStatePacket Parse(ReadOnlySpan<byte> content)
    {
        if (content.Length < 20)
            throw new ArgumentException("패킷 데이터가 너무 짧습니다.", nameof(content));

        string usedBy = content[..4].To_hex();
        content = content[8..]; // 8바이트 후 이동

        uint stateId = content[..4].from_bytes<uint>("little");
        content = content[4..];

        int type = content[..4].from_bytes<int>("little");

        return new EntityStatePacket(usedBy, stateId, type);
    }
}

public sealed class Packet_100324 : Packet
{
    /// <summary>지원되는 패킷 타입</summary>
    public static readonly int[] TYPE = [100324];

    /// <summary>사용자 ID</summary>
    public string UsedBy { get; init; } = string.Empty;

    /// <summary>대상 ID</summary>
    public string Target { get; init; } = string.Empty;

    /// <summary>액션 코드</summary>
    public string Action { get; init; } = string.Empty;

    /// <summary>부동소수점 값 (시간/거리/각도 등)</summary>
    public float Value { get; init; }

    /// <summary>
    /// Packet_100324를 생성합니다.
    /// </summary>
    /// <param name="usedBy">사용자 ID</param>
    /// <param name="target">대상 ID</param>
    /// <param name="action">액션 코드</param>
    /// <param name="value">부동소수점 값</param>
    public Packet_100324(string usedBy, string target, string action, float value)
    {
        UsedBy = usedBy ?? throw new ArgumentNullException(nameof(usedBy));
        Target = target ?? throw new ArgumentNullException(nameof(target));
        Action = action ?? throw new ArgumentNullException(nameof(action));
        Value = value;
    }

    public override string ToString() => $"{UsedBy} {Target} : {Action} ({Value:F3})";

    /// <summary>
    /// ReadOnlySpan을 사용한 고성능 패킷 파싱
    /// </summary>
    /// <param name="content">패킷 데이터 (29바이트)</param>
    /// <returns>파싱된 패킷</returns>
    public static Packet_100324 Parse(ReadOnlySpan<byte> content)
    {
        if (content.Length < 29) // 정확한 패킷 크기 확인
            throw new ArgumentException("패킷 데이터가 너무 짧습니다.", nameof(content));

        // 사용자 ID 추출 (4바이트, 4바이트 패딩 후)
        string usedBy = content[..4].To_hex();
        content = content[8..];

        // 대상 ID 추출 (4바이트)
        string target = content[..4].To_hex();
        content = content[4..];

        // 액션 코드 추출 (4바이트)
        string action = content[..4].To_hex();
        content = content[4..];

        // 4바이트 패딩 건너뛰기
        content = content[4..];

        // 부동소수점 값 추출 (4바이트, Little Endian)
        float value = content[..4].from_bytes<float>("little");

        return new Packet_100324(usedBy, target, action, value);
    }
}

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

public sealed class EntityManaPacket : Packet
{
    public static readonly int[] TYPE = [100343];

    public string UsedBy { get; init; } = string.Empty;
    public int Mana { get; init; }

    public EntityManaPacket(string usedBy, int mana)
    {
        UsedBy = usedBy ?? throw new ArgumentNullException(nameof(usedBy));
        Mana = mana;
    }

    public override string ToString() => $"Usage {UsedBy} : Mana : {Mana}";

    public static EntityManaPacket Parse(ReadOnlySpan<byte> content)
    {
        if (content.Length < 16)
            throw new ArgumentException("패킷 데이터가 너무 짧습니다.", nameof(content));

        string usedBy = content[..4].To_hex();
        content = content[8..]; // 8바이트 후 이동

        int mana = content[..4].from_bytes<int>("little");

        return new EntityManaPacket(usedBy, mana);
    }
}

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

public sealed class SkillActionPacket : Packet
{
    public static readonly int[] TYPE = [100041];

    /// <summary>사용자 ID</summary>
    public string UsedBy { get; init; } = string.Empty;

    /// <summary>대상 ID</summary>
    public string Target { get; init; } = string.Empty;

    /// <summary>액션 코드</summary>
    public string Action { get; init; } = string.Empty;

    /// <summary>시전 시간 (초 단위)</summary>
    public float CastTime { get; init; }

    public string NextTarget { get; init; } = string.Empty;

    /// <summary>액션 이름</summary>
    public string ActionName { get; init; } = string.Empty;

    /// <summary>
    /// 액션 패킷을 생성합니다.
    /// </summary>
    /// <param name="usedBy">사용자 ID</param>
    /// <param name="target">대상 ID</param>
    /// <param name="nextTarget">다음 대상 ID</param>
    /// <param name="action">액션 코드</param>
    /// <param name="castTime">시전 시간 (초 단위)</param>
    /// <param name="actionName">액션 이름</param>
    /// <exception cref="ArgumentNullException"></exception>
    public SkillActionPacket(
        string usedBy,
        string target,
        string nextTarget,
        string action,
        float castTime,
        string actionName
    )
    {
        UsedBy = usedBy ?? throw new ArgumentNullException(nameof(usedBy));
        Target = target ?? throw new ArgumentNullException(nameof(target));
        NextTarget = nextTarget ?? throw new ArgumentNullException(nameof(nextTarget));
        Action = action ?? throw new ArgumentNullException(nameof(action));
        CastTime = castTime;
        ActionName = actionName ?? throw new ArgumentNullException(nameof(actionName));
    }

    public override string ToString() => $"{UsedBy} {Target} : {ActionName}";

    public static SkillActionPacket Parse(ReadOnlySpan<byte> content)
    {
        if (content.Length < 24) // 최소 패킷 크기 확인
            throw new ArgumentException("패킷 데이터가 너무 짧습니다.", nameof(content));

        // 사용자 ID 추출 (4바이트, 4바이트 패딩 후)
        string usedBy = content[..4].To_hex();
        content = content[8..];

        (string actionName, int actualLength) = ExtractName(content);
        content = content[actualLength..]; // 스킬 이름 이후 데이터 건너뛰기
        string action = content[..4].To_hex();
        content = content[8..]; // 4 + 4바이트 패딩

        string unknown1 = content[..4].To_hex();
        content = content[4..]; // 4바이트 건너뛰기

        // 시전 시간 추출 (4바이트, Little Endian)
        float castTime = content[..4].from_bytes<float>("little");
        content = content[4..]; // 4바이트 건너뛰기
        // 다음 대상 ID 추출 (4바이트, Little Endian)
        string nextTarget = content[..4].To_hex();
        content = content[4..]; // 4바이트 건너뛰기

        // 대상 ID 추출 (4바이트, Little Endian)
        string target = content[^4..].To_hex();

        return new SkillActionPacket(usedBy, target, nextTarget, action, castTime, actionName);
    }
}

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
    public string Action { get; init; } = string.Empty;

    /// <summary>스킬 이름</summary>
    public string SkillName { get; init; } = string.Empty;

    /// <summary>
    /// 스킬 패킷을 생성합니다.
    /// </summary>
    /// <param name="usedBy">사용자 ID</param>
    /// <param name="target">대상 ID</param>
    /// <param name="action">액션</param>
    /// <param name="skillName">스킬 이름</param>
    public SkillInfoPacket(string usedBy, string target, string action, string skillName)
    {
        UsedBy = usedBy ?? throw new ArgumentNullException(nameof(usedBy));
        Target = target ?? throw new ArgumentNullException(nameof(target));
        Action = action ?? throw new ArgumentNullException(nameof(action));
        SkillName = skillName ?? throw new ArgumentNullException(nameof(skillName));
    }

    public override string ToString() => $"{UsedBy} {Target} : {SkillName}";

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
        // 액션 코드 추출 (4바이트, Little Endian)
        string action = content[..4].To_hex();
        content = content[8..];

        // 스킬 이름 추출
        (string skillName, _) = ExtractName(content);

        return new SkillInfoPacket(usedBy, target, action, skillName);
    }
}

/// <summary>
/// 데미지 패킷 데이터를 처리하는 봉인된 클래스
/// 최신 C# 기능과 Span<T>를 활용한 고성능 구현
/// </summary>
public sealed class SkillDamagePacket : Packet
{
    /// <summary>지원되는 데미지 패킷 타입들</summary>
    public static readonly int[] TYPE = [10701, 100088];

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

        public override readonly string ToString() =>
            $"crit: {Crit}, unguarded: {Unguarded}, broken: {Broken}, first_hit: {FirstHit}, "
            + $"multi_hit: {MultiHit}, fast_hit: {FastHit}, power_hit: {PowerHit}, add_hit: {AddHit}, "
            + $"default_attack: {DefaultAttack}, dot: {Dot}, ice: {Ice}, fire: {Fire}, "
            + $"electric: {Electric}, bleed: {Bleed}, poison: {Poison}, mind: {Mind}, "
            + $"holy: {Holy}, dark: {Dark}";
    }

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
        $"{UsedBy} -> {Target} | Damage: {Damage} | Skill ID: {SkillId} | Flags: {Flags}";

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
        var flags = ParseFlags(flagBytes);

        return new SkillDamagePacket(usedBy, target, damage, skillId, flags, flagBytes);
    }

    /// <summary>
    /// 플래그 바이트를 FlagBits 구조체로 파싱합니다.
    /// </summary>
    /// <param name="flagBytes">플래그 바이트 배열</param>
    /// <returns>파싱된 플래그</returns>
    private static FlagBits ParseFlags(ReadOnlySpan<byte> flagBytes)
    {
        if (flagBytes.Length < 6)
            throw new ArgumentException("플래그 바이트 배열이 너무 짧습니다.", nameof(flagBytes));

        return new FlagBits
        {
            Crit = (flagBytes[0] & 0x01) != 0,
            Unguarded = (flagBytes[0] & 0x04) != 0,
            Broken = (flagBytes[0] & 0x08) != 0,
            FirstHit = (flagBytes[0] & 0x64) != 0,
            DefaultAttack = (flagBytes[0] & 0x128) != 0,

            MultiHit = (flagBytes[1] & 0x01) != 0,
            PowerHit = (flagBytes[1] & 0x02) != 0,
            FastHit = (flagBytes[1] & 0x04) != 0,
            Dot = (flagBytes[1] & 0x08) != 0,

            AddHit = (flagBytes[3] & 0x08) != 0,
            Bleed = (flagBytes[3] & 0x16) != 0,
            Dark = (flagBytes[3] & 0x32) != 0,
            Fire = (flagBytes[3] & 0x64) != 0,
            Holy = (flagBytes[3] & 0x128) != 0,

            Ice = (flagBytes[4] & 0x01) != 0,
            Electric = (flagBytes[4] & 0x02) != 0,
            Poison = (flagBytes[4] & 0x04) != 0,
            Mind = (flagBytes[4] & 0x08) != 0,
        };
    }
}
