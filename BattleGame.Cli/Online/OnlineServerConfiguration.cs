namespace BattleGame.Cli.Online;

public static class OnlineServerConfiguration
{
    public const string FileName = "在线服务器地址.txt";

    public static string? Load()
    {
        string? environmentValue = Environment.GetEnvironmentVariable(
            "BATTLEGAME_SERVER_URL");
        string path = Path.Combine(AppContext.BaseDirectory, FileName);
        IEnumerable<string> lines = File.Exists(path)
            ? File.ReadLines(path)
            : Array.Empty<string>();
        return Resolve(environmentValue, lines);
    }

    public static string? Resolve(string? environmentValue, IEnumerable<string> fileLines)
    {
        if (!string.IsNullOrWhiteSpace(environmentValue))
        {
            return environmentValue.Trim();
        }

        return fileLines
            .Select(line => line.Trim())
            .FirstOrDefault(line => line.Length > 0 && !line.StartsWith('#'));
    }
}
