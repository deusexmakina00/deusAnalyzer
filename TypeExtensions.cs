using System.Buffers.Binary;

namespace PacketCapture;

public static class TypeExtensions
{
    public static T from_bytes<T>(this ReadOnlySpan<byte> bytes, string byteOrder = "little")
        where T : struct
    {
        return byteOrder switch
        {
            "little" => ReadLittleEndian<T>(bytes),
            "big" => ReadBigEndian<T>(bytes),
            _ => throw new ArgumentException("잘못된 바이트 순서입니다.", nameof(byteOrder)),
        };
    }

    private static T ReadLittleEndian<T>(ReadOnlySpan<byte> bytes)
        where T : struct
    {
        return typeof(T) switch
        {
            Type t when t == typeof(byte) => (T)(object)bytes[0],
            Type t when t == typeof(sbyte) => (T)(object)(sbyte)bytes[0],
            Type t when t == typeof(short) => CheckLength<T>(bytes, 2)
                ? (T)(object)BinaryPrimitives.ReadInt16LittleEndian(bytes)
                : default,
            Type t when t == typeof(ushort) => CheckLength<T>(bytes, 2)
                ? (T)(object)BinaryPrimitives.ReadUInt16LittleEndian(bytes)
                : default,
            Type t when t == typeof(int) => CheckLength<T>(bytes, 4)
                ? (T)(object)BinaryPrimitives.ReadInt32LittleEndian(bytes)
                : default,
            Type t when t == typeof(uint) => CheckLength<T>(bytes, 4)
                ? (T)(object)BinaryPrimitives.ReadUInt32LittleEndian(bytes)
                : default,
            Type t when t == typeof(long) => CheckLength<T>(bytes, 8)
                ? (T)(object)BinaryPrimitives.ReadInt64LittleEndian(bytes)
                : default,
            Type t when t == typeof(ulong) => CheckLength<T>(bytes, 8)
                ? (T)(object)BinaryPrimitives.ReadUInt64LittleEndian(bytes)
                : default,
            Type t when t == typeof(float) => CheckLength<T>(bytes, 4)
                ? (T)(object)BinaryPrimitives.ReadSingleLittleEndian(bytes)
                : default,
            Type t when t == typeof(double) => CheckLength<T>(bytes, 8)
                ? (T)(object)BinaryPrimitives.ReadDoubleLittleEndian(bytes)
                : default,
            _ => throw new NotSupportedException($"타입 {typeof(T)}은 지원되지 않습니다."),
        };
    }

    private static T ReadBigEndian<T>(ReadOnlySpan<byte> bytes)
        where T : struct
    {
        return typeof(T) switch
        {
            Type t when t == typeof(byte) => (T)(object)bytes[0],
            Type t when t == typeof(sbyte) => (T)(object)(sbyte)bytes[0],
            Type t when t == typeof(short) => CheckLength<T>(bytes, 2)
                ? (T)(object)BinaryPrimitives.ReadInt16BigEndian(bytes)
                : default,
            Type t when t == typeof(ushort) => CheckLength<T>(bytes, 2)
                ? (T)(object)BinaryPrimitives.ReadUInt16BigEndian(bytes)
                : default,
            Type t when t == typeof(int) => CheckLength<T>(bytes, 4)
                ? (T)(object)BinaryPrimitives.ReadInt32BigEndian(bytes)
                : default,
            Type t when t == typeof(uint) => CheckLength<T>(bytes, 4)
                ? (T)(object)BinaryPrimitives.ReadUInt32BigEndian(bytes)
                : default,
            Type t when t == typeof(long) => CheckLength<T>(bytes, 8)
                ? (T)(object)BinaryPrimitives.ReadInt64BigEndian(bytes)
                : default,
            Type t when t == typeof(ulong) => CheckLength<T>(bytes, 8)
                ? (T)(object)BinaryPrimitives.ReadUInt64BigEndian(bytes)
                : default,
            Type t when t == typeof(float) => CheckLength<T>(bytes, 4)
                ? (T)(object)BinaryPrimitives.ReadSingleBigEndian(bytes)
                : default,
            Type t when t == typeof(double) => CheckLength<T>(bytes, 8)
                ? (T)(object)BinaryPrimitives.ReadDoubleBigEndian(bytes)
                : default,
            _ => throw new NotSupportedException($"타입 {typeof(T)}은 지원되지 않습니다."),
        };
    }

    private static bool CheckLength<T>(ReadOnlySpan<byte> bytes, int requiredLength)
    {
        if (bytes.Length < requiredLength)
            throw new ArgumentOutOfRangeException(
                nameof(bytes),
                $"타입 {typeof(T)}을 읽기 위해서는 최소 {requiredLength}바이트가 필요합니다."
            );
        return true;
    }

    public static string To_hex(this ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length == 0)
            return string.Empty;

        // 바이트 배열을 16진수 문자열로 변환
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }
}
