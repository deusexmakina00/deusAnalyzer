using PacketCapture;

public sealed class ChangeHpPacket : Packet
{
    public static readonly int[] TYPE = [100178];

    public string Target { get; init; } = string.Empty;
    public int PrevHp { get; init; }
    public int CurrentHp { get; init; }
    public int Damage => PrevHp - CurrentHp;

    public ChangeHpPacket(string target, int prevHp, int currentHp)
    {
        Target = target;
        PrevHp = prevHp;
        CurrentHp = currentHp;
    }

    public static ChangeHpPacket Parse(ReadOnlySpan<byte> content)
    {
        if (content.Length < 16) // 최소 패킷 크기 확인
            return new ChangeHpPacket(string.Empty, 0, 0);

        string target = content[..4].To_hex();
        content = content[8..]; // 4바이트 데이터 + 4바이트 패딩
        int prevHp = content[..4].from_bytes<int>("little");
        content = content[4..]; // 이전 HP 이후 데이터 건너뛰기
        int currentHp = content[..4].from_bytes<int>("little");
        content = content[4..]; // 현재 HP 이후 데이터 건너뛰기

        return new ChangeHpPacket(target, prevHp, currentHp);
    }

    public override string ToString() =>
        $"{Target} | 이전 HP: {PrevHp} | 현재 HP: {CurrentHp} | 데미지: {Damage}";
}
