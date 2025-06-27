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

    static async Task Main(string[] args)
    {
        // 임시 파일 정리 시스템 초기화
        BinaryObfuscator.RegisterCleanup();

        // 프로그램 시작 시 바이너리 난독화 (첫 실행이 아닌 경우 스킵)
        bool isObfuscatedCopy = args.Length > 0 && File.Exists(args[0]);
        //isObfuscatedCopy = true;
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
            webServer = new StaticWebServer(8080);
            var webServerTask = webServer.StartAsync();

            // 최신 WebSocket 서버 시작
            webSocketServer = new ModernWebSocketServer(9001);
            var webSocketTask = webSocketServer.StartAsync();

            // 패킷 캡처 매니저 시작
            captureManager = new PacketCaptureManager();
            await captureManager.StartAsync(webSocketServer);

            logger.Info("=== Cross-Platform Packet Capture Server Started ===");
            logger.Info("📡 WebSocket server: ws://0.0.0.0:9001 (all interfaces)");
            logger.Info("🌐 Static web server: http://0.0.0.0:8080 (all interfaces)");
            logger.Info("📦 Packet capture: monitoring port 16000 (multi-interface)");
            logger.Info("✨ Using libpcap/npcap cross-platform packet capture");
            logger.Info("🖥️  Platform: " + Environment.OSVersion.Platform);
            logger.Info("🌍 Network access: Available from any network interface");
            logger.Info("🔒 Security: Binary obfuscation and title randomization active");
            logger.Info("=====================================================");
            logger.Info("Press any key to stop all servers..."); // Ctrl+C 핸들러 추가
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
