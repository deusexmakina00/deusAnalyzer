namespace PacketCapture;

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

    public override string ToString() =>
        $"{UsedBy} -> {Target} | Action: {Action} | Cast Time: {CastTime}s | Next Target: {NextTarget} | Action Name: {ActionName}";

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
