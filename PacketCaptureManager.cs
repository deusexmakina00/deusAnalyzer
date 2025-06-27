using System.Collections.Frozen;
using System.Diagnostics.CodeAnalysis;
using System.Runtime.CompilerServices;
using NLog;

namespace PacketCapture;

/// <summary>
/// 패킷 캡처 및 처리를 담당하는 메인 관리 클래스
/// npcap/libpcap을 통한 크로스 플랫폼 패킷 캡처와
/// 스킬-데미지 매칭 로직을 처리합니다.
/// </summary>
public sealed class PacketCaptureManager : IDisposable
{
    private static readonly Logger logger = LogManager.GetCurrentClassLogger();
    private const int PORT = 16000;
    private const int MAX_BUFFER_SIZE = 3 * 1024 * 1024;
    private const double SKILL_TIMEOUT_SECONDS = 5.0;
    private static readonly FrozenSet<int> ExceptedDataTypes = PacketTypeRegistry
        .GetPacketTypeMap()
        .Keys.ToFrozenSet();
    private readonly SkillMatcher _skillMatcher = new();
    private readonly MemoryStream _buffer = new();
    private readonly object _lockObject = new();
    private uint _lastRelSeq;
    private DateTime _lastAt = DateTime.UtcNow;
    private NpcapPacketCapture? _npcapCapture;
    private ModernWebSocketServer? _webSocketServer;
    private bool _disposed;

    private async void OnDamageMatched(SkillDamagePacket damage)
    {
        if (_webSocketServer is not null)
        {
            logger.Info(
                $"[Capture][{damage.SkillName}] Damage found: {damage.Damage} (UsedBy: {damage.UsedBy}, Target: {damage.Target})"
            );
            await _webSocketServer.BroadcastMessageAsync(damage.ToLog());
        }
    }

    /// <summary>
    /// 패킷 캡처 매니저를 시작합니다.
    /// </summary>
    /// <param name="webSocketServer">WebSocket 서버 인스턴스</param>
    /// <returns>시작 작업</returns>
    public Task StartAsync(ModernWebSocketServer webSocketServer)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(webSocketServer);

        _webSocketServer = webSocketServer;
        _skillMatcher.OnDamageMatched += OnDamageMatched;
        logger.Info("Starting packet capture manager...");
        StartPacketCapture();

        return Task.CompletedTask;
    }

    /// <summary>
    /// 패킷 캡처를 중지합니다.
    /// </summary>
    public void Stop()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        StopPacketCapture();
    }

    /// <summary>
    /// npcap 패킷 캡처를 시작합니다.
    /// </summary>
    private void StartPacketCapture()
    {
        logger.Info("Starting npcap packet capture...");

        try
        {
            // npcap 가용성 확인
            if (!NpcapPacketCapture.IsNpcapAvailable())
            {
                throw new InvalidOperationException(
                    "Npcap is not available. Please install Npcap."
                );
            }

            logger.Info("Npcap is available, starting packet capture...");
            _npcapCapture = new NpcapPacketCapture(PORT, OnPacketReceived);
            _npcapCapture.StartCapture();
            logger.Info($"Npcap packet capture started for port {PORT}");

            // 크로스 플랫폼 지원을 위한 로깅
            var platformInfo = Environment.OSVersion.Platform;
            logger.Info($"Running on platform: {platformInfo}");
            logger.Info("Using libpcap-based packet capture (cross-platform compatible)");
        }
        catch (Exception ex)
        {
            logger.Error($"Failed to start npcap packet capture: {ex.Message}");
            logger.Error("Please ensure:");
            logger.Error("  - On Windows: Npcap is installed (https://npcap.org/)");
            logger.Error("  - On Linux: libpcap-dev is installed (apt-get install libpcap-dev)");
            logger.Error("  - On macOS: libpcap is available (comes with Xcode tools)");
            logger.Error("  - Run as administrator/root if required");

            throw new InvalidOperationException(
                "Packet capture initialization failed. See logs for details.",
                ex
            );
        }
    }

    /// <summary>
    /// 패킷 캡처를 중지합니다.
    /// </summary>
    private void StopPacketCapture()
    {
        _npcapCapture?.StopCapture();
        logger.Info("Packet capture stopped");
    }

    /// <summary>
    /// 패킷 수신 시 호출되는 콜백 메서드
    /// </summary>
    /// <param name="data">패킷 데이터</param>
    /// <param name="seq">패킷 시퀀스 번호</param>
    /// <param name="timestamp">수신 시각</param>
    private void OnPacketReceived(byte[] data, uint seq, DateTime timestamp)
    {
        try
        {
            var payloadData = new PacketPayload(data, seq, timestamp);
            ParseData(payloadData);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error processing packet");
        }
    }

    /// <summary>
    /// 패킷 데이터를 파싱하여 데미지 정보를 추출합니다.
    /// </summary>
    /// <param name="payloadData">패킷 페이로드</param>
    /// <returns>파싱된 데미지 패킷 목록</returns>
    private void ParseData(PacketPayload payloadData)
    {
        lock (_lockObject)
        {
            _buffer.Write(payloadData.Data, 0, payloadData.Data.Length);
            _lastRelSeq = payloadData.RelSeq;
            _lastAt = payloadData.At;

            byte[] data = _buffer.ToArray();

            // 버퍼 크기 관리
            if (data.Length > MAX_BUFFER_SIZE)
            {
                data = TrimBuffer(data);
                _buffer.SetLength(0);
                _buffer.Write(data, 0, data.Length);
            }

            if (data.Length < 50)
                return;

            try
            {
                var allPackets = PacketExtractor.ExtractPackets(data);

                PacketExtractor.SavePacketsToFiles(allPackets, "C:\\Packets", _lastAt, _lastRelSeq);
                int maxProcessedEnd = 0;
                int processedCount = 0;

                logger.Debug($"Found {allPackets.Count} data packets in {data.Length} bytes");
                foreach (
                    var (dataType, dataLen, encodeType, payload, start, end, seq, at) in allPackets
                )
                {
                    // 패킷의 끝 위치
                    maxProcessedEnd = Math.Max(maxProcessedEnd, end);

                    // 관심 있는 패킷만 실제 처리
                    if (!ExceptedDataTypes.Contains(dataType))
                    {
                        //                        logger.Trace($"Skipping unregistered packet type: {dataType} at {start}-{end}");
                        continue;
                    }
                    processedCount++;
                    logger.Debug(
                        $"Processing packet: type={dataType}, len={dataLen}, pos={start}-{end}"
                    );

                    try
                    {
                        if (SkillInfoPacket.TYPE.Contains(dataType))
                        {
                            var skillInfo = SkillInfoPacket.Parse(payload);
                            if (skillInfo.UsedBy == skillInfo.Target)
                            {
                                // 버프 스킬 스킵
                                logger.Debug(
                                    $"Skipping self-targeted skill: {skillInfo.SkillName} (UsedBy: {skillInfo.UsedBy})"
                                );
                                continue;
                            }
                            _skillMatcher.EnqueueSkill(skillInfo, _lastAt);
                        }
                        else if (SkillDamagePacket.TYPE.Contains(dataType))
                        {
                            var damage = SkillDamagePacket.Parse(payload, dataType);
                            if (damage.Damage != 0xFFFFFFFF)
                            {
                                _skillMatcher.EnqueueDamage(damage, _lastAt);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        logger.Error(
                            ex,
                            "Error processing packet type {DataType} at position {Start}-{End}",
                            dataType,
                            start,
                            end
                        );
                        logger.Error(ex.StackTrace);
                    }
                }
                _skillMatcher.CleanupOldSkills(_lastAt);

                // 처리된 패킷들까지만 버퍼에서 제거
                if (maxProcessedEnd > 0)
                {
                    var remainingData = data.AsSpan(maxProcessedEnd).ToArray();
                    _buffer.SetLength(0);
                    _buffer.Write(remainingData, 0, remainingData.Length);
                    logger.Debug(
                        $"Buffer consumed {maxProcessedEnd} bytes, {remainingData.Length} bytes remaining"
                    );
                }
                return;
            }
            catch (Exception ex)
            {
                logger.Error(ex, "Unexpected error processing packets");
                logger.Error(ex.StackTrace);

                // 오류 발생 시 버퍼 일부 정리 (메모리 누수 방지)
                if (data.Length > MAX_BUFFER_SIZE / 2)
                {
                    var trimmedData = TrimBuffer(data);
                    _buffer.SetLength(0);
                    _buffer.Write(trimmedData, 0, trimmedData.Length);
                    logger.Warn(
                        $"Emergency buffer trim due to error: {trimmedData.Length} bytes remaining"
                    );
                }
                return;
            }
        }
    }

    /// <summary>
    /// 버퍼 크기가 너무 클 때 데이터를 잘라냅니다.
    /// </summary>
    /// <param name="data">원본 데이터</param>
    /// <returns>잘라낸 데이터</returns>
    private byte[] TrimBuffer(ReadOnlySpan<byte> data)
    {
        int keepSize = MAX_BUFFER_SIZE / 2;
        var result = data[^keepSize..].ToArray();
        return result;
    }

    /// <summary>
    /// 리소스를 해제합니다.
    /// </summary>
    public void Dispose()
    {
        if (_disposed)
            return;
        try
        {
            Stop();
            _npcapCapture?.StopCapture();
            _buffer.Dispose();
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
