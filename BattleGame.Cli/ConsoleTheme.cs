namespace BattleGame.Cli;

/// <summary>
/// 集中管理 ANSI 样式和等宽布局；测试关闭颜色后仍得到稳定的纯文本输出。
/// </summary>
public sealed class ConsoleTheme
{
    private const string Reset = "\u001b[0m";
    private const string Cyan = "\u001b[96m";
    private const string Yellow = "\u001b[93m";
    private const string Bold = "\u001b[1m";

    private readonly TextWriter _output;
    private readonly bool _useColors;

    public ConsoleTheme(TextWriter output, bool useColors)
    {
        _output = output;
        _useColors = useColors;
    }

    public void WriteBanner(string title, string subtitle)
    {
        string border = new('═', 52);
        WriteLine(Color($"╔{border}╗", Cyan));
        WriteLine(Color($"║  {title,-48}  ║", Bold + Cyan));
        WriteLine(Color($"║  {subtitle,-48}  ║", Cyan));
        WriteLine(Color($"╚{border}╝", Cyan));
    }

    public void WriteSection(string title)
    {
        WriteLine(string.Empty);
        WriteLine(Color($"━━ {title} {new string('━', Math.Max(2, 42 - title.Length))}", Yellow));
    }

    public void WriteLine(string value)
    {
        _output.WriteLine(value);
    }

    public static string CreateBar(int value, int maximum, int width)
    {
        int filled = maximum == 0 ? 0 : (int)Math.Round((double)value / maximum * width);
        filled = Math.Clamp(filled, 0, width);
        return $"[{new string('█', filled)}{new string('░', width - filled)}]";
    }

    public static string CreateEnergyBar(int energy)
    {
        int clamped = Math.Clamp(energy, 0, 3);
        return $"{new string('◆', clamped)}{new string('◇', 3 - clamped)}";
    }

    private string Color(string value, string color)
    {
        return _useColors ? color + value + Reset : value;
    }
}
