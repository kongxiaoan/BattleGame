using BattleGame.Cli.Online;
using System.Net.WebSockets;
using BattleGame.Online;
using Spectre.Console;

namespace BattleGame.Cli;

/// <summary>
/// 应用主菜单与模式切换。单机、局域网和在线服务保持独立编排，避免界面选择渗入规则层。
/// </summary>
public sealed class SpectreGameShell
{
    private readonly IAnsiConsole _console;
    private readonly TerminalPrompts _prompts;
    private readonly string _deviceToken;

    public SpectreGameShell(
        IAnsiConsole console,
        TerminalPrompts prompts,
        string deviceToken)
    {
        _console = console;
        _prompts = prompts;
        _deviceToken = deviceToken;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            RenderHome();
            string choice = _prompts.Select(
                GameText.MainMenuPrompt,
                new[]
                {
                    GameText.MenuSingle,
                    GameText.MenuLanCreate,
                    GameText.MenuLanJoin,
                    GameText.MenuOnlineCreate,
                    GameText.MenuOnlineJoin,
                    GameText.MenuProfile,
                    GameText.MenuExit
                },
                value => value);

            if (choice == GameText.MenuExit)
            {
                return;
            }

            if (choice == GameText.MenuSingle)
            {
                _console.Clear();
                new ConsoleGame(Console.In, Console.Out).Run();
                return;
            }

            if (choice == GameText.MenuProfile)
            {
                await ShowProfileAsync(cancellationToken);
                WaitForMenu();
                continue;
            }

            // 使用词条值判断模式，翻译文案只要保持选项唯一就不会影响网络协议。
            bool createRoom = choice == GameText.MenuLanCreate || choice == GameText.MenuOnlineCreate;
            bool useLan = choice == GameText.MenuLanCreate || choice == GameText.MenuLanJoin;
            ServerConnectionProfile? profile = useLan
                ? ReadLanProfile()
                : ReadOnlineProfile();
            if (profile == null)
            {
                WaitForMenu();
                continue;
            }

            _console.Clear();
            await new OnlineConsoleGame(
                _console,
                _prompts,
                profile,
                createRoom,
                _deviceToken)
                .RunAsync(cancellationToken);
        }
    }

    private async Task ShowProfileAsync(CancellationToken cancellationToken)
    {
        string choice = _prompts.Select(
            GameText.ProfileConnectionPrompt,
            new[] { GameText.ProfileLan, GameText.ProfileOnline },
            value => value);
        ServerConnectionProfile? profile = choice == GameText.ProfileLan
            ? ReadLanProfile()
            : ReadOnlineProfile();
        if (profile == null)
        {
            return;
        }

        await using var client = new GameServerClient();
        try
        {
            await _console.Status()
                .Spinner(Spinner.Known.Dots12)
                .StartAsync(GameText.NetworkConnecting, async _ =>
                    await client.ConnectAsync(profile.WebSocketEndpoint, cancellationToken));
            await client.GetProfileAsync(
                _deviceToken,
                Guid.NewGuid().ToString("N"),
                cancellationToken);
            IncomingServerMessage? response = await client.ReceiveAsync(cancellationToken);
            if (response == null)
            {
                throw new InvalidDataException(GameText.NetworkDisconnected);
            }

            if (response.Type == ServerMessageTypes.Error)
            {
                ProtocolError error = response.ReadPayload<ProtocolError>();
                string message = error.Code == "PROFILENOTFOUND"
                    ? GameText.ProfileNotFound
                    : string.Format(GameText.NetworkServerError, error.Code);
                _console.MarkupLine($"[yellow]{Markup.Escape(message)}[/]");
                return;
            }

            if (response.Type != ServerMessageTypes.Profile)
            {
                throw new InvalidDataException();
            }

            new ProfileConsoleView(_console).Render(response.ReadPayload<PlayerProfile>());
        }
        catch (Exception exception) when (
            exception is WebSocketException
                or HttpRequestException
                or InvalidDataException)
        {
            _console.MarkupLine($"[red]{Markup.Escape(string.Format(
                GameText.NetworkConnectionFailed,
                exception.Message))}[/]");
        }
    }

    private ServerConnectionProfile ReadLanProfile()
    {
        string address = _prompts.ReadText(
            GameText.LanServerPrompt,
            GameText.LanServerDefault);
        return ServerConnectionProfile.CreateLan(address);
    }

    private ServerConnectionProfile? ReadOnlineProfile()
    {
        string? configured = OnlineServerConfiguration.Load();
        if (string.IsNullOrWhiteSpace(configured))
        {
            _console.MarkupLine($"[yellow]{Markup.Escape(GameText.OnlineServerMissing)}[/]");
            return null;
        }

        return ServerConnectionProfile.CreateOnline(configured);
    }

    private void RenderHome()
    {
        _console.Clear();
        _console.Write(new FigletText(GameText.AsciiTitle)
            .Centered()
            .Color(Color.Cyan1));
        _console.Write(Align.Center(
            new Markup($"[grey]{Markup.Escape(GameText.Subtitle)}[/]")));
        _console.WriteLine();
        _console.Write(new Rule("[yellow]⚖ AI FATE COURT ⚖[/]")
        {
            Style = Style.Parse("cyan")
        });
    }

    private void WaitForMenu()
    {
        _console.WriteLine();
        _prompts.WaitForEnter(GameText.NetworkBackToMenu);
    }
}
