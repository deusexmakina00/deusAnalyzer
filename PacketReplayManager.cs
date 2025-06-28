using NLog;

namespace PacketCapture;

/// <summary>
/// 패킷 재생을 관리하고 실제 캡처 시스템과 통합하는 클래스
/// </summary>
public class PacketReplayManager : IDisposable
{
    private static readonly Logger logger = LogManager.GetCurrentClassLogger();

    private readonly PacketPlayer _player;
    private readonly ModernWebSocketServer? _webSocketServer;
    private readonly SkillMatcher? _skillMatcher;
    private bool _disposed;

    public PacketReplayManager(
        string packetDirectory,
        ModernWebSocketServer? webSocketServer = null,
        SkillMatcher? skillMatcher = null
    )
    {
        _player = new PacketPlayer(packetDirectory);
        _webSocketServer = webSocketServer;
        _skillMatcher = skillMatcher;

        // 이벤트 구독
        _player.OnPacketReplayed += OnPacketReplayed;
        _player.OnPlaybackStateChanged += OnPlaybackStateChanged;

        logger.Info($"PacketReplayManager initialized with directory: {packetDirectory}");
    }

    /// <summary>
    /// 지정된 날짜의 세션 목록을 가져옵니다
    /// </summary>
    public async Task<List<PacketSessionInfo>> GetSessionsAsync(DateTime date)
    {
        return await _player.GetPacketSessionsAsync(date);
    }

    /// <summary>
    /// 세션을 재생하여 실시간 캡처처럼 처리합니다
    /// </summary>
    public async Task ReplaySessionAsync(PacketSessionInfo session, double speed = 1.0)
    {
        logger.Info(
            $"Starting replay of session from {session.Timestamp:yyyy-MM-dd HH:mm:ss} with {session.PacketCount} packets"
        );

        await _player.PlaySessionAsync(session, speed, ProcessReplayedPacket);
    }

    /// <summary>
    /// 여러 세션을 연속으로 재생합니다
    /// </summary>
    public async Task ReplayMultipleSessionsAsync(
        List<PacketSessionInfo> sessions,
        double speed = 1.0
    )
    {
        logger.Info($"Starting replay of {sessions.Count} sessions");

        await _player.PlayMultipleSessionsAsync(sessions, speed, ProcessReplayedPacket);
    }

    /// <summary>
    /// 특정 기간의 모든 세션을 재생합니다
    /// </summary>
    public async Task ReplayDateRangeAsync(DateTime startDate, DateTime endDate, double speed = 1.0)
    {
        var allSessions = new List<PacketSessionInfo>();

        for (var date = startDate.Date; date <= endDate.Date; date = date.AddDays(1))
        {
            var dailySessions = await GetSessionsAsync(date);
            allSessions.AddRange(dailySessions);
        }

        if (allSessions.Count == 0)
        {
            logger.Warn(
                $"No sessions found for date range {startDate:yyyy-MM-dd} to {endDate:yyyy-MM-dd}"
            );
            return;
        }

        logger.Info($"Found {allSessions.Count} sessions in date range");
        await ReplayMultipleSessionsAsync(allSessions, speed);
    }

    /// <summary>
    /// 재생을 중지합니다
    /// </summary>
    public void Stop()
    {
        _player.Stop();
        logger.Info("Replay stopped");
    }

    /// <summary>
    /// 재생된 패킷을 처리합니다 (실제 캡처와 동일하게)
    /// </summary>
    private async Task ProcessReplayedPacket(ExtractedPacket packet)
    {
        try
        {
            // SkillMatcher에 패킷 전달 (타입에 따라)
            if (_skillMatcher != null)
            {
                if (SkillActionPacket.TYPE.Contains(packet.DataType))
                {
                    var skillAction = SkillActionPacket.Parse(packet.Payload);
                    if (skillAction.UsedBy != skillAction.Target) // 버프 스킬 제외
                    {
                        _skillMatcher.EnqueueSkillAction(skillAction, packet.At);
                    }
                }
                else if (SkillInfoPacket.TYPE.Contains(packet.DataType))
                {
                    var skillInfo = SkillInfoPacket.Parse(packet.Payload);
                    if (skillInfo.UsedBy != skillInfo.Target) // 버프 스킬 제외
                    {
                        _skillMatcher.EnqueueSkillInfo(skillInfo, packet.At);
                    }
                }
                else if (SkillDamagePacket.TYPE.Contains(packet.DataType))
                {
                    var damage = SkillDamagePacket.Parse(packet.Payload, packet.DataType);
                    if (damage.Damage != 0xFFFFFFFF)
                    {
                        _skillMatcher.EnqueueDamage(damage, packet.At);
                    }
                }
                else if (ChangeHpPacket.TYPE.Contains(packet.DataType))
                {
                    var changeHp = ChangeHpPacket.Parse(packet.Payload);
                    _skillMatcher.EnqueueHpChange(changeHp, packet.At);
                }
                else if (SkillStatePacket.TYPE.Contains(packet.DataType))
                {
                    var skillState = SkillStatePacket.Parse(packet.Payload);
                    _skillMatcher.EnqueueSkillState(skillState, packet.At);
                }
            }
        }
        catch (Exception ex)
        {
            logger.Error(ex, $"Error processing replayed packet type {packet.DataType}");
        }
    }

    /// <summary>
    /// 재생된 패킷 이벤트 처리
    /// </summary>
    private void OnPacketReplayed(ReplayPacketInfo replayInfo)
    {
        var packet = replayInfo.Packet;
        var progress = (double)replayInfo.CurrentIndex / replayInfo.TotalCount * 100;

        logger.Debug(
            $"Replaying packet {replayInfo.CurrentIndex + 1}/{replayInfo.TotalCount} "
                + $"({progress:F1}%) - Type: 0x{packet.DataType:X8}, Size: {packet.DataLength}"
        );
    }

    /// <summary>
    /// 재생 상태 변경 이벤트 처리
    /// </summary>
    private void OnPlaybackStateChanged(PlaybackState state)
    {
        logger.Info($"Playback state changed to: {state}");

        // WebSocket을 통해 재생 상태를 클라이언트에 알림
        if (_webSocketServer != null)
        {
            var message = new
            {
                Type = "PlaybackState",
                State = state.ToString(),
                Timestamp = DateTime.Now,
            };

            _ = Task.Run(async () =>
            {
                try
                {
                    await _webSocketServer.BroadcastMessageAsync(
                        System.Text.Json.JsonSerializer.Serialize(message)
                    );
                }
                catch (Exception ex)
                {
                    logger.Error(ex, "Failed to broadcast playback state");
                }
            });
        }
    }

    public void Dispose()
    {
        if (_disposed)
            return;

        try
        {
            Stop();
            _player?.Dispose();
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
