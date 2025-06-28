using PacketCapture;

public sealed class SkillStatePacket : Packet
{
    /// <summary>지원되는 패킷 타입</summary>
    public static readonly int[] TYPE = [10299];

    public string UsedBy { get; init; } = string.Empty;
    public string Target { get; init; } = string.Empty;

    public string Action { get; init; } = string.Empty;

    public byte[] flags_bytes { get; init; } = [];
    public FlagBits Flags { get; init; } = new FlagBits();

    /// <summary>
    /// 패킷을 생성합니다.
    /// </summary>
    /// <param name="data">패킷 데이터</param>
    public SkillStatePacket(
        string usageBy,
        string target,
        string action,
        byte[] flagsBytes,
        FlagBits flags
    )
    {
        UsedBy = usageBy;
        Target = target;
        Action = action;
        flags_bytes = flagsBytes;
        Flags = flags;
    }

    /// <inheritdoc />
    public override string ToString() =>
        $"{UsedBy} -> {Target} | Action: {Action} | Flags: {Flags}";

    public static SkillStatePacket Parse(ReadOnlySpan<byte> content)
    {
        // 패킷 길이 검사
        if (content.Length < 30)
            return new SkillStatePacket(
                string.Empty,
                string.Empty,
                string.Empty,
                [],
                new FlagBits()
            );

        // 사용자 ID 추출 (4바이트, 4바이트 패딩 후)
        string usedBy = content[..4].To_hex();
        content = content[8..]; // 4바이트 데이터 + 4바이트 패딩

        // 대상 ID 추출 (4바이트, 4바이트 패딩 후)
        string target = content[..4].To_hex();
        content = content[8..]; // 4바이트 데이터 + 4바이트 패딩

        // 액션 추출 (4바이트, 4바이트 패딩 후)
        string action = content[..4].To_hex();
        content = content[8..]; // 4바이트 데이터 + 4바이트 패딩
        // 플래그 바이트 추출 (6바이트)
        var flagBytes = content[..6].ToArray();
        content = content[6..];

        var flags = FlagBits.ParseFlags(flagBytes);
        return new SkillStatePacket(usedBy, target, action, flagBytes, flags);
    }
}
