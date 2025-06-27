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
    private static bool IsLikelyUnicode(ReadOnlySpan<byte> data)
    {
        if (data.Length < 4 || data.Length % 2 != 0)
            return false;

        int nullCount = 0;
        for (int i = 1; i < data.Length; i += 2)
        {
            if (data[i] == 0)
                nullCount++;
        }

        // 홀수 인덱스 바이트의 50% 이상이 null이면 Unicode로 판단
        return nullCount > (data.Length / 4);
    }

    /// <summary>
    /// 패킷에서 이름을 추출합니다.
    /// </summary>
    protected static (string skillName, int byteLength) ExtractName(ReadOnlySpan<byte> content)
    {
        if (content.Length < 4)
            return (string.Empty, 0);

        // 이름 길이 읽기 (Little Endian)
        int nameLength = content[..4].from_bytes<int>("little");

        if (content.Length < 4 + nameLength)
            return (string.Empty, 4);

        // 실제 바이트 길이 = 길이필드(4) + 이름바이트(nameLength)
        int totalByteLength = 4 + nameLength;
        // 이름 바이트 추출
        var nameBytes = content.Slice(4, nameLength);
        // Unicode 감지: 짝수 길이이고 홀수 인덱스에 null 바이트가 많은 경우
        bool isUnicode = IsLikelyUnicode(nameBytes);
        string cleanName;
        if (isUnicode)
        {
            cleanName = Encoding.Unicode.GetString(nameBytes).TrimEnd('\0');
        }
        else
        {
            // null 바이트 필터링 및 UTF-8 디코딩
            Span<byte> asciiBytes = stackalloc byte[nameLength];
            int asciiLength = 0;
            for (int i = 0; i < nameBytes.Length; i++)
            {
                byte b = nameBytes[i];
                if (b != 0 && b >= 0x20 && b <= 0x7E)
                    asciiBytes[asciiLength++] = b;
            }
            cleanName =
                asciiLength > 0
                    ? Encoding.UTF8.GetString(asciiBytes.Slice(0, asciiLength))
                    : string.Empty;
        }
        return (cleanName, totalByteLength);
    }
}
