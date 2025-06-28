using System.Text.Json;
using NLog;

namespace PacketCapture;

/// <summary>
/// 저장된 패킷 데이터를 읽어와서 재생하는 클래스
/// </summary>
public class PacketPlayer : IDisposable
{
    private static readonly Logger logger = LogManager.GetCurrentClassLogger();

    private readonly string _baseDirectory;
    private readonly CancellationTokenSource _cancellationTokenSource;
    private bool _isPlaying;
    private bool _disposed;

    /// <summary>
    /// 재생 중인 패킷 정보 이벤트
    /// </summary>
    public event Action<ReplayPacketInfo>? OnPacketReplayed;

    /// <summary>
    /// 재생 상태 변경 이벤트
    /// </summary>
    public event Action<PlaybackState>? OnPlaybackStateChanged;

    public PacketPlayer(string baseDirectory)
    {
        _baseDirectory = baseDirectory ?? throw new ArgumentNullException(nameof(baseDirectory));
        _cancellationTokenSource = new CancellationTokenSource();

        if (!Directory.Exists(_baseDirectory))
        {
            throw new DirectoryNotFoundException($"Packet directory not found: {_baseDirectory}");
        }

        logger.Info($"PacketPlayer initialized with directory: {_baseDirectory}");
    }

    /// <summary>
    /// 지정된 날짜의 패킷 세션 목록을 가져옵니다
    /// </summary>
    /// <param name="date">조회할 날짜</param>
    /// <returns>세션 정보 목록</returns>
    public async Task<List<PacketSessionInfo>> GetPacketSessionsAsync(DateTime date)
    {
        var sessions = new List<PacketSessionInfo>();
        var dateFolder = Path.Combine(_baseDirectory, date.ToString("yyyy-MM-dd"));

        if (!Directory.Exists(dateFolder))
        {
            logger.Warn($"No packets found for date: {date:yyyy-MM-dd}");
            return sessions;
        }

        var timeFolders = Directory.GetDirectories(dateFolder).OrderBy(d => d).ToList();

        foreach (var timeFolder in timeFolders)
        {
            var timeFolderName = Path.GetFileName(timeFolder);
            var seqFolders = Directory.GetDirectories(timeFolder).OrderBy(d => d).ToList();

            foreach (var seqFolder in seqFolders)
            {
                try
                {
                    var sessionInfo = await LoadSessionInfoAsync(seqFolder, date, timeFolderName);
                    if (sessionInfo != null)
                    {
                        sessions.Add(sessionInfo);
                    }
                }
                catch (Exception ex)
                {
                    logger.Error(ex, $"Failed to load session from: {seqFolder}");
                }
            }
        }

        return sessions.OrderBy(s => s.Timestamp).ToList();
    }

    /// <summary>
    /// 특정 세션의 패킷들을 재생합니다
    /// </summary>
    /// <param name="sessionInfo">재생할 세션 정보</param>
    /// <param name="playbackSpeed">재생 속도 (1.0 = 실시간, 2.0 = 2배속)</param>
    /// <param name="packetProcessor">패킷 처리 함수</param>
    public async Task PlaySessionAsync(
        PacketSessionInfo sessionInfo,
        double playbackSpeed = 1.0,
        Func<ExtractedPacket, Task>? packetProcessor = null
    )
    {
        if (_isPlaying)
        {
            logger.Warn("Already playing. Stop current playback first.");
            return;
        }

        _isPlaying = true;
        OnPlaybackStateChanged?.Invoke(PlaybackState.Playing);

        try
        {
            logger.Info($"Starting playback of session: {sessionInfo.SessionPath}");

            var packets = await LoadPacketsFromSessionAsync(sessionInfo.SessionPath);
            var sortedPackets = packets.OrderBy(p => p.StartPosition).ToList();

            logger.Info($"Loaded {sortedPackets.Count} packets for replay");

            for (int i = 0; i < sortedPackets.Count; i++)
            {
                if (_cancellationTokenSource.Token.IsCancellationRequested)
                {
                    logger.Info("Playback cancelled");
                    break;
                }

                var packet = sortedPackets[i];

                // 패킷 재생 이벤트 발생
                OnPacketReplayed?.Invoke(
                    new ReplayPacketInfo
                    {
                        Packet = packet,
                        SessionInfo = sessionInfo,
                        CurrentIndex = i,
                        TotalCount = sortedPackets.Count,
                        PlaybackSpeed = playbackSpeed,
                    }
                );

                // 외부 패킷 처리기 호출
                if (packetProcessor != null)
                {
                    await packetProcessor(packet);
                }

                // 다음 패킷까지의 지연 시간 계산 (재생 속도 적용)
                if (i < sortedPackets.Count - 1)
                {
                    // 기본 50ms 간격으로 패킷 재생 (원래 타이밍 정보가 없으므로)
                    var delay = 50.0 / playbackSpeed;

                    if (delay > 0 && delay < 5000) // 최대 5초 지연
                    {
                        await Task.Delay(
                            TimeSpan.FromMilliseconds(delay),
                            _cancellationTokenSource.Token
                        );
                    }
                }
            }

            logger.Info("Playback completed successfully");
        }
        catch (OperationCanceledException)
        {
            logger.Info("Playback was cancelled");
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error during playback");
            throw;
        }
        finally
        {
            _isPlaying = false;
            OnPlaybackStateChanged?.Invoke(PlaybackState.Stopped);
        }
    }

    /// <summary>
    /// 여러 세션을 연속으로 재생합니다
    /// </summary>
    public async Task PlayMultipleSessionsAsync(
        List<PacketSessionInfo> sessions,
        double playbackSpeed = 1.0,
        Func<ExtractedPacket, Task>? packetProcessor = null
    )
    {
        if (_isPlaying)
        {
            logger.Warn("Already playing. Stop current playback first.");
            return;
        }

        logger.Info($"Starting playback of {sessions.Count} sessions");

        foreach (var session in sessions.OrderBy(s => s.Timestamp))
        {
            if (_cancellationTokenSource.Token.IsCancellationRequested)
                break;

            await PlaySessionAsync(session, playbackSpeed, packetProcessor);

            // 세션 간 짧은 대기
            await Task.Delay(100, _cancellationTokenSource.Token);
        }
    }

    /// <summary>
    /// 재생을 중지합니다
    /// </summary>
    public void Stop()
    {
        if (_isPlaying)
        {
            logger.Info("Stopping playback...");
            _cancellationTokenSource.Cancel();
        }
    }

    /// <summary>
    /// 세션 정보를 로드합니다
    /// </summary>
    private async Task<PacketSessionInfo?> LoadSessionInfoAsync(
        string sessionPath,
        DateTime date,
        string timeFolder
    )
    {
        var summaryPath = Path.Combine(sessionPath, "packets_summary.txt");
        if (!File.Exists(summaryPath))
        {
            return null;
        }

        var summaryContent = await File.ReadAllTextAsync(summaryPath);
        var seqFolderName = Path.GetFileName(sessionPath);

        // 시퀀스 번호 추출 (seq_########)
        if (!seqFolderName.StartsWith("seq_") || seqFolderName.Length != 12)
        {
            return null;
        }

        if (!uint.TryParse(seqFolderName[4..], out var sequenceNumber))
        {
            return null;
        }

        // 패킷 파일 개수 확인
        var packetFiles = Directory.GetFiles(sessionPath, "packet_*.bin");

        // 타임스탬프 파싱
        if (
            !DateTime.TryParseExact(
                $"{date:yyyy-MM-dd} {timeFolder.Replace('-', ':')}",
                "yyyy-MM-dd HH:mm:ss",
                null,
                System.Globalization.DateTimeStyles.None,
                out var timestamp
            )
        )
        {
            timestamp = date;
        }

        return new PacketSessionInfo
        {
            SessionPath = sessionPath,
            Timestamp = timestamp,
            SequenceNumber = sequenceNumber,
            PacketCount = packetFiles.Length,
            SummaryContent = summaryContent,
        };
    }

    /// <summary>
    /// 세션에서 모든 패킷을 로드합니다
    /// </summary>
    private async Task<List<ExtractedPacket>> LoadPacketsFromSessionAsync(string sessionPath)
    {
        var packets = new List<ExtractedPacket>();

        var packetFiles = Directory.GetFiles(sessionPath, "packet_*.bin").OrderBy(f => f).ToList();

        foreach (var packetFile in packetFiles)
        {
            try
            {
                var metaFile = packetFile.Replace(".bin", ".meta");
                var packet = await LoadSinglePacketAsync(packetFile, metaFile);
                if (packet.HasValue)
                {
                    packets.Add(packet.Value);
                }
            }
            catch (Exception ex)
            {
                logger.Error(ex, $"Failed to load packet: {packetFile}");
            }
        }

        return packets;
    }

    /// <summary>
    /// 단일 패킷 파일을 로드합니다
    /// </summary>
    private async Task<ExtractedPacket?> LoadSinglePacketAsync(string packetFile, string metaFile)
    {
        if (!File.Exists(packetFile) || !File.Exists(metaFile))
        {
            return null;
        }

        var payload = await File.ReadAllBytesAsync(packetFile);
        var metaContent = await File.ReadAllTextAsync(metaFile);

        // 메타데이터에서 정보 추출
        var metadata = ParseMetadata(metaContent);

        return new ExtractedPacket(
            DataType: metadata.DataType,
            DataLength: metadata.DataLength,
            EncodeType: metadata.EncodeType,
            Payload: payload,
            StartPosition: metadata.StartPosition,
            EndPosition: metadata.EndPosition,
            RelSeq: metadata.SequenceNumber,
            At: metadata.Timestamp
        );
    }

    /// <summary>
    /// 메타데이터 파일을 파싱합니다
    /// </summary>
    private PacketMetadata ParseMetadata(string metaContent)
    {
        var metadata = new PacketMetadata();

        var lines = metaContent.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        foreach (var line in lines)
        {
            var trimmedLine = line.Trim();

            if (trimmedLine.StartsWith("수신 시간:"))
            {
                var timeStr = trimmedLine[6..].Trim();
                if (DateTime.TryParse(timeStr, out var timestamp))
                {
                    metadata.Timestamp = timestamp;
                }
            }
            else if (trimmedLine.StartsWith("시퀀스 번호:"))
            {
                var seqStr = trimmedLine[7..].Trim();
                if (uint.TryParse(seqStr, out var seq))
                {
                    metadata.SequenceNumber = seq;
                }
            }
            else if (trimmedLine.StartsWith("데이터 타입:"))
            {
                var typeStr = trimmedLine[7..].Trim();
                var hexPart = typeStr.Split(' ')[0];
                if (
                    hexPart.StartsWith("0x")
                    && int.TryParse(
                        hexPart[2..],
                        System.Globalization.NumberStyles.HexNumber,
                        null,
                        out var dataType
                    )
                )
                {
                    metadata.DataType = dataType;
                }
            }
            else if (trimmedLine.StartsWith("인코딩 타입:"))
            {
                var encodeStr = trimmedLine[7..].Trim();
                if (int.TryParse(encodeStr, out var encodeType))
                {
                    metadata.EncodeType = encodeType;
                }
            }
            else if (trimmedLine.StartsWith("페이로드 길이:"))
            {
                var lengthStr = trimmedLine[8..].Trim().Split(' ')[0];
                if (int.TryParse(lengthStr, out var length))
                {
                    metadata.DataLength = length;
                }
            }
            else if (trimmedLine.StartsWith("원본 위치:"))
            {
                var posStr = trimmedLine[6..].Trim();
                var positions = posStr.Split(" - ");
                if (positions.Length == 2)
                {
                    if (
                        positions[0].StartsWith("0x")
                        && int.TryParse(
                            positions[0][2..],
                            System.Globalization.NumberStyles.HexNumber,
                            null,
                            out var start
                        )
                    )
                    {
                        metadata.StartPosition = start;
                    }
                    if (
                        positions[1].StartsWith("0x")
                        && int.TryParse(
                            positions[1][2..],
                            System.Globalization.NumberStyles.HexNumber,
                            null,
                            out var end
                        )
                    )
                    {
                        metadata.EndPosition = end;
                    }
                }
            }
        }

        return metadata;
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        try
        {
            Stop();
            _cancellationTokenSource.Dispose();
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error during disposal");
        }
        finally
        {
            _disposed = true;
        }
    }
}

/// <summary>
/// 패킷 세션 정보
/// </summary>
public class PacketSessionInfo
{
    public string SessionPath { get; set; } = string.Empty;
    public DateTime Timestamp { get; set; }
    public uint SequenceNumber { get; set; }
    public int PacketCount { get; set; }
    public string SummaryContent { get; set; } = string.Empty;
}

/// <summary>
/// 재생 중인 패킷 정보
/// </summary>
public class ReplayPacketInfo
{
    public ExtractedPacket Packet { get; set; }
    public PacketSessionInfo SessionInfo { get; set; } = new();
    public int CurrentIndex { get; set; }
    public int TotalCount { get; set; }
    public double PlaybackSpeed { get; set; }
}

/// <summary>
/// 재생 상태
/// </summary>
public enum PlaybackState
{
    Stopped,
    Playing,
    Paused,
}

/// <summary>
/// 패킷 메타데이터
/// </summary>
internal class PacketMetadata
{
    public DateTime Timestamp { get; set; }
    public uint SequenceNumber { get; set; }
    public int DataType { get; set; }
    public int EncodeType { get; set; }
    public int DataLength { get; set; }
    public int StartPosition { get; set; }
    public int EndPosition { get; set; }
}
