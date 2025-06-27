namespace PacketCapture;

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
