using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using BattleGame.Core;
using BattleGame.Online;

namespace BattleGame.Cli.Online;

/// <summary>
/// 局域网与公网共用的 WebSocket 客户端。这里不包含任何游戏判定，客户端只提交意图并
/// 展示服务端快照，防止未来切换公网地址时出现两套规则。
/// </summary>
public sealed class GameServerClient : IAsyncDisposable
{
    private const int MaximumMessageSize = 64 * 1024;
    private ClientWebSocket _socket = new();
    private Uri? _endpoint;
    private RoomAccess? _reconnectAccess;

    public WebSocketState State => _socket.State;

    public async Task ConnectAsync(Uri endpoint, CancellationToken cancellationToken)
    {
        await _socket.ConnectAsync(endpoint, cancellationToken);
        _endpoint = endpoint;
    }

    public void EnableReconnect(RoomAccess access)
    {
        _reconnectAccess = access;
    }

    public Task CreateRoomAsync(
        string displayName,
        string deviceToken,
        string requestId,
        CancellationToken cancellationToken)
    {
        return SendCommandAsync(
            ClientMessageTypes.CreateRoom,
            requestId,
            new { displayName, deviceToken },
            cancellationToken);
    }

    public Task JoinRoomAsync(
        string roomCode,
        string displayName,
        string deviceToken,
        string requestId,
        CancellationToken cancellationToken)
    {
        return SendCommandAsync(
            ClientMessageTypes.JoinRoom,
            requestId,
            new { roomCode, displayName, deviceToken },
            cancellationToken);
    }

    public Task ReconnectAsync(
        RoomAccess access,
        string requestId,
        CancellationToken cancellationToken)
    {
        return SendCommandAsync(
            ClientMessageTypes.Reconnect,
            requestId,
            new
            {
                roomCode = access.RoomCode,
                playerId = access.PlayerId,
                reconnectToken = access.ReconnectToken
            },
            cancellationToken);
    }

    public Task SubmitActionAsync(
        int expectedRound,
        CombatAction action,
        string requestId,
        CancellationToken cancellationToken)
    {
        return SendCommandAsync(
            ClientMessageTypes.SubmitAction,
            requestId,
            new { expectedRound, action },
            cancellationToken);
    }

    public Task SubmitAnswerAsync(
        string questionId,
        int selectedOptionIndex,
        string requestId,
        CancellationToken cancellationToken)
    {
        return SendCommandAsync(
            ClientMessageTypes.SubmitAnswer,
            requestId,
            new { questionId, selectedOptionIndex },
            cancellationToken);
    }

    public Task SubmitLastChanceEntryAsync(
        string challengeId,
        string entry,
        string requestId,
        CancellationToken cancellationToken)
    {
        return SendCommandAsync(
            ClientMessageTypes.SubmitLastChanceEntry,
            requestId,
            new { challengeId, entry },
            cancellationToken);
    }

    public Task GetProfileAsync(
        string deviceToken,
        string requestId,
        CancellationToken cancellationToken)
    {
        return SendCommandAsync(
            ClientMessageTypes.GetProfile,
            requestId,
            new { deviceToken },
            cancellationToken);
    }

    public Task GetTagCandidatesAsync(
        string requestId,
        CancellationToken cancellationToken)
    {
        return SendCommandAsync(
            ClientMessageTypes.GetTagCandidates,
            requestId,
            new { },
            cancellationToken);
    }

    public Task GrantTagAsync(
        string tagCode,
        string requestId,
        CancellationToken cancellationToken)
    {
        return SendCommandAsync(
            ClientMessageTypes.GrantTag,
            requestId,
            new { tagCode },
            cancellationToken);
    }

    public Task SubmitFateDecisionAsync(
        string eventId,
        string argument,
        string requestId,
        CancellationToken cancellationToken)
    {
        return SendCommandAsync(
            ClientMessageTypes.SubmitFateDecision,
            requestId,
            new { eventId, argument },
            cancellationToken);
    }

    public async Task<IncomingServerMessage?> ReceiveAsync(
        CancellationToken cancellationToken)
    {
        try
        {
            IncomingServerMessage? message = await ReceiveCoreAsync(cancellationToken);
            if (message == null && _endpoint != null && _reconnectAccess != null)
            {
                RoomAccess restored = await RecoverAsync(cancellationToken);
                throw new ConnectionRestoredException(restored);
            }

            return message;
        }
        catch (Exception exception) when (
            (exception is WebSocketException or IOException)
            && _endpoint != null
            && _reconnectAccess != null)
        {
            RoomAccess restored = await RecoverAsync(cancellationToken);
            throw new ConnectionRestoredException(restored);
        }
    }

    private async Task<IncomingServerMessage?> ReceiveCoreAsync(
        CancellationToken cancellationToken)
    {
        byte[] buffer = new byte[4 * 1024];
        using var messageBuffer = new MemoryStream();
        while (true)
        {
            WebSocketReceiveResult result = await _socket.ReceiveAsync(
                buffer,
                cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
            {
                return null;
            }

            if (result.MessageType != WebSocketMessageType.Text
                || messageBuffer.Length + result.Count > MaximumMessageSize)
            {
                throw new InvalidDataException();
            }

            messageBuffer.Write(buffer, 0, result.Count);
            if (!result.EndOfMessage)
            {
                continue;
            }

            string json = Encoding.UTF8.GetString(
                messageBuffer.GetBuffer(),
                0,
                (int)messageBuffer.Length);
            return JsonSerializer.Deserialize<IncomingServerMessage>(
                json,
                OnlineProtocolJson.Options) ?? throw new JsonException();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_socket.State is WebSocketState.Open or WebSocketState.CloseReceived)
        {
            try
            {
                await _socket.CloseAsync(
                    WebSocketCloseStatus.NormalClosure,
                    "client closed",
                    CancellationToken.None);
            }
            catch (WebSocketException)
            {
                // 关闭阶段服务器可能已离线，此时释放套接字比继续上抛更重要。
            }
        }

        _socket.Dispose();
    }

    private async Task SendCommandAsync(
        string type,
        string requestId,
        object payload,
        CancellationToken cancellationToken)
    {
        byte[] bytes = JsonSerializer.SerializeToUtf8Bytes(
            new ClientCommand(type, requestId, payload),
            OnlineProtocolJson.Options);
        try
        {
            await SendBytesAsync(bytes, cancellationToken);
        }
        catch (Exception exception) when (
            (exception is WebSocketException or InvalidOperationException)
            && _endpoint != null
            && _reconnectAccess != null)
        {
            // 不重发不确定是否已经到达服务端的命令，恢复权威快照后让状态机决定下一步。
            RoomAccess restored = await RecoverAsync(cancellationToken);
            throw new ConnectionRestoredException(restored);
        }
    }

    private async Task SendBytesAsync(byte[] bytes, CancellationToken cancellationToken)
    {
        await _socket.SendAsync(
            bytes,
            WebSocketMessageType.Text,
            true,
            cancellationToken);
    }

    private async Task<RoomAccess> RecoverAsync(CancellationToken cancellationToken)
    {
        Uri endpoint = _endpoint ?? throw new InvalidOperationException();
        RoomAccess previous = _reconnectAccess ?? throw new InvalidOperationException();
        _socket.Dispose();
        _socket = new ClientWebSocket();
        await _socket.ConnectAsync(endpoint, cancellationToken);

        byte[] reconnectCommand = JsonSerializer.SerializeToUtf8Bytes(
            new ClientCommand(
                ClientMessageTypes.Reconnect,
                Guid.NewGuid().ToString("N"),
                new
                {
                    roomCode = previous.RoomCode,
                    playerId = previous.PlayerId,
                    reconnectToken = previous.ReconnectToken
                }),
            OnlineProtocolJson.Options);
        await SendBytesAsync(reconnectCommand, cancellationToken);
        IncomingServerMessage response = await ReceiveCoreAsync(cancellationToken)
            ?? throw new WebSocketException();
        if (response.Type == ServerMessageTypes.Error)
        {
            ProtocolError error = response.ReadPayload<ProtocolError>();
            throw new InvalidDataException(error.Code);
        }

        if (response.Type != ServerMessageTypes.RoomAccess)
        {
            throw new InvalidDataException();
        }

        RoomAccess restored = response.ReadPayload<RoomAccess>();
        _reconnectAccess = restored;
        return restored;
    }

    private sealed record ClientCommand(string Type, string RequestId, object Payload);
}

/// <summary>
/// 连接已恢复不是失败，而是要求上层丢弃旧的本地等待状态并采用新的服务端快照。
/// </summary>
public sealed class ConnectionRestoredException : Exception
{
    public ConnectionRestoredException(RoomAccess access)
    {
        Access = access;
    }

    public RoomAccess Access { get; }
}

public sealed record IncomingServerMessage(
    string Type,
    string? RequestId,
    JsonElement Payload)
{
    public T ReadPayload<T>()
    {
        return Payload.Deserialize<T>(OnlineProtocolJson.Options)
            ?? throw new JsonException();
    }
}
