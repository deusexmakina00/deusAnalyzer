namespace PacketCapture;

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
