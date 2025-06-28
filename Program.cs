using System.Text;
using NLog;

namespace PacketCapture;

/// <summary>
/// 메인 프로그램 클래스 - 크로스 플랫폼 패킷 캡처 서버 진입점
/// </summary>
class Program
{
    private static readonly Logger logger = LogManager.GetCurrentClassLogger();
    private static StaticWebServer? webServer;
    private static ModernWebSocketServer? webSocketServer;
    private static PacketCaptureManager? captureManager;
    private static CancellationTokenSource? titleChangerCancellation;

    /// <summary>
    /// 명령줄 인수를 파싱하여 패킷 저장 설정을 반환합니다.
    /// </summary>
    /// <param name="args">명령줄 인수</param>
    /// <returns>(패킷저장여부, 저장디렉토리, 난독화복사본여부, 재생모드여부, 재생디렉토리)</returns>
    private static (
        bool savePackets,
        string saveDirectory,
        bool isObfuscatedCopy,
        bool replayMode,
        string replayDirectory
    ) ParseArguments(string[] args)
    {
        bool savePackets = false;
        string saveDirectory = @"C:\Packets";
        bool isObfuscatedCopy = false;
        bool replayMode = false;
        string replayDirectory = string.Empty;

        if (args.Length == 0)
        {
            // 기본값 반환
            return (savePackets, saveDirectory, isObfuscatedCopy, replayMode, replayDirectory);
        }

        // 도움말 표시
        if (args.Contains("--help") || args.Contains("-h"))
        {
            ShowHelp();
            Environment.Exit(0);
        }

        // 인수 파싱
        for (int i = 0; i < args.Length; i++)
        {
            switch (args[i].ToLowerInvariant())
            {
                case "--save-packets":
                case "-s":
                    savePackets = true;
                    break;

                case "--save-directory":
                case "-d":
                    if (i + 1 < args.Length)
                    {
                        saveDirectory = args[i + 1];
                        i++; // 다음 인수는 디렉토리 경로이므로 건너뛰기
                    }
                    else
                    {
                        Console.WriteLine("Error: --save-directory requires a path argument");
                        Environment.Exit(1);
                    }
                    break;

                case "--replay":
                case "-r":
                    replayMode = true;
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                    {
                        replayDirectory = args[i + 1];
                        i++; // 다음 인수는 디렉토리 경로이므로 건너뛰기
                    }
                    else
                    {
                        replayDirectory = saveDirectory; // 기본 저장 디렉토리 사용
                    }
                    break;

                default:
                    // 첫 번째 인수가 파일 경로인 경우 (난독화 관련)
                    if (i == 0 && File.Exists(args[i]))
                    {
                        isObfuscatedCopy = true;
                    }
                    break;
            }
        }

        return (savePackets, saveDirectory, isObfuscatedCopy, replayMode, replayDirectory);
    }

    /// <summary>
    /// 도움말을 표시합니다.
    /// </summary>
    private static void ShowHelp()
    {
        Console.WriteLine("=== Packet Capture Server ===");
        Console.WriteLine("Usage: PacketCapture.exe [options]");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --save-packets, -s           Enable packet saving to disk");
        Console.WriteLine(
            "  --save-directory, -d <path>  Set packet save directory (default: C:\\Packets)"
        );
        Console.WriteLine("  --replay, -r [path]          Replay mode - replay saved packets");
        Console.WriteLine(
            "                                [path]: Directory to replay from (default: same as save directory)"
        );
        Console.WriteLine("  --help, -h                   Show this help message");
        Console.WriteLine();
        Console.WriteLine("Examples:");
        Console.WriteLine(
            "  PacketCapture.exe                                 # Run without packet saving"
        );
        Console.WriteLine(
            "  PacketCapture.exe --save-packets                 # Save packets to C:\\Packets"
        );
        Console.WriteLine(
            "  PacketCapture.exe -s -d \"D:\\MyPackets\"          # Save packets to custom directory"
        );
        Console.WriteLine(
            "  PacketCapture.exe --replay                       # Replay from default directory"
        );
        Console.WriteLine(
            "  PacketCapture.exe -r \"D:\\MyPackets\"             # Replay from custom directory"
        );
        Console.WriteLine();
    }

    static async Task Main(string[] args)
    {
        // 명령줄 인수 파싱
        var (savePackets, saveDirectory, isObfuscatedCopy, replayMode, replayDirectory) =
            ParseArguments(args);

        // 설정 정보 출력
        logger.Info("=== Packet Capture Application Started ===");
        logger.Info($"Configuration:");
        logger.Info($"  - Mode: {(replayMode ? "Replay" : "Capture")}");
        logger.Info($"  - Save Packets: {savePackets}");
        logger.Info($"  - Save Directory: {saveDirectory}");
        if (replayMode)
        {
            logger.Info($"  - Replay Directory: {replayDirectory}");
        }

        if (replayMode)
        {
            await RunReplayModeAsync(replayDirectory);
        }
        else
        {
            await RunCaptureModeAsync(savePackets, saveDirectory, isObfuscatedCopy);
        }
    }

    /// <summary>
    /// 재생 모드를 실행합니다
    /// </summary>
    private static async Task RunReplayModeAsync(string replayDirectory)
    {
        if (!Directory.Exists(replayDirectory))
        {
            logger.Error($"Replay directory does not exist: {replayDirectory}");
            return;
        }

        try
        {
            // 정적 웹 서버 시작
            webServer = new StaticWebServer("*", 8080);
            var webServerTask = webServer.StartAsync();

            // 최신 WebSocket 서버 시작
            webSocketServer = new ModernWebSocketServer("*", 9001);
            var webSocketTask = webSocketServer.StartAsync();

            // SkillMatcher 초기화
            var skillMatcher = new SkillMatcher();
            skillMatcher.OnDamageMatched += async (damage) =>
            {
                if (damage.SkillName == "")
                {
                    damage.SkillName = damage.Flags.GenerateSkillName();
                }
                logger.Info(
                    $"[Replay][{damage.SkillName}] Damage: {damage.Damage} (UsedBy: {damage.UsedBy}, Target: {damage.Target})"
                );
                await webSocketServer.BroadcastMessageAsync(damage.ToLog());
            };

            // 패킷 재생 매니저 시작
            using var replayManager = new PacketReplayManager(
                replayDirectory,
                webSocketServer,
                skillMatcher
            );

            logger.Info("=== Packet Replay Mode Started ===");
            logger.Info("📡 WebSocket server: ws://0.0.0.0:9001 (all interfaces)");
            logger.Info("🌐 Static web server: http://0.0.0.0:8080 (all interfaces)");
            logger.Info($"📂 Replay directory: {replayDirectory}");
            logger.Info("Available commands:");
            logger.Info("  'today' - Replay today's sessions");
            logger.Info("  'yesterday' - Replay yesterday's sessions");
            logger.Info("  'date YYYY-MM-DD' - Replay specific date");
            logger.Info("  'range YYYY-MM-DD YYYY-MM-DD' - Replay date range");
            logger.Info("  'list YYYY-MM-DD' - List sessions for date");
            logger.Info("  'stop' - Stop current replay");
            logger.Info("  'quit' - Exit application");

            while (true)
            {
                Console.Write("replay> ");
                var input = Console.ReadLine()?.Trim().ToLowerInvariant();

                if (string.IsNullOrEmpty(input))
                    continue;

                var parts = input.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                var command = parts[0];

                try
                {
                    switch (command)
                    {
                        case "today":
                            await replayManager.ReplayDateRangeAsync(
                                DateTime.Today,
                                DateTime.Today
                            );
                            break;

                        case "yesterday":
                            var yesterday = DateTime.Today.AddDays(-1);
                            await replayManager.ReplayDateRangeAsync(yesterday, yesterday);
                            break;

                        case "date":
                            if (parts.Length > 1 && DateTime.TryParse(parts[1], out var date))
                            {
                                await replayManager.ReplayDateRangeAsync(date.Date, date.Date);
                            }
                            else
                            {
                                Console.WriteLine("Invalid date format. Use: date YYYY-MM-DD");
                            }
                            break;

                        case "range":
                            if (
                                parts.Length > 2
                                && DateTime.TryParse(parts[1], out var startDate)
                                && DateTime.TryParse(parts[2], out var endDate)
                            )
                            {
                                await replayManager.ReplayDateRangeAsync(
                                    startDate.Date,
                                    endDate.Date
                                );
                            }
                            else
                            {
                                Console.WriteLine(
                                    "Invalid date format. Use: range YYYY-MM-DD YYYY-MM-DD"
                                );
                            }
                            break;

                        case "list":
                            if (parts.Length > 1 && DateTime.TryParse(parts[1], out var listDate))
                            {
                                var sessions = await replayManager.GetSessionsAsync(listDate.Date);
                                Console.WriteLine(
                                    $"Found {sessions.Count} sessions for {listDate:yyyy-MM-dd}:"
                                );
                                foreach (var session in sessions)
                                {
                                    Console.WriteLine(
                                        $"  {session.Timestamp:HH:mm:ss} - {session.PacketCount} packets (seq: {session.SequenceNumber})"
                                    );
                                }
                            }
                            else
                            {
                                Console.WriteLine("Invalid date format. Use: list YYYY-MM-DD");
                            }
                            break;

                        case "stop":
                            replayManager.Stop();
                            Console.WriteLine("Replay stopped.");
                            break;

                        case "quit":
                        case "exit":
                            logger.Info("Exit requested by user");
                            return;

                        default:
                            Console.WriteLine("Unknown command. Type 'quit' to exit.");
                            break;
                    }
                }
                catch (Exception ex)
                {
                    logger.Error(ex, $"Error executing command: {command}");
                    Console.WriteLine($"Error: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error in replay mode");
        }
        finally
        {
            await StopAllServersAsync();
        }
    }

    /// <summary>
    /// 캡처 모드를 실행합니다 (기존 코드)
    /// </summary>
    private static async Task RunCaptureModeAsync(
        bool savePackets,
        string saveDirectory,
        bool isObfuscatedCopy
    )
    {
        // 임시 파일 정리 시스템 초기화
        BinaryObfuscator.RegisterCleanup();

        // 프로그램 시작 시 바이너리 난독화 (첫 실행이 아닌 경우 스킵)
        isObfuscatedCopy = true;
        if (!isObfuscatedCopy)
        {
            // 원본 실행: 노이즈가 추가된 복사본 생성 후 실행
            string currentPath =
                Environment.ProcessPath
                ?? Path.Combine(
                    AppContext.BaseDirectory,
                    Path.GetFileName(System.Reflection.Assembly.GetExecutingAssembly().Location)
                );

            if (BinaryObfuscator.TryCreateObfuscatedCopy(currentPath))
            {
                // 복사본이 성공적으로 생성되고 실행되었으므로 원본은 종료
                Environment.Exit(0);
            }
            // 복사 실패 시 원본이 계속 실행됨
        }

        // 콘솔 타이틀 랜덤화 시작
        titleChangerCancellation = new CancellationTokenSource();
        BinaryObfuscator.StartRandomTitleChanger(titleChangerCancellation.Token);

        // NLog 설정
        var config = new NLog.Config.LoggingConfiguration();
        var fileTarget = new NLog.Targets.FileTarget("fileTarget")
        {
            FileName = "packetCapture.log",
            Encoding = Encoding.UTF8,
            Layout = "${longdate} [${level:uppercase=true}] ${message}",
        };
        var consoleTarget = new NLog.Targets.ConsoleTarget("consoleTarget")
        {
            Layout = "${longdate} [${level:uppercase=true}] ${message}",
        };

        config.AddRule(LogLevel.Info, LogLevel.Fatal, fileTarget);
        config.AddRule(LogLevel.Info, LogLevel.Fatal, consoleTarget);
        LogManager.Configuration = config;

        try
        {
            // 정적 웹 서버 시작
            webServer = new StaticWebServer("*", 8080);
            var webServerTask = webServer.StartAsync();

            // 최신 WebSocket 서버 시작
            webSocketServer = new ModernWebSocketServer("*", 9001);
            var webSocketTask = webSocketServer.StartAsync();

            // 패킷 캡처 매니저 시작
            captureManager = new PacketCaptureManager(savePackets, saveDirectory);
            await captureManager.StartAsync(webSocketServer);

            logger.Info("=== Cross-Platform Packet Capture Server Started ===");
            logger.Info("📡 WebSocket server: ws://0.0.0.0:9001 (all interfaces)");
            logger.Info("🌐 Static web server: http://0.0.0.0:8080 (all interfaces)");
            logger.Info("📦 Packet capture: monitoring port 16000 (multi-interface)");
            logger.Info("✨ Using libpcap/npcap cross-platform packet capture");
            logger.Info("🖥️  Platform: " + Environment.OSVersion.Platform);
            logger.Info("🌍 Network access: Available from any network interface");
            logger.Info("🔒 Security: Binary obfuscation and title randomization active");

            // 패킷 저장 설정 표시
            if (savePackets)
            {
                logger.Info($"💾 Packet saving: ENABLED → {saveDirectory}");
            }
            else
            {
                logger.Info("💾 Packet saving: DISABLED");
            }

            logger.Info("=====================================================");
            logger.Info("Press any key to stop all servers...");

            // Ctrl+C 핸들러 추가
            Console.CancelKeyPress += async (sender, e) =>
            {
                e.Cancel = true;
                logger.Info("Shutdown signal received...");
                await StopAllServersAsync();
            };

            Console.ReadKey();
            await StopAllServersAsync();
        }
        catch (Exception ex)
        {
            logger.Error($"Error starting servers: {ex.Message}");
        }
    }

    private static async Task StopAllServersAsync()
    {
        try
        {
            logger.Info("Stopping all servers...");

            // 보안 기능 중지
            titleChangerCancellation?.Cancel();

            captureManager?.Stop();
            webServer?.Stop();
            if (webSocketServer != null)
            {
                await webSocketServer.StopAsync();
            }

            logger.Info("All servers stopped successfully.");
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error stopping servers");
        }
    }
}
