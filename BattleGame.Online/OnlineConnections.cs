using System.Collections.Concurrent;

namespace BattleGame.Online;

/// <summary>
/// 把具体 WebSocket 隔离在委托之后，使协议编排可以用内存连接做确定性测试。
/// 每条连接串行发送，避免多个广播同时写入同一个 WebSocket。
/// </summary>
public sealed class OnlineClientConnection : IAsyncDisposable
{
    private readonly Func<ServerMessage, CancellationToken, Task> _sender;
    private readonly SemaphoreSlim _sendLock = new(1, 1);

    public OnlineClientConnection(
        string connectionId,
        Func<ServerMessage, CancellationToken, Task> sender)
    {
        ConnectionId = string.IsNullOrWhiteSpace(connectionId)
            ? throw new ArgumentException(nameof(connectionId))
            : connectionId;
        _sender = sender ?? throw new ArgumentNullException(nameof(sender));
    }

    public string ConnectionId { get; }
    public string? RoomCode { get; private set; }
    public Guid? PlayerId { get; private set; }
    public Guid? ProfileId { get; private set; }

    internal void Bind(RoomAccess access)
    {
        RoomCode = access.RoomCode;
        PlayerId = access.PlayerId;
        ProfileId = access.ProfileId;
    }

    public async Task SendAsync(ServerMessage message, CancellationToken cancellationToken = default)
    {
        await _sendLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _sender(message, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _sendLock.Release();
        }
    }

    public ValueTask DisposeAsync()
    {
        _sendLock.Dispose();
        return ValueTask.CompletedTask;
    }
}

public sealed class OnlineConnectionRegistry
{
    private readonly ConcurrentDictionary<Guid, OnlineClientConnection> _connections = new();

    public void Register(Guid playerId, OnlineClientConnection connection)
    {
        _connections[playerId] = connection;
    }

    public void Remove(OnlineClientConnection connection)
    {
        if (connection.PlayerId is not Guid playerId)
        {
            return;
        }

        // 旧连接断开时不能误删已经完成重连的新连接。
        _connections.TryGetValue(playerId, out OnlineClientConnection? registered);
        if (ReferenceEquals(registered, connection))
        {
            _connections.TryRemove(playerId, out _);
        }
    }

    public async Task BroadcastAsync(
        OnlineMatchSnapshot snapshot,
        ServerMessage message,
        CancellationToken cancellationToken)
    {
        var sends = new List<Task>();
        foreach (PlayerSnapshot player in snapshot.Players)
        {
            if (_connections.TryGetValue(player.PlayerId, out OnlineClientConnection? connection))
            {
                sends.Add(connection.SendAsync(message, cancellationToken));
            }
        }

        await Task.WhenAll(sends).ConfigureAwait(false);
    }
}
