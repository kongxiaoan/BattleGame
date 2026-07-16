using System.Globalization;
using BattleGame.Online;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace BattleGame.Cli.Online;

/// <summary>
/// 个人中心只负责渲染服务端资料；战绩和标签真相仍由服务端数据库维护。
/// </summary>
public sealed class ProfileConsoleView
{
    private readonly IAnsiConsole _console;

    public ProfileConsoleView(IAnsiConsole console)
    {
        _console = console;
    }

    public void Render(PlayerProfile profile)
    {
        _console.Clear();
        _console.Write(new FigletText(GameText.ProfileHeader)
            .Centered()
            .Color(Color.Cyan1));

        string record = string.Format(
            CultureInfo.CurrentCulture,
            GameText.ProfileRecord,
            profile.GamesPlayed,
            profile.Wins,
            profile.Losses,
            profile.Draws);
        string winRate = string.Format(
            CultureInfo.CurrentCulture,
            GameText.ProfileWinRate,
            ProfilePresentation.CalculateWinRate(profile.Wins, profile.GamesPlayed));
        _console.Write(new Panel(new Rows(
            new Markup($"[bold cyan]{Markup.Escape(profile.DisplayName)}[/]"),
            new Markup($"[white]{Markup.Escape(record)}[/]"),
            new Markup($"[yellow]{Markup.Escape(winRate)}[/]")))
        {
            Border = BoxBorder.Double,
            BorderStyle = Style.Parse("cyan")
        });

        _console.Write(new Rule($"[magenta]{Markup.Escape(GameText.ProfileTags)}[/]"));
        if (profile.Tags.Count == 0)
        {
            _console.Write(new Panel(new Markup(
                $"[grey]{Markup.Escape(GameText.ProfileNoTags)}[/]"))
            {
                Border = BoxBorder.Rounded
            });
            return;
        }

        // 标签数量未来可能增长，逐个卡片比宽表格更适合窄终端和 SSH 窗口。
        IEnumerable<IRenderable> cards = profile.Tags.Select(tag =>
            (IRenderable)new Panel(new Rows(
                new Markup($"[bold yellow]◆ {Markup.Escape(tag.DisplayName)}[/]"),
                new Markup($"[grey]{Markup.Escape(ProfilePresentation.DescribeTagLifetime(tag))}[/]")))
            {
                Border = BoxBorder.Rounded,
                BorderStyle = Style.Parse("magenta")
            });
        _console.Write(new Rows(cards));
    }
}
