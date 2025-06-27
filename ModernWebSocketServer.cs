using System.Collections.Concurrent;
using System.Net;
using System.Net.WebSockets;
using System.Text;
using NLog;

namespace PacketCapture;

/// <summary>
/// 현대적인 WebSocket 서버 구현
/// 최신 C# 기능과 비동기 패턴을 활용한 고성능 WebSocket 서버
/// </summary>
public sealed class ModernWebSocketServer
{
    private static readonly Logger logger = LogManager.GetCurrentClassLogger();
    private readonly int port;
    private readonly ConcurrentDictionary<string, WebSocket> clients = [];
    private HttpListener? httpListener;
    private CancellationTokenSource? cancellationTokenSource;
    private volatile bool isRunning;

    /// <summary>
    /// ModernWebSocketServer 인스턴스를 초기화합니다.
    /// </summary>
    /// <param name="port">서버가 수신할 포트 번호</param>
    /// <exception cref="ArgumentOutOfRangeException">포트가 유효하지 않은 경우</exception>
    public ModernWebSocketServer(int port)
    {
        if (port is <= 0 or > 65535)
            throw new ArgumentOutOfRangeException(nameof(port), "포트는 1-65535 범위여야 합니다.");

        this.port = port;
    }

    /// <summary>
    /// 연결된 클라이언트 수를 반환합니다.
    /// </summary>
    public int ClientCount => clients.Count;

    /// <summary>
    /// 서버가 실행 중인지 확인합니다.
    /// </summary>
    public bool IsRunning => isRunning;

    /// <summary>
    /// WebSocket 서버를 비동기적으로 시작합니다.
    /// </summary>
    /// <returns>서버 시작 작업을 나타내는 Task</returns>
    public async Task StartAsync()
    {
        try
        {
            cancellationTokenSource = new CancellationTokenSource();
            httpListener = new HttpListener();
            httpListener.Prefixes.Add($"http://*:{port}/");

            httpListener.Start();
            isRunning = true;

            logger.Info(
                "WebSocket server started on port {Port} (listening on all interfaces)",
                port
            );

            await ProcessIncomingConnections().ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error starting WebSocket server");
            throw;
        }
    }

    /// <summary>
    /// 들어오는 연결을 처리합니다.
    /// </summary>
    private async Task ProcessIncomingConnections()
    {
        ArgumentNullException.ThrowIfNull(cancellationTokenSource);
        ArgumentNullException.ThrowIfNull(httpListener);

        while (isRunning && !cancellationTokenSource.Token.IsCancellationRequested)
        {
            try
            {
                var context = await httpListener.GetContextAsync().ConfigureAwait(false);
                _ = context.Request.IsWebSocketRequest
                    ? ProcessWebSocketRequestAsync(context)
                    : RejectNonWebSocketRequestAsync(context);
            }
            catch (Exception ex) when (isRunning)
            {
                logger.Error(ex, "Error accepting WebSocket connection");
            }
        }
    }

    /// <summary>
    /// WebSocket이 아닌 요청을 거부합니다.
    /// </summary>
    /// <param name="context">HTTP 컨텍스트</param>
    private static async Task RejectNonWebSocketRequestAsync(HttpListenerContext context)
    {
        context.Response.StatusCode = 400;
        await using var writer = new StreamWriter(context.Response.OutputStream);
        await writer.WriteAsync("WebSocket connection required").ConfigureAwait(false);
        context.Response.Close();
    }

    /// <summary>
    /// WebSocket 요청을 처리합니다.
    /// </summary>
    /// <param name="context">HTTP 컨텍스트</param>
    private async Task ProcessWebSocketRequestAsync(HttpListenerContext context)
    {
        var clientId = Guid.NewGuid().ToString();

        try
        {
            var webSocketContext = await context
                .AcceptWebSocketAsync(subProtocol: null)
                .ConfigureAwait(false);
            var webSocket = webSocketContext.WebSocket;

            if (clients.TryAdd(clientId, webSocket))
            {
                logger.Info("WebSocket client connected: {ClientId}", clientId);
                await HandleWebSocketClientAsync(webSocket, clientId).ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error processing WebSocket request for client {ClientId}", clientId);
        }
        finally
        {
            if (clients.TryRemove(clientId, out _))
            {
                logger.Info("WebSocket client disconnected: {ClientId}", clientId);
            }
        }
    }

    /// <summary>
    /// WebSocket 클라이언트를 처리합니다.
    /// </summary>
    /// <param name="webSocket">WebSocket 연결</param>
    /// <param name="clientId">클라이언트 ID</param>
    private async Task HandleWebSocketClientAsync(WebSocket webSocket, string clientId)
    {
        var buffer = new byte[4096];

        try
        {
            while (webSocket.State == WebSocketState.Open && isRunning)
            {
                var result = await webSocket
                    .ReceiveAsync(
                        buffer.AsMemory(),
                        cancellationTokenSource?.Token ?? CancellationToken.None
                    )
                    .ConfigureAwait(false);

                switch (result.MessageType)
                {
                    case WebSocketMessageType.Text:
                        await HandleTextMessageAsync(webSocket, buffer, result.Count, clientId)
                            .ConfigureAwait(false);
                        break;
                    case WebSocketMessageType.Close:
                        return;
                    case WebSocketMessageType.Binary:
                        logger.Debug("Binary message received from client {ClientId}", clientId);
                        break;
                }
            }
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error handling WebSocket client {ClientId}", clientId);
        }
        finally
        {
            await CloseWebSocketSafelyAsync(webSocket).ConfigureAwait(false);
        }
    }

    /// <summary>
    /// 텍스트 메시지를 처리합니다.
    /// </summary>
    private async Task HandleTextMessageAsync(
        WebSocket webSocket,
        byte[] buffer,
        int count,
        string clientId
    )
    {
        var message = Encoding.UTF8.GetString(buffer.AsSpan(0, count));
        logger.Debug("Received message from client {ClientId}: {Message}", clientId, message);

        // Echo back for testing (modern pattern matching)
        if (message.Contains("ping", StringComparison.OrdinalIgnoreCase))
        {
            await SendMessageToClientAsync(webSocket, "pong").ConfigureAwait(false);
        }
    }

    /// <summary>
    /// WebSocket을 안전하게 닫습니다.
    /// </summary>
    private static async Task CloseWebSocketSafelyAsync(WebSocket webSocket)
    {
        try
        {
            if (webSocket.State == WebSocketState.Open)
            {
                await webSocket
                    .CloseAsync(
                        WebSocketCloseStatus.NormalClosure,
                        "Connection closed",
                        CancellationToken.None
                    )
                    .ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            LogManager.GetCurrentClassLogger().Error(ex, "Error closing WebSocket safely");
        }
    }

    /// <summary>
    /// 특정 클라이언트에게 메시지를 전송합니다.
    /// </summary>
    /// <param name="webSocket">WebSocket 연결</param>
    /// <param name="message">전송할 메시지</param>
    private static async Task SendMessageToClientAsync(WebSocket webSocket, string message)
    {
        try
        {
            if (webSocket.State == WebSocketState.Open)
            {
                var buffer = Encoding.UTF8.GetBytes(message);
                await webSocket
                    .SendAsync(
                        buffer.AsMemory(),
                        WebSocketMessageType.Text,
                        endOfMessage: true,
                        CancellationToken.None
                    )
                    .ConfigureAwait(false);
            }
        }
        catch (Exception ex)
        {
            LogManager.GetCurrentClassLogger().Error(ex, "Error sending message to client");
        }
    }

    /// <summary>
    /// 모든 연결된 클라이언트에게 메시지를 브로드캐스트합니다.
    /// </summary>
    /// <param name="message">브로드캐스트할 메시지</param>
    public async Task BroadcastMessageAsync(string message)
    {
        ArgumentException.ThrowIfNullOrEmpty(message);

        var clientsToRemove = new List<string>();
        var tasks = new List<Task>();

        foreach (var (clientId, webSocket) in clients)
        {
            if (webSocket.State == WebSocketState.Open)
            {
                tasks.Add(SendMessageToClientAsync(webSocket, message));
            }
            else
            {
                clientsToRemove.Add(clientId);
            }
        }

        // Send messages concurrently
        try
        {
            await Task.WhenAll(tasks).ConfigureAwait(false);
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error during broadcast operation");
        }

        // Remove disconnected clients
        foreach (var clientId in clientsToRemove)
        {
            if (clients.TryRemove(clientId, out _))
            {
                logger.Debug("Removed disconnected client: {ClientId}", clientId);
            }
        }
    }

    /// <summary>
    /// 서버를 안전하게 중지합니다.
    /// </summary>
    public async Task StopAsync()
    {
        try
        {
            isRunning = false;
            cancellationTokenSource?.Cancel();

            // Close all client connections concurrently
            var closeTasks = clients
                .Values.Where(ws => ws.State == WebSocketState.Open)
                .Select(ws => CloseWebSocketSafelyAsync(ws))
                .ToList();

            if (closeTasks.Count > 0)
            {
                await Task.WhenAll(closeTasks).ConfigureAwait(false);
            }

            clients.Clear();
            httpListener?.Stop();
            httpListener?.Close();

            cancellationTokenSource?.Dispose();

            logger.Info("WebSocket server stopped");
        }
        catch (Exception ex)
        {
            logger.Error(ex, "Error stopping WebSocket server");
        }
    }
}
