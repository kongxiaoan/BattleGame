using System.Net.WebSockets;
using System.Text;
using BattleGame.Online;

namespace BattleGame.Server;

/// <summary>
/// ASP.NET Core 与可测试协议处理器之间的薄适配层，只负责帧拼接、大小限制和连接生命周期。
/// </summary>
public sealed class WebSocketGameEndpoint
{
    private const int ReceiveBufferSize = 4 * 1024;
    private const int MaximumMessageSize = 16 * 1024;
    private readonly OnlineCommandProcessor _processor;
    private readonly ILogger<WebSocketGameEndpoint> _logger;

    public WebSocketGameEndpoint(
        OnlineCommandProcessor processor,
        ILogger<WebSocketGameEndpoint> logger)
    {
        _processor = processor;
        _logger = logger;
    }

    public async Task HandleAsync(HttpContext context)
    {
        if (!context.WebSockets.IsWebSocketRequest)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            return;
        }

        using WebSocket socket = await context.WebSockets.AcceptWebSocketAsync();
        await using var connection = new OnlineClientConnection(
            Guid.NewGuid().ToString("N"),
            (message, cancellationToken) => SendAsync(socket, message, cancellationToken));

        try
        {
            while (socket.State == WebSocketState.Open
                   && !context.RequestAborted.IsCancellationRequested)
            {
                string? message = await ReceiveTextAsync(socket, context.RequestAborted);
                if (message == null)
                {
                    break;
                }

                await _processor.ProcessAsync(connection, message, context.RequestAborted);
            }
        }
        catch (OperationCanceledException) when (context.RequestAborted.IsCancellationRequested)
        {
            // 客户端断开或服务器停止属于正常生命周期，不写错误日志。
        }
        catch (WebSocketException exception)
        {
            _logger.LogInformation(
                exception,
                "WebSocket connection {ConnectionId} ended unexpectedly.",
                connection.ConnectionId);
        }
        finally
        {
            _processor.Disconnect(connection);
            if (socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
            {
                await socket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "closed",
                    CancellationToken.None);
            }
        }
    }

    private static async Task<string?> ReceiveTextAsync(
        WebSocket socket,
        CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[ReceiveBufferSize];
        using var messageBuffer = new MemoryStream();
        while (true)
        {
            WebSocketReceiveResult result = await socket.ReceiveAsync(
                buffer,
                cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            if (result.MessageType != WebSocketMessageType.Text)
            {
                await socket.CloseAsync(
                    WebSocketCloseStatus.InvalidMessageType,
                    "text messages only",
                    cancellationToken);
                return null;
            }

            if (messageBuffer.Length + result.Count > MaximumMessageSize)
            {
                await socket.CloseAsync(
                    WebSocketCloseStatus.MessageTooBig,
                    "message too large",
                    cancellationToken);
                return null;
            }

            messageBuffer.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
            {
                return Encoding.UTF8.GetString(messageBuffer.GetBuffer(), 0, (int)messageBuffer.Length);
            }
        }
    }

    private static Task SendAsync(
        WebSocket socket,
        ServerMessage message,
        CancellationToken cancellationToken)
    {
        byte[] bytes = Encoding.UTF8.GetBytes(OnlineProtocolJson.Serialize(message));
        return socket.SendAsync(
            bytes,
            WebSocketMessageType.Text,
            true,
            cancellationToken);
    }
}
