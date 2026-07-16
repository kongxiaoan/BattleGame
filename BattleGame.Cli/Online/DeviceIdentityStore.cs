using System.Security.Cryptography;

namespace BattleGame.Cli.Online;

/// <summary>
/// 局域网版本使用设备令牌恢复个人数据。令牌不写进发布包，也不上传到战报；
/// 服务端只保存其哈希。公网账号系统上线后可由正式登录令牌替换这一适配层。
/// </summary>
public sealed class DeviceIdentityStore
{
    private readonly string _path;

    public DeviceIdentityStore(string path)
    {
        _path = string.IsNullOrWhiteSpace(path)
            ? throw new ArgumentException(nameof(path))
            : path;
    }

    public static DeviceIdentityStore CreateDefault()
    {
        string directory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "BattleGame");
        return new DeviceIdentityStore(Path.Combine(directory, "device-token"));
    }

    public string LoadOrCreate()
    {
        if (File.Exists(_path))
        {
            string existing = File.ReadAllText(_path).Trim();
            if (existing.Length >= 24)
            {
                return existing;
            }
        }

        string? directory = Path.GetDirectoryName(_path);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        string token = Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        File.WriteAllText(_path, token + Environment.NewLine);
        if (OperatingSystem.IsMacOS() || OperatingSystem.IsLinux())
        {
            File.SetUnixFileMode(_path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }

        return token;
    }
}
