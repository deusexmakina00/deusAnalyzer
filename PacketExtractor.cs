using System.IO.Compression;
using SharpPcap.WinpkFilter;

namespace PacketCapture;

/// <summary>
/// 추출된 패킷 정보를 나타내는 불변 레코드 (.NET 8 record)
/// </summary>
/// <param name="DataType">패킷 데이터 타입</param>
/// <param name="Payload">패킷 페이로드</param>
/// <param name="DataLength">데이터 길이</param>
/// <param name="StartPosition">시작 위치</param>
/// <param name="EndPosition">끝 위치</param>
public readonly record struct ExtractedPacket(
    int DataType,
    byte[] Payload,
    int DataLength,
    int StartPosition,
    int EndPosition
);

/// <summary>
/// 네트워크 패킷에서 특정 데이터 타입의 패킷을 추출하는 고성능 유틸리티 클래스
/// 최신 C# 기능과 Span<T>를 활용한 메모리 효율적 구현
/// </summary>
public static class PacketExtractor
{
    /// <summary>패킷 헤더 크기 (바이트)</summary>
    private const int PacketHeaderSize = 9;

    /// <summary>최대 허용 패킷 길이</summary>
    private const int MaxPacketLength = 65536;

    /// <summary>Brotli 압축 인코딩 타입</summary>
    private const byte BrotliEncodeType = 1;

    /// <summary>
    /// ReadOnlySpan을 사용한 고성능 패턴 검색
    /// </summary>
    /// <param name="data">검색할 데이터</param>
    /// <param name="pattern">찾을 패턴</param>
    /// <returns>패턴이 발견된 인덱스 목록</returns>
    public static List<int> FindPattern(ReadOnlySpan<byte> data, ReadOnlySpan<byte> pattern)
    {
        if (pattern.IsEmpty || data.Length < pattern.Length)
            return [];

        var indices = new List<int>();

        for (int i = 0; i <= data.Length - pattern.Length; i++)
        {
            if (data.Slice(i, pattern.Length).SequenceEqual(pattern))
            {
                indices.Add(i);
            }
        }

        return indices;
    }

    /// <summary>
    /// 바이트 배열에서 지정된 데이터 타입들의 패킷을 추출합니다.
    /// Span<T>와 최신 C# 기능을 활용한 고성능 구현
    /// </summary>
    /// <param name="data">패킷 데이터</param>
    /// <param name="dataTypes">추출할 데이터 타입 배열</param>
    /// <returns>추출된 패킷 정보 목록(필터링됨)</returns>
    public static List<ExtractedPacket> ExtractPackets(
        ReadOnlySpan<byte> data,
        ReadOnlySpan<int> dataTypes
    )
    {
        if (data.IsEmpty || dataTypes.IsEmpty)
            return [];

        var allPackets = ExtractPackets(data);

        var filteredPackets = new List<ExtractedPacket>();

        foreach (var packet in allPackets)
        {
            if (dataTypes.Contains(packet.DataType))
            {
                filteredPackets.Add(packet);
            }
        }
        return filteredPackets;
    }

    /// <summary>
    /// 바이트 배열에서 모든 유효한 패킷을 추출합니다.
    /// DataType(4) + Length(4) + Encode(1) + Payload(Length) 구조를 파싱합니다.
    /// </summary>
    /// <param name="data">패킷 데이터</param>
    /// <returns>추출된 패킷 목록</returns>
    private static List<ExtractedPacket> ExtractPackets(ReadOnlySpan<byte> data)
    {
        if (data.IsEmpty)
            return [];

        var packets = new List<ExtractedPacket>();
        int position = 0;

        while (position <= data.Length - PacketHeaderSize)
        {
            // 단일 패킷 추출 시도
            var packet = TryExtractSinglePacket(data, position);
            if (packet.HasValue)
            {
                packets.Add(packet.Value);
                position = packet.Value.EndPosition;
            }
            else
            {
                // 패킷 추출 실패 시 다음 바이트로 이동
                position++;
            }
        }
        packets.Sort((p1, p2) => p1.StartPosition.CompareTo(p2.StartPosition));
        return packets;
    }

    /// <summary>
    /// 지정된 위치에서 다음 단일 패킷 추출을 시도합니다.
    /// </summary>
    /// <param name="data">전체 데이터</param>
    /// <param name="startPosition">시작 위치</param>
    /// <returns>추출된 패킷 또는 null</returns>
    private static ExtractedPacket? TryExtractSinglePacket(
        ReadOnlySpan<byte> data,
        int startPosition
    )
    {
        // 최소 헤더 크기 확인(Data Type(4) + Length(4) + Encode(1) = 9바이트)
        if (startPosition + PacketHeaderSize > data.Length)
            return null;

        try
        {
            // 패킷 헤더에서 데이터 타입과 길이 추출
            var dataType = data[..4].from_bytes<int>("little");
            var payloadLength = data[4..8].from_bytes<int>("little");
            var encodeType = data[startPosition + 8];

            // 유효성 검사
            if (!IsValidPacketLength(payloadLength))
                return null;
            int totalPacketLength = PacketHeaderSize + payloadLength;

            // 페이로드 추출
            var rawPayload = data.Slice(startIndex + PacketHeaderSize, dataLength);
            byte[] payload = ProcessPayload(rawPayload, encodeType);

            return new ExtractedPacket(
                DataType: dataType,
                Payload: payload,
                DataLength: dataLength,
                StartPosition: startIndex,
                EndPosition: endPosition
            );
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// 패킷 길이가 유효한지 확인합니다.
    /// </summary>
    /// <param name="length">확인할 길이</param>
    /// <returns>유효성 여부</returns>
    private static bool IsValidPacketLength(int length) => length is >= 1 and <= MaxPacketLength;

    /// <summary>
    /// 페이로드를 처리합니다 (압축 해제 포함).
    /// </summary>
    /// <param name="rawPayload">원본 페이로드</param>
    /// <param name="encodeType">인코딩 타입</param>
    /// <returns>처리된 페이로드</returns>
    private static byte[] ProcessPayload(ReadOnlySpan<byte> rawPayload, byte encodeType)
    {
        // 압축되지 않은 데이터인 경우
        if (encodeType != BrotliEncodeType)
            return rawPayload.ToArray();

        // Brotli 압축 해제 시도
        try
        {
            return DecompressBrotli(rawPayload);
        }
        catch
        {
            // 압축 해제 실패 시 원본 반환
            return rawPayload.ToArray();
        }
    }

    /// <summary>
    /// Brotli 압축을 해제합니다.
    /// </summary>
    /// <param name="compressedData">압축된 데이터</param>
    /// <returns>압축 해제된 데이터</returns>
    private static byte[] DecompressBrotli(ReadOnlySpan<byte> compressedData)
    {
        using var compressedStream = new MemoryStream(compressedData.ToArray());
        using var brotliStream = new BrotliStream(compressedStream, CompressionMode.Decompress);
        using var decompressedStream = new MemoryStream();

        brotliStream.CopyTo(decompressedStream);
        return decompressedStream.ToArray();
    } // 이전 버전과의 호환성을 위한 오버로드
}
