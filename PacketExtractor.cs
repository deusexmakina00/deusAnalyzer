using System.IO.Compression;
using System.Text;
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
    int DataLength,
    int EncodeType,
    byte[] Payload,
    int StartPosition,
    int EndPosition,
    uint RelSeq = 0,
    DateTime At = default
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

    // DataType : TypeName 매핑 사전 생성
    private static readonly Dictionary<int, string> typeMap = PacketTypeRegistry.GetPacketTypeMap();

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

    private static int[] Excludes = [100252, 10318, 1694498816];

    /// <summary>
    /// 바이트 배열에서 모든 유효한 패킷을 추출합니다.
    /// DataType(4) + Length(4) + Encode(1) + Payload(Length) 구조를 파싱합니다.
    /// </summary>
    /// <param name="data">패킷 데이터</param>
    /// <returns>추출된 패킷 목록</returns>
    public static List<ExtractedPacket> ExtractPackets(ReadOnlySpan<byte> data)
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
                if (!Excludes.Contains(packet.Value.DataType))
                {
                    packets.Add(packet.Value);
                }
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
            // DataType 읽기 (Little Endian)
            var dataTypeBytes = data.Slice(startPosition, 4);
            int dataType = dataTypeBytes.from_bytes<int>("little");

            // Length 읽기 (Little Endian)
            var lengthBytes = data.Slice(startPosition + 4, 4);
            int payloadLength = lengthBytes.from_bytes<int>("little");
            var encodeType = data[startPosition + 8];

            // 유효성 검사
            if (!IsValidPacketLength(payloadLength))
                return null;

            if (dataType <= 0 || dataType > 200000)
                return null;
            // 인코딩 타입이 Brotli(1) 또는 압축되지 않음(0)만 허용
            if (encodeType < 0 || encodeType > 1)
            {
                return null;
            }
            // 전체 패킷 길이 확인
            int totalPacketLength = PacketHeaderSize + payloadLength;
            if (startPosition + totalPacketLength > data.Length)
                return null;

            int endPosition = startPosition + totalPacketLength;

            // 페이로드 추출
            var rawPayload = data.Slice(startPosition + PacketHeaderSize, payloadLength);
            byte[] payload = ProcessPayload(rawPayload, encodeType);

            return new ExtractedPacket(
                DataType: dataType,
                DataLength: payloadLength,
                EncodeType: encodeType,
                Payload: payload,
                StartPosition: startPosition,
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
    }

    /// <summary>
    /// 추출된 패킷들을 시간과 시퀀스로 구분하여 파일로 저장합니다.
    /// </summary>
    /// <param name="packets">저장할 패킷 목록</param>
    /// <param name="baseDirectory">기본 저장 디렉토리</param>
    /// <param name="timestamp">패킷 수신 시간</param>
    /// <param name="sequenceNumber">시퀀스 번호</param>
    /// <returns>저장된 파일 경로 목록</returns>
    public static List<string> SavePacketsToFiles(
        List<ExtractedPacket> packets,
        string baseDirectory,
        DateTime timestamp,
        uint sequenceNumber
    )
    {
        if (packets.Count == 0)
            return [];

        var savedFiles = new List<string>();

        try
        {
            // 시간 기반 폴더 구조 생성: baseDirectory/YYYY-MM-DD/HH-mm-ss/seq_########
            string dateFolder = timestamp.ToString("yyyy-MM-dd");
            string timeFolder = timestamp.ToString("HH-mm-ss");
            string seqFolder = $"seq_{sequenceNumber:D8}";

            string fullDirectory = Path.Combine(baseDirectory, dateFolder, timeFolder, seqFolder);
            Directory.CreateDirectory(fullDirectory);

            // 패킷별로 개별 파일 저장
            for (int i = 0; i < packets.Count; i++)
            {
                var packet = packets[i];
                string fileName =
                    $"packet_{i:D3}_type_{packet.DataType:X8}_len_{packet.DataLength}.bin";
                string filePath = Path.Combine(fullDirectory, fileName);

                // 패킷 데이터를 바이너리 파일로 저장
                File.WriteAllBytes(filePath, packet.Payload);
                savedFiles.Add(filePath);

                // 메타데이터 파일도 함께 저장
                string metaFileName =
                    $"packet_{i:D3}_type_{packet.DataType:X8}_len_{packet.DataLength}.meta";
                string metaFilePath = Path.Combine(fullDirectory, metaFileName);

                string metaContent = CreatePacketMetadata(packet, timestamp, sequenceNumber, i);
                File.WriteAllText(metaFilePath, metaContent);
                savedFiles.Add(metaFilePath);
            }

            // 전체 패킷 요약 정보 저장
            string summaryPath = Path.Combine(fullDirectory, "packets_summary.txt");
            string summary = CreatePacketsSummary(packets, timestamp, sequenceNumber);
            File.WriteAllText(summaryPath, summary);
            savedFiles.Add(summaryPath);

            // 16진수 덤프 파일 저장
            string hexDumpPath = Path.Combine(fullDirectory, "packets_hexdump.txt");
            string hexDump = CreatePacketsHexDump(packets);
            File.WriteAllText(hexDumpPath, hexDump);
            savedFiles.Add(hexDumpPath);

            Console.WriteLine($"패킷 {packets.Count}개가 {fullDirectory}에 저장되었습니다.");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"패킷 저장 중 오류 발생: {ex.Message}");
        }

        return savedFiles;
    }

    /// <summary>
    /// 패킷의 메타데이터를 생성합니다.
    /// </summary>
    /// <param name="packet">패킷 정보</param>
    /// <param name="timestamp">수신 시간</param>
    /// <param name="sequenceNumber">시퀀스 번호</param>
    /// <param name="packetIndex">패킷 인덱스</param>
    /// <returns>메타데이터 문자열</returns>
    private static string CreatePacketMetadata(
        ExtractedPacket packet,
        DateTime timestamp,
        uint sequenceNumber,
        int packetIndex
    )
    {
        var sb = new StringBuilder();

        string typeName = typeMap.TryGetValue(packet.DataType, out var name)
            ? name
            : $"{packet.DataType}";

        sb.AppendLine("=== 패킷 메타데이터 ===");
        sb.AppendLine($"수신 시간: {timestamp:yyyy-MM-dd HH:mm:ss.fff}");
        sb.AppendLine($"시퀀스 번호: {sequenceNumber}");
        sb.AppendLine($"패킷 인덱스: {packetIndex}");
        sb.AppendLine($"데이터 타입: 0x{packet.DataType:X8} ({typeName})");
        sb.AppendLine($"인코딩 타입: {packet.EncodeType}");
        sb.AppendLine($"페이로드 길이: {packet.DataLength} bytes");
        sb.AppendLine($"원본 위치: 0x{packet.StartPosition:X8} - 0x{packet.EndPosition:X8}");
        sb.AppendLine($"페이로드 해시: {ComputeHash(packet.Payload)}");
        sb.AppendLine();
        sb.AppendLine("=== 페이로드 미리보기 (처음 64바이트) ===");

        int previewLength = Math.Min(64, packet.Payload.Length);
        var preview = packet.Payload[0..previewLength];
        sb.AppendLine(new ReadOnlySpan<byte>(preview).To_hex());

        if (packet.Payload.Length > 64)
        {
            sb.AppendLine($"... (총 {packet.Payload.Length - 64}바이트 더 있음)");
        }

        return sb.ToString();
    }

    /// <summary>
    /// 패킷 목록의 요약 정보를 생성합니다.
    /// </summary>
    /// <param name="packets">패킷 목록</param>
    /// <param name="timestamp">수신 시간</param>
    /// <param name="sequenceNumber">시퀀스 번호</param>
    /// <returns>요약 정보 문자열</returns>
    private static string CreatePacketsSummary(
        List<ExtractedPacket> packets,
        DateTime timestamp,
        uint sequenceNumber
    )
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== 패킷 세션 요약 ===");
        sb.AppendLine($"수신 시간: {timestamp:yyyy-MM-dd HH:mm:ss.fff}");
        sb.AppendLine($"시퀀스 번호: {sequenceNumber}");
        sb.AppendLine($"총 패킷 수: {packets.Count}");
        sb.AppendLine();

        // 패킷 타입별 통계
        var typeGroups = packets.GroupBy(p => p.DataType).OrderBy(g => g.Key);
        sb.AppendLine("=== 패킷 타입별 통계 ===");

        foreach (var group in typeGroups)
        {
            var avgLength = group.Average(p => p.DataLength);
            var minLength = group.Min(p => p.DataLength);
            var maxLength = group.Max(p => p.DataLength);
            var totalBytes = group.Sum(p => p.DataLength);

            sb.AppendLine($"타입 0x{group.Key:X8} ({group.Key}):");
            sb.AppendLine($"  개수: {group.Count()}");
            sb.AppendLine($"  총 바이트: {totalBytes:N0}");
            sb.AppendLine($"  평균 길이: {avgLength:F1}");
            sb.AppendLine($"  길이 범위: {minLength} - {maxLength}");
            sb.AppendLine();
        }

        // 개별 패킷 목록
        sb.AppendLine("=== 개별 패킷 목록 ===");
        for (int i = 0; i < packets.Count; i++)
        {
            var packet = packets[i];
            string typeName = typeMap.TryGetValue(packet.DataType, out var name)
                ? name
                : $"{packet.DataType}";

            sb.AppendLine(
                $"[{i:D3}] 타입: 0x{packet.DataType:X8} ({typeName}), "
                    + $"길이: {packet.DataLength}, "
                    + $"위치: 0x{packet.StartPosition:X8}-0x{packet.EndPosition:X8}"
            );
        }

        return sb.ToString();
    }

    /// <summary>
    /// 패킷들의 16진수 덤프를 생성합니다.
    /// </summary>
    /// <param name="packets">패킷 목록</param>
    /// <returns>16진수 덤프 문자열</returns>
    private static string CreatePacketsHexDump(List<ExtractedPacket> packets)
    {
        var sb = new StringBuilder();
        sb.AppendLine("=== 패킷 16진수 덤프 ===");
        sb.AppendLine();

        for (int i = 0; i < packets.Count; i++)
        {
            var packet = packets[i];
            string typeName = typeMap.TryGetValue(packet.DataType, out var name)
                ? name
                : $"{packet.DataType}";

            sb.AppendLine($"=== 패킷 {i:D3}: 타입 0x{packet.DataType:X8} ({typeName}) ===");
            sb.AppendLine(HexDump(packet));
            sb.AppendLine();
        }

        return sb.ToString();
    }

    /// <summary>
    /// 바이트 배열의 해시값을 계산합니다.
    /// </summary>
    /// <param name="data">해시를 계산할 데이터</param>
    /// <returns>SHA256 해시 문자열</returns>
    private static string ComputeHash(byte[] data)
    {
        using var sha256 = System.Security.Cryptography.SHA256.Create();
        var hash = sha256.ComputeHash(data);
        return Convert.ToHexString(hash);
    }

    /// <summary>
    /// 패킷 페이로드를 16진수 덤프 형태로 출력합니다.
    /// </summary>
    /// <param name="packet">덤프할 패킷</param>
    /// <param name="bytesPerLine">한 줄에 출력할 바이트 수</param>
    /// <returns>16진수 덤프 문자열</returns>
    public static string HexDump(ExtractedPacket packet, int bytesPerLine = 16)
    {
        var payload = packet.Payload;
        var dump = new StringBuilder();
        string typeName = typeMap.TryGetValue(packet.DataType, out var name)
            ? name
            : $"{packet.DataType}";
        dump.AppendLine($"Sequence: {packet.RelSeq}");
        dump.AppendLine($"Received: {packet.At:yyyy-MM-dd HH:mm:ss.fff}");
        dump.AppendLine($"DataType: 0x{packet.DataType:X8} ({typeName})");
        dump.AppendLine($"Length: {packet.DataLength} bytes");
        dump.AppendLine($"EncodeType: {packet.EncodeType}");
        dump.AppendLine($"Position: 0x{packet.StartPosition:X8}-0x{packet.EndPosition:X8}");
        dump.AppendLine("Payload:");

        for (int i = 0; i < payload.Length; i += bytesPerLine)
        {
            var lineBytes = Math.Min(bytesPerLine, payload.Length - i);
            var line = payload[i..(i + lineBytes)];

            dump.Append($"{i:X4}: ");
            dump.Append(new ReadOnlySpan<byte>(line).To_hex(" "));

            if (lineBytes < bytesPerLine)
            {
                dump.Append(new string(' ', (bytesPerLine - lineBytes) * 3));
            }

            dump.Append(" |");
            foreach (byte b in line)
            {
                dump.Append(b >= 32 && b <= 126 ? (char)b : '.');
            }
            dump.AppendLine("|");
        }

        return dump.ToString();
    }
}
