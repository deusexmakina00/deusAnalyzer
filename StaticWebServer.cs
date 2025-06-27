using System.Collections.Frozen;
using System.Net;
using System.Text;
using NLog;

namespace PacketCapture;

/// <summary>
/// 현대적인 정적 웹 서버 구현
/// 최신 C# 기능과 비동기 패턴을 활용한 고성능 정적 파일 서버
/// </summary>
public sealed class StaticWebServer
{
    private static readonly Logger logger = LogManager.GetCurrentClassLogger();
    private readonly string host;
    private readonly int port;
    private readonly string webRootPath;
    private readonly FrozenDictionary<string, string> mimeTypes;
    private HttpListener? httpListener;
    private volatile bool isRunning;

    /// <summary>
    /// StaticWebServer 인스턴스를 초기화합니다.
    /// </summary>
    /// <param name="host">서버가 연결가능한 host</param>
    /// <param name="port">서버가 수신할 포트 번호</param>
    /// <param name="webRootPath">웹 루트 경로 (null인 경우 자동 설정)</param>
    /// <exception cref="ArgumentOutOfRangeException">포트가 유효하지 않은 경우</exception>
    public StaticWebServer(string? host = "localhost", int port = 3000, string? webRootPath = null)
    {
        if (port is <= 0 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(port), "포트는 1-65535 범위여야 합니다.");

        this.host = host;
        this.port = port;
        this.webRootPath = webRootPath ?? GetDefaultWebRootPath();
        this.mimeTypes = CreateMimeTypeDictionary();
    }

    /// <summary>
    /// 기본 웹 루트 경로를 결정합니다.
    /// </summary>
    private static string GetDefaultWebRootPath()
    {
        string currentDir = AppDomain.CurrentDomain.BaseDirectory;
        var dirInfo = new DirectoryInfo(currentDir);
        /*
        // 상위 디렉토리를 찾아가면서 .csproj 파일이 있는 디렉토리를 찾기
        while (dirInfo?.Parent != null)
        {
            if (Directory.GetFiles(dirInfo.FullName, "*.csproj").Length > 0)
            {
                return Path.Combine(dirInfo.FullName, "wwwroot");
            }
            dirInfo = dirInfo.Parent;
        }
*/
        Console.WriteLine(
            $"Current Directory: {currentDir}, Parent: {dirInfo.Parent?.FullName}, Grandparent: {dirInfo.Parent?.Parent?.FullName}"
        );

        // 기본 경로로 시도
        var projectRoot = currentDir;
        return Path.Combine(projectRoot, "wwwroot");
    }

    /// <summary>
    /// MIME 타입 딕셔너리를 생성합니다.
    /// </summary>
    private static FrozenDictionary<string, string> CreateMimeTypeDictionary() =>
        new Dictionary<string, string>
        {
            [".html"] = "text/html; charset=utf-8",
            [".htm"] = "text/html; charset=utf-8",
            [".css"] = "text/css",
            [".js"] = "application/javascript",
            [".json"] = "application/json",
            [".png"] = "image/png",
            [".jpg"] = "image/jpeg",
            [".jpeg"] = "image/jpeg",
            [".gif"] = "image/gif",
            [".svg"] = "image/svg+xml",
            [".ico"] = "image/x-icon",
            [".txt"] = "text/plain",
            [".xml"] = "application/xml",
            [".pdf"] = "application/pdf",
            [".zip"] = "application/zip",
            [".woff"] = "font/woff",
            [".woff2"] = "font/woff2",
            [".ttf"] = "font/ttf",
            [".eot"] = "application/vnd.ms-fontobject",
        }.ToFrozenDictionary();

    /// <summary>
    /// 정적 웹 서버를 비동기적으로 시작합니다.
    /// </summary>
    public async Task StartAsync()
    {
        try
        {
            EnsureWebRootExists();

            httpListener = new HttpListener();
            httpListener.Prefixes.Add($"http://{host}:{port}/");
            httpListener.Start();
            isRunning = true;

            logger.Info("Static web server started on http://{Host}:{Port}/", host, port);
            logger.Info("Serving files from: {WebRootPath}", webRootPath);
            logger.Debug(
                "AppDomain.CurrentDomain.BaseDirectory: {BaseDirectory}",
                AppDomain.CurrentDomain.BaseDirectory
            );
            logger.Debug("wwwroot exists: {Exists}", Directory.Exists(webRootPath));

            await ProcessRequestsAsync().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error starting static web server");
            throw;
        }
    }

    /// <summary>
    /// 웹 루트 디렉토리가 존재하는지 확인하고 필요시 생성합니다.
    /// </summary>
    private void EnsureWebRootExists()
    {
        if (!Directory.Exists(webRootPath))
        {
            Directory.CreateDirectory(webRootPath!);
            CreateDefaultFiles();
        }
    }

    /// <summary>
    /// 요청을 처리하는 메인 루프입니다.
    /// </summary>
    private async Task ProcessRequestsAsync()
    {
        while (isRunning)
        {
            try
            {
                var context = await httpListener!.GetContextAsync().ConfigureAwait(false);
                _ = Task.Run(async () => await ProcessRequestAsync(context).ConfigureAwait(false));
            }
            catch (HttpListenerException)
            {
                // HttpListener가 중지될 때 발생
                break;
            }
            catch (ObjectDisposedException)
            {
                // HttpListener가 disposed될 때 발생
                break;
            }
        }
    }

    /// <summary>
    /// 서버를 중지합니다.
    /// </summary>
    public void Stop()
    {
        isRunning = false;
        httpListener?.Stop();
        httpListener?.Close();
        logger.Info("Static web server stopped");
    }

    /// <summary>
    /// 개별 HTTP 요청을 처리합니다.
    /// </summary>
    private async Task ProcessRequestAsync(HttpListenerContext context)
    {
        try
        {
            var request = context.Request;
            var response = context.Response;

            // URL 디코딩 및 경로 정리
            string requestedPath = Uri.UnescapeDataString(request.Url?.AbsolutePath ?? "/");

            // 보안: 상위 디렉토리 접근 방지
            if (requestedPath.Contains("..", StringComparison.Ordinal))
            {
                await SendErrorResponseAsync(response, 403, "Forbidden").ConfigureAwait(false);
                return;
            }

            // 기본 파일 설정
            if (requestedPath is "/" || requestedPath.EndsWith('/'))
            {
                requestedPath += "index.html";
            }

            // 파일 경로 계산
            string filePath = Path.Combine(webRootPath, requestedPath.TrimStart('/'));

            logger.Debug(
                "Request: {HttpMethod} {RequestedPath} -> {FilePath}",
                request.HttpMethod,
                requestedPath,
                filePath
            );

            if (File.Exists(filePath))
            {
                await ServeFileAsync(response, filePath).ConfigureAwait(false);
            }
            else
            {
                await SendErrorResponseAsync(response, 404, "Not Found").ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error processing request");
            try
            {
                await SendErrorResponseAsync(context.Response, 500, "Internal Server Error")
                    .ConfigureAwait(false);
            }
            catch (Exception innerEx)
            {
                logger.Error(innerEx, "Error sending error response");
            }
        }
    }

    /// <summary>
    /// 파일을 클라이언트에게 제공합니다.
    /// </summary>
    private async Task ServeFileAsync(HttpListenerResponse response, string filePath)
    {
        try
        {
            var fileInfo = new FileInfo(filePath);
            var fileExtension = fileInfo.Extension.ToLowerInvariant();

            // MIME 타입 설정
            response.ContentType = mimeTypes.TryGetValue(fileExtension, out string? mimeType)
                ? mimeType
                : "application/octet-stream";

            // 캐시 헤더 설정
            response.Headers.Add("Cache-Control", "public, max-age=3600");
            response.Headers.Add("Last-Modified", fileInfo.LastWriteTimeUtc.ToString("R"));

            // 파일 읽기 및 전송
            byte[] fileBytes = await File.ReadAllBytesAsync(filePath).ConfigureAwait(false);
            response.ContentLength64 = fileBytes.Length;
            response.StatusCode = 200;

            await response.OutputStream.WriteAsync(fileBytes).ConfigureAwait(false);
            response.OutputStream.Close();

            logger.Debug("Served file: {FilePath} ({FileSize} bytes)", filePath, fileBytes.Length);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error serving file {FilePath}", filePath);
            await SendErrorResponseAsync(response, 500, "Internal Server Error")
                .ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 오류 응답을 클라이언트에게 전송합니다.
    /// </summary>
    private async Task SendErrorResponseAsync(
        HttpListenerResponse response,
        int statusCode,
        string statusDescription
    )
    {
        try
        {
            response.StatusCode = statusCode;
            response.StatusDescription = statusDescription;
            response.ContentType = "text/html; charset=utf-8";

            string errorHtml = GenerateErrorHtml(statusCode, statusDescription);
            byte[] errorBytes = Encoding.UTF8.GetBytes(errorHtml);
            response.ContentLength64 = errorBytes.Length;

            await response.OutputStream.WriteAsync(errorBytes).ConfigureAwait(false);
            response.OutputStream.Close();
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error sending error response");
        }
    }

    /// <summary>
    /// 오류 페이지 HTML을 생성합니다.
    /// </summary>
    private static string GenerateErrorHtml(int statusCode, string statusDescription) =>
        $$"""
            <!DOCTYPE html>
            <html>
            <head>
                <title>{{statusCode}} {{statusDescription}}</title>
                <style>
                    body { font-family: Arial, sans-serif; margin: 40px; }
                    h1 { color: #d32f2f; }
                </style>
            </head>
            <body>
                <h1>{{statusCode}} {{statusDescription}}</h1>
                <p>The requested resource could not be found or accessed.</p>
                <hr>
                <p><em>Packet Capture Static Web Server</em></p>
            </body>
            </html>
            """;

    private void CreateDefaultFiles()
    {
        try
        {
            // 기본 index.html 파일 생성
            string indexPath = Path.Combine(webRootPath, "index.html");
            if (!File.Exists(indexPath))
            {
                string defaultHtml =
                    @"<!DOCTYPE html>
<html lang=""ko"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <title>Packet Capture Web Server</title>
    <style>
        body {
            font-family: 'Segoe UI', Tahoma, Geneva, Verdana, sans-serif;
            max-width: 800px;
            margin: 0 auto;
            padding: 20px;
            background-color: #f5f5f5;
        }
        .container {
            background: white;
            padding: 30px;
            border-radius: 10px;
            box-shadow: 0 2px 10px rgba(0,0,0,0.1);
        }
        h1 {
            color: #2c3e50;
            text-align: center;
        }
        .status {
            background: #e8f5e8;
            border: 1px solid #27ae60;
            border-radius: 5px;
            padding: 15px;
            margin: 20px 0;
        }
        .info {
            background: #e3f2fd;
            border: 1px solid #2196f3;
            border-radius: 5px;
            padding: 15px;
            margin: 20px 0;
        }
        .code {
            background: #f8f9fa;
            border: 1px solid #dee2e6;
            border-radius: 5px;
            padding: 10px;
            font-family: 'Courier New', monospace;
            margin: 10px 0;
        }
    </style>
</head>
<body>
    <div class=""container"">
        <h1>🚀 Packet Capture Web Server</h1>
        
        <div class=""status"">
            <h3>✅ 서버가 정상적으로 실행중입니다!</h3>
            <p>포트 3000에서 정적 웹 서버가 실행되고 있습니다.</p>
        </div>

        <div class=""info"">
            <h3>📡 WebSocket 연결</h3>
            <p>패킷 캡처 데이터를 실시간으로 받으려면 WebSocket에 연결하세요:</p>
            <div class=""code"">ws://localhost:8000/</div>
        </div>

        <div class=""info"">
            <h3>📁 파일 서비스</h3>
            <p>wwwroot 폴더에 파일을 추가하면 이 웹 서버를 통해 제공됩니다.</p>
            <ul>
                <li>HTML, CSS, JavaScript 파일</li>
                <li>이미지 파일 (PNG, JPG, GIF, SVG)</li>
                <li>폰트 파일 (WOFF, WOFF2, TTF)</li>
                <li>기타 정적 리소스</li>
            </ul>
        </div>

        <div class=""info"">
            <h3>🛠️ 사용 가능한 서비스</h3>
            <ul>
                <li><strong>패킷 캡처:</strong> 포트 16000에서 TCP 패킷 모니터링</li>
                <li><strong>WebSocket 서버:</strong> 포트 8000에서 실시간 데이터 전송</li>
                <li><strong>정적 웹 서버:</strong> 포트 3000에서 웹 파일 서비스</li>
            </ul>
        </div>
    </div>

    <script>
        // WebSocket 연결 테스트 (선택사항)
        console.log('Packet Capture Web Server - Ready!');
        
        // WebSocket 연결 예제
        /*
        const ws = new WebSocket('ws://localhost:8000/');
        ws.onopen = function() {
            console.log('WebSocket Connected');
        };
        ws.onmessage = function(event) {
            console.log('Packet data:', event.data);
        };
        */
    </script>
</body>
</html>";

                File.WriteAllText(indexPath, defaultHtml, Encoding.UTF8);
                logger.Info($"Created default index.html at {indexPath}");
            }
        }
        catch (Exception ex)
        {
            logger.Error($"Error creating default files: {ex.Message}");
        }
    }
}
