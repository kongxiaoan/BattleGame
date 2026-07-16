namespace BattleGame.Cli.Online;

public enum ServerConnectionMode
{
    Lan = 1,
    Online = 2
}

/// <summary>
/// 客户端只切换服务器地址，不切换协议或业务实现。局域网验证完成后，公网部署
/// 只需要把 ws:// 地址换成 wss:// 地址。
/// </summary>
public sealed record ServerConnectionProfile(
    ServerConnectionMode Mode,
    Uri WebSocketEndpoint)
{
    public static ServerConnectionProfile CreateLan(string address)
    {
        return new ServerConnectionProfile(
            ServerConnectionMode.Lan,
            Normalize(address, useLanDefaultPort: true));
    }

    public static ServerConnectionProfile CreateOnline(string address)
    {
        return new ServerConnectionProfile(
            ServerConnectionMode.Online,
            Normalize(address, useLanDefaultPort: false));
    }

    private static Uri Normalize(string address, bool useLanDefaultPort)
    {
        string value = address?.Trim() ?? string.Empty;
        if (value.Length == 0)
        {
            throw new ArgumentException(nameof(address));
        }

        if (!value.Contains("://", StringComparison.Ordinal))
        {
            value = "ws://" + value;
        }

        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? parsed)
            || string.IsNullOrWhiteSpace(parsed.Host))
        {
            throw new ArgumentException(nameof(address));
        }

        string scheme = parsed.Scheme.ToLowerInvariant() switch
        {
            "ws" => "ws",
            "wss" => "wss",
            "http" => "ws",
            "https" => "wss",
            _ => throw new ArgumentException(nameof(address))
        };

        var builder = new UriBuilder(parsed)
        {
            Scheme = scheme,
            Path = parsed.AbsolutePath == "/" ? "/ws" : parsed.AbsolutePath
        };

        // UriBuilder 改协议时会保留 http/https 默认端口，需要显式恢复 WebSocket 默认值。
        if (useLanDefaultPort && parsed.IsDefaultPort)
        {
            builder.Port = 5088;
        }
        else if (!useLanDefaultPort && parsed.IsDefaultPort)
        {
            builder.Port = -1;
        }

        return builder.Uri;
    }
}
