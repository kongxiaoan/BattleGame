using System.Net.WebSockets;
using BattleGame.Core;
using BattleGame.Online;
using Spectre.Console;
using Spectre.Console.Rendering;

namespace BattleGame.Cli.Online;

/// <summary>
/// 联网对局的终端编排层。所有显示都来自服务端快照，确保局域网与未来公网模式的行为一致。
/// </summary>
public sealed class OnlineConsoleGame
{
    private readonly IAnsiConsole _console;
    private readonly TerminalPrompts _prompts;
    private readonly ServerConnectionProfile _profile;
    private readonly bool _createRoom;
    private readonly string _deviceToken;

    public OnlineConsoleGame(
        IAnsiConsole console,
        TerminalPrompts prompts,
        ServerConnectionProfile profile,
        bool createRoom,
        string deviceToken)
    {
        _console = console;
        _prompts = prompts;
        _profile = profile;
        _createRoom = createRoom;
        _deviceToken = deviceToken;
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        string displayName = _prompts.ReadText(
            GameText.NetworkNamePrompt,
            defaultValue: null,
            name => string.IsNullOrWhiteSpace(name) || name.Trim().Length > 20
                ? GameText.NameEmpty
                : null);

        await using var client = new GameServerClient();
        try
        {
            await _console.Status()
                .Spinner(Spinner.Known.Dots12)
                .SpinnerStyle(Style.Parse("cyan"))
                .StartAsync(GameText.NetworkConnecting, async _ =>
                    await client.ConnectAsync(_profile.WebSocketEndpoint, cancellationToken));
            _console.MarkupLine($"[green]✓[/] {Markup.Escape(GameText.NetworkConnected)}");

            if (_createRoom)
            {
                await client.CreateRoomAsync(
                    displayName.Trim(),
                    _deviceToken,
                    CreateRequestId(),
                    cancellationToken);
            }
            else
            {
                string roomCode = _prompts.ReadText(
                    GameText.RoomCodePrompt,
                    defaultValue: null,
                    code => code.Trim().Length == 6 ? null : GameText.InvalidChoice);
                await client.JoinRoomAsync(
                    roomCode.Trim().ToUpperInvariant(),
                    displayName.Trim(),
                    _deviceToken,
                    CreateRequestId(),
                    cancellationToken);
            }

            RoomAccess access = await WaitForAccessAsync(client, cancellationToken);
            client.EnableReconnect(access);
            ShowRoomAccess(access);
            OnlineMatchSnapshot snapshot = access.Snapshot.Status == OnlineRoomStatus.InProgress
                ? access.Snapshot
                : await WaitForStartedMatchAsync(client, cancellationToken);
            await PlayMatchAsync(client, access, snapshot, cancellationToken);
        }
        catch (OperationCanceledException)
        {
            // Ctrl+C 或应用退出不额外输出错误。
        }
        catch (Exception exception) when (
            exception is WebSocketException
                or HttpRequestException
                or InvalidDataException
                or OnlineServerException)
        {
            _console.MarkupLine($"[red]{Markup.Escape(Format(
                GameText.NetworkConnectionFailed,
                exception.Message))}[/]");
        }

        _console.WriteLine();
        _console.Write(new Rule($"[grey]{Markup.Escape(GameText.NetworkBackToMenu)}[/]"));
        _prompts.WaitForEnter(GameText.NetworkBackToMenu);
    }

    private async Task PlayMatchAsync(
        GameServerClient client,
        RoomAccess access,
        OnlineMatchSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        while (snapshot.Status == OnlineRoomStatus.InProgress)
        {
            try
            {
            RenderBattle(snapshot, access.PlayerId);
            if (snapshot.Phase == OnlineMatchPhase.FateEvent
                && snapshot.ActiveFateEvent != null)
            {
                FateEventSubmission fateResult = await PlayFateEventAsync(
                    client,
                    access,
                    snapshot,
                    cancellationToken);
                snapshot = fateResult.Snapshot;
                RenderFateResolution(fateResult);
                await Task.Delay(1200, cancellationToken);
                continue;
            }
            if (snapshot.Phase == OnlineMatchPhase.LastChance
                && snapshot.ActiveLastChance != null)
            {
                LastChanceSubmission challengeResult = await PlayLastChanceAsync(
                    client,
                    access,
                    snapshot,
                    cancellationToken);
                snapshot = challengeResult.Snapshot;
                RenderLastChanceResult(challengeResult);
                await Task.Delay(1200, cancellationToken);
                continue;
            }

            if (snapshot.Phase == OnlineMatchPhase.AnsweringQuestion
                && snapshot.ActiveQuestion != null)
            {
                QuestionSubmission questionResult = await PlayQuestionAsync(
                    client,
                    access,
                    snapshot.ActiveQuestion,
                    cancellationToken);
                snapshot = questionResult.Snapshot;
                RenderQuestionResult(questionResult, access.PlayerId);
                await Task.Delay(1200, cancellationToken);
                continue;
            }

            PlayerSnapshot self = snapshot.Players.Single(
                player => player.PlayerId == access.PlayerId);

            if (!self.ActionLocked)
            {
                CombatAction action = PromptAction(self.Energy);
                await client.SubmitActionAsync(
                    snapshot.RoundNumber,
                    action,
                    CreateRequestId(),
                    cancellationToken);
            }

            ActionSubmission submission = await WaitForRoundAsync(
                client,
                cancellationToken);
            snapshot = submission.Snapshot;
            if (submission.ResolvedRound != null)
            {
                RenderRoundResult(snapshot, submission.ResolvedRound);
                await Task.Delay(900, cancellationToken);
            }
            }
            catch (ConnectionRestoredException restored)
            {
                snapshot = restored.Access.Snapshot;
                _console.MarkupLine($"[green]↻ {Markup.Escape(GameText.NetworkReconnected)}[/]");
            }
        }

        RenderBattle(snapshot, access.PlayerId);
        RenderWinner(snapshot, access.PlayerId);
        if (IsWinner(snapshot, access.PlayerId) && access.ProfileId != null)
        {
            await OfferTagAwardAsync(client, snapshot, cancellationToken);
        }
    }

    private async Task<FateEventSubmission> PlayFateEventAsync(
        GameServerClient client,
        RoomAccess access,
        OnlineMatchSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        FateEventSnapshot fateEvent = snapshot.ActiveFateEvent
            ?? throw new InvalidOperationException();
        RenderFateEvent(fateEvent);
        if (fateEvent.DisadvantagedPlayerId == access.PlayerId
            && !fateEvent.JudgmentPending)
        {
            PlayerSnapshot self = snapshot.Players.Single(
                player => player.PlayerId == access.PlayerId);
            string argument = string.Empty;
            if (self.AppealAvailable)
            {
                string choice = _prompts.Select(
                    GameText.FateDecisionPrompt,
                    new[] { GameText.FateAppeal, GameText.FateAccept },
                    value => value);
                if (choice == GameText.FateAppeal)
                {
                    argument = _prompts.ReadText(
                        GameText.FateArgumentPrompt,
                        defaultValue: null,
                        value => value.Length > 200 ? GameText.FateArgumentTooLong : null);
                    _console.MarkupLine($"[yellow]◉ {Markup.Escape(GameText.FateJudging)}[/]");
                }
            }
            else
            {
                _console.MarkupLine($"[grey]{Markup.Escape(GameText.FateAppealUsed)}[/]");
            }

            await client.SubmitFateDecisionAsync(
                fateEvent.EventId,
                argument,
                CreateRequestId(),
                cancellationToken);
        }
        else
        {
            _console.MarkupLine($"[yellow]◉ {Markup.Escape(
                fateEvent.JudgmentPending ? GameText.FateJudging : GameText.FateWaiting)}[/]");
        }

        IncomingServerMessage result = await WaitForMessageTypeAsync(
            client,
            ServerMessageTypes.FateResolved,
            cancellationToken);
        return result.ReadPayload<FateEventSubmission>();
    }

    private void RenderFateEvent(FateEventSnapshot fateEvent)
    {
        string effect = fateEvent.Type switch
        {
            BattleEventType.RestoreHealth => Format(GameText.FateRestore, fateEvent.Magnitude),
            BattleEventType.LoseHealth => Format(GameText.FateLoss, fateEvent.Magnitude),
            BattleEventType.GainEnergy => Format(GameText.FateEnergy, fateEvent.Magnitude),
            _ => throw new ArgumentOutOfRangeException()
        };
        _console.Write(new Panel(new Rows(
            new Markup($"[bold white]{Markup.Escape(fateEvent.Narrative)}[/]"),
            new Markup($"[yellow]{Markup.Escape(effect)}[/]")))
        {
            Header = new PanelHeader($" {Markup.Escape(GameText.FateHeader)} ", Justify.Center),
            Border = BoxBorder.Double,
            BorderStyle = Style.Parse("magenta")
        });
    }

    private void RenderFateResolution(FateEventSubmission submission)
    {
        FateEventResolution resolution = submission.Resolution
            ?? throw new InvalidOperationException();
        string text = Format(
            resolution.EventApplied ? GameText.FateApplied : GameText.FateRevoked,
            resolution.Explanation);
        _console.MarkupLine(resolution.EventApplied
            ? $"[yellow]{Markup.Escape(text)}[/]"
            : $"[green]{Markup.Escape(text)}[/]");
    }

    private async Task OfferTagAwardAsync(
        GameServerClient client,
        OnlineMatchSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        await client.GetTagCandidatesAsync(CreateRequestId(), cancellationToken);
        IncomingServerMessage candidateMessage = await WaitForMessageTypeAsync(
            client,
            ServerMessageTypes.TagCandidates,
            cancellationToken);
        // 反序列化为具体数组，避免 System.Text.Json 对接口集合构造策略发生版本差异。
        IReadOnlyList<TagDefinition> candidates =
            candidateMessage.ReadPayload<TagDefinition[]>();
        var choices = candidates
            .Select(tag => new TagChoice(tag.Code, tag.DisplayName))
            .Append(new TagChoice(null, GameText.TagSkip))
            .ToArray();
        TagChoice selected = _prompts.Select(
            GameText.TagAwardPrompt,
            choices,
            choice => choice.DisplayName);
        if (selected.Code == null)
        {
            return;
        }

        await client.GrantTagAsync(selected.Code, CreateRequestId(), cancellationToken);
        IncomingServerMessage grantedMessage = await WaitForMessageTypeAsync(
            client,
            ServerMessageTypes.TagGranted,
            cancellationToken);
        PlayerProfile tagged = grantedMessage.ReadPayload<PlayerProfile>();
        _console.MarkupLine($"[green]✓ {Markup.Escape(Format(
            GameText.TagGranted,
            selected.DisplayName,
            tagged.DisplayName))}[/]");
    }

    private async Task<LastChanceSubmission> PlayLastChanceAsync(
        GameServerClient client,
        RoomAccess access,
        OnlineMatchSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        LastChanceSnapshot challenge = snapshot.ActiveLastChance
            ?? throw new InvalidOperationException();
        if (challenge.TargetPlayerId != access.PlayerId)
        {
            string targetName = snapshot.Players.Single(
                player => player.PlayerId == challenge.TargetPlayerId).DisplayName;
            return await _console.Status()
                .Spinner(Spinner.Known.BouncingBall)
                .SpinnerStyle(Style.Parse("red"))
                .StartAsync(Format(GameText.LastChanceWatching, targetName), async context =>
                {
                    while (true)
                    {
                        LastChanceSubmission submission = await ReceiveLastChanceAsync(
                            client,
                            cancellationToken);
                        if (submission.Status != LastChanceSubmissionStatus.InProgress)
                        {
                            return submission;
                        }

                        LastChanceSnapshot progress = submission.Snapshot.ActiveLastChance!;
                        context.Status(Format(
                            GameText.LastChanceProgress,
                            progress.CurrentCount,
                            progress.RequiredCount,
                            RemainingSeconds(progress.DeadlineUtc)));
                    }
                });
        }

        while (true)
        {
            RenderLastChanceChallenge(challenge);
            string entry = _prompts.ReadText(
                GameText.LastChanceInputPrompt,
                defaultValue: null);
            await client.SubmitLastChanceEntryAsync(
                challenge.ChallengeId,
                entry,
                CreateRequestId(),
                cancellationToken);
            LastChanceSubmission submission = await ReceiveLastChanceAsync(
                client,
                cancellationToken);
            if (submission.Status != LastChanceSubmissionStatus.InProgress)
            {
                return submission;
            }

            challenge = submission.Snapshot.ActiveLastChance
                ?? throw new InvalidOperationException();
        }
    }

    private async Task<LastChanceSubmission> ReceiveLastChanceAsync(
        GameServerClient client,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            IncomingServerMessage message = await RequireMessageAsync(client, cancellationToken);
            ThrowIfError(message);
            if (message.Type is ServerMessageTypes.LastChanceProgress
                or ServerMessageTypes.LastChanceResolved)
            {
                return message.ReadPayload<LastChanceSubmission>();
            }
        }
    }

    private async Task<QuestionSubmission> PlayQuestionAsync(
        GameServerClient client,
        RoomAccess access,
        QuestionSnapshot question,
        CancellationToken cancellationToken)
    {
        string category = GetCategoryName(question.Category);
        _console.Write(new Panel(new Markup(
            $"[bold white]{Markup.Escape(question.Prompt)}[/]"))
        {
            Header = new PanelHeader($" {Markup.Escape(Format(
                GameText.QuestionHeader,
                category))} ", Justify.Center),
            Border = BoxBorder.Double,
            BorderStyle = Style.Parse("magenta")
        });

        var choices = question.Options
            .Select((option, index) => new QuestionChoice(index, option))
            .ToArray();
        QuestionChoice selected = _prompts.Select(
            GameText.QuestionPrompt,
            choices,
            choice => $"{(char)('A' + choice.Index)}. {choice.Option}");
        await client.SubmitAnswerAsync(
            question.QuestionId,
            selected.Index,
            CreateRequestId(),
            cancellationToken);

        bool waitingShown = false;
        while (true)
        {
            IncomingServerMessage message = await RequireMessageAsync(client, cancellationToken);
            ThrowIfError(message);
            if (message.Type is not (
                ServerMessageTypes.QuestionStatus or ServerMessageTypes.QuestionResolved))
            {
                continue;
            }

            QuestionSubmission submission = message.ReadPayload<QuestionSubmission>();
            if (submission.Status == QuestionSubmissionStatus.Resolved)
            {
                return submission;
            }

            if (!waitingShown)
            {
                _console.MarkupLine($"[yellow]◉[/] {Markup.Escape(GameText.QuestionWaiting)}");
                waitingShown = true;
            }
        }
    }

    private async Task<RoomAccess> WaitForAccessAsync(
        GameServerClient client,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            IncomingServerMessage message = await RequireMessageAsync(client, cancellationToken);
            if (message.Type == ServerMessageTypes.RoomAccess)
            {
                return message.ReadPayload<RoomAccess>();
            }

            ThrowIfError(message);
        }
    }

    private async Task<OnlineMatchSnapshot> WaitForStartedMatchAsync(
        GameServerClient client,
        CancellationToken cancellationToken)
    {
        return await _console.Status()
            .Spinner(Spinner.Known.BouncingBar)
            .SpinnerStyle(Style.Parse("yellow"))
            .StartAsync(GameText.WaitingGuest, async _ =>
            {
                while (true)
                {
                    IncomingServerMessage message = await RequireMessageAsync(
                        client,
                        cancellationToken);
                    ThrowIfError(message);
                    if (message.Type == ServerMessageTypes.GameState)
                    {
                        OnlineMatchSnapshot snapshot = message.ReadPayload<OnlineMatchSnapshot>();
                        if (snapshot.Status == OnlineRoomStatus.InProgress)
                        {
                            return snapshot;
                        }
                    }
                }
            });
    }

    private async Task<ActionSubmission> WaitForRoundAsync(
        GameServerClient client,
        CancellationToken cancellationToken)
    {
        bool waitingShown = false;
        while (true)
        {
            IncomingServerMessage message = await RequireMessageAsync(client, cancellationToken);
            ThrowIfError(message);
            if (message.Type is not (
                ServerMessageTypes.ActionStatus or ServerMessageTypes.RoundResolved))
            {
                continue;
            }

            ActionSubmission submission = message.ReadPayload<ActionSubmission>();
            if (submission.Status == ActionSubmissionStatus.RoundResolved)
            {
                return submission;
            }

            if (!waitingShown)
            {
                _console.MarkupLine(
                    $"[yellow]◉[/] {Markup.Escape(GameText.WaitingOpponent)}");
                waitingShown = true;
            }
        }
    }

    private async Task<IncomingServerMessage> RequireMessageAsync(
        GameServerClient client,
        CancellationToken cancellationToken)
    {
        return await client.ReceiveAsync(cancellationToken)
            ?? throw new OnlineServerException(GameText.NetworkDisconnected);
    }

    private async Task<IncomingServerMessage> WaitForMessageTypeAsync(
        GameServerClient client,
        string expectedType,
        CancellationToken cancellationToken)
    {
        while (true)
        {
            IncomingServerMessage message = await RequireMessageAsync(client, cancellationToken);
            ThrowIfError(message);
            if (message.Type == expectedType)
            {
                return message;
            }
        }
    }

    private CombatAction PromptAction(int energy)
    {
        var choices = new List<ActionChoice>
        {
            new(CombatAction.Attack, $"⚔  {GameText.ActionAttack}"),
            new(CombatAction.Guard, $"⛨  {GameText.ActionGuard}"),
            new(CombatAction.Break, $"◆  {GameText.ActionBreak}")
        };
        if (energy >= StrategicBattle.HealEnergyCost)
        {
            choices.Add(new ActionChoice(CombatAction.Heal, $"✚  {GameText.ActionHeal}"));
        }

        ActionChoice selected = _prompts.Select(
            GameText.NetworkActionPrompt,
            choices,
            choice => choice.Label);
        return selected.Action;
    }

    private void ShowRoomAccess(RoomAccess access)
    {
        string mode = _profile.Mode == ServerConnectionMode.Lan
            ? GameText.NetworkModeLan
            : GameText.NetworkModeOnline;
        var panel = new Panel(
            new Rows(
                new Markup($"[bold green]{Markup.Escape(Format(GameText.RoomCreated, access.RoomCode))}[/]"),
                new Markup($"[grey]{Markup.Escape(Format(GameText.NetworkEndpoint, _profile.WebSocketEndpoint))}[/]"),
                new Markup($"[yellow]{Markup.Escape(GameText.RoomJoinHint)}[/]")))
        {
            Header = new PanelHeader($" {Markup.Escape(mode)} ", Justify.Center),
            Border = BoxBorder.Double,
            BorderStyle = new Style(Color.Cyan)
        };
        _console.Write(panel);
    }

    private void RenderBattle(OnlineMatchSnapshot snapshot, Guid selfId)
    {
        _console.Clear();
        _console.Write(new FigletText(GameText.AsciiTitle)
            .LeftJustified()
            .Color(Color.Cyan1));
        _console.Write(new Rule($"[yellow]{Markup.Escape(Format(
            GameText.NetworkRound,
            snapshot.RoundNumber))}[/]")
        {
            Style = Style.Parse("cyan")
        });

        PlayerSnapshot self = snapshot.Players.Single(player => player.PlayerId == selfId);
        PlayerSnapshot opponent = snapshot.Players.Single(player => player.PlayerId != selfId);
        _console.Write(new Columns(
            CreatePlayerPanel(self, true),
            CreatePlayerPanel(opponent, false)));
        _console.Write(new Rule($"[grey]{Markup.Escape(Format(
            GameText.NetworkRoom,
            snapshot.RoomCode))}[/]"));
        if (snapshot.ActionDeadlineUtc is DateTimeOffset deadline)
        {
            _console.MarkupLine($"[grey]◷ {Markup.Escape(Format(
                GameText.NetworkActionDeadline,
                RemainingSeconds(deadline)))}[/]");
        }
    }

    private static Panel CreatePlayerPanel(PlayerSnapshot player, bool isSelf)
    {
        string color = isSelf ? "cyan" : "magenta";
        string lockState = player.ActionLocked ? GameText.LockedLabel : GameText.ChoosingLabel;
        string healthBar = CreateHealthBar(player.Health, player.MaxHealth, 24);
        string energyBar = $"[yellow]{new string('◆', player.Energy)}[/][grey]{new string('◇', 3 - player.Energy)}[/]";
        var content = new Rows(
            new Markup($"[bold {color}]{Markup.Escape(player.DisplayName)}[/]"),
            new Markup($"{Markup.Escape(GameText.HealthLabel)}  {healthBar} [bold]{player.Health}/{player.MaxHealth}[/]"),
            new Markup($"{Markup.Escape(GameText.EnergyLabel)}  {energyBar}  {player.Energy}/3"),
            new Markup(player.ActionLocked
                ? $"[green]● {Markup.Escape(lockState)}[/]"
                : $"[grey]○ {Markup.Escape(lockState)}[/]"));
        return new Panel(content)
        {
            Border = BoxBorder.Rounded,
            BorderStyle = Style.Parse(color),
            Expand = true
        };
    }

    private void RenderRoundResult(
        OnlineMatchSnapshot snapshot,
        ResolvedRoundSnapshot round)
    {
        PlayerSnapshot host = snapshot.Players.Single(
            player => player.Slot == OnlinePlayerSlot.Host);
        PlayerSnapshot guest = snapshot.Players.Single(
            player => player.Slot == OnlinePlayerSlot.Guest);
        var rows = new List<IRenderable>
        {
            new Markup($"[bold cyan]{Markup.Escape(host.DisplayName)}[/]  {GetActionGlyph(round.HostAction)}  " +
                       $"[grey]VS[/]  {GetActionGlyph(round.GuestAction)}  " +
                       $"[bold magenta]{Markup.Escape(guest.DisplayName)}[/]")
        };
        AddResultLine(rows, host.DisplayName, round.HostDamageTaken, round.HostHealing);
        AddResultLine(rows, guest.DisplayName, round.GuestDamageTaken, round.GuestHealing);

        _console.Write(new Panel(new Rows(rows))
        {
            Header = new PanelHeader($" {Markup.Escape(Format(
                GameText.NetworkRoundResult,
                round.RoundNumber))} ", Justify.Center),
            Border = BoxBorder.Heavy,
            BorderStyle = Style.Parse("yellow")
        });
    }

    private void RenderWinner(OnlineMatchSnapshot snapshot, Guid selfId)
    {
        string result = snapshot.Outcome switch
        {
            BattleOutcome.Draw => GameText.NetworkDraw,
            BattleOutcome.HumanWin when snapshot.Players.Single(
                player => player.Slot == OnlinePlayerSlot.Host).PlayerId == selfId
                => GameText.NetworkWinnerYou,
            BattleOutcome.ComputerWin when snapshot.Players.Single(
                player => player.Slot == OnlinePlayerSlot.Guest).PlayerId == selfId
                => GameText.NetworkWinnerYou,
            _ => Format(
                GameText.NetworkWinnerOpponent,
                snapshot.Players.Single(player => player.PlayerId != selfId).DisplayName)
        };
        _console.Write(new Panel(Align.Center(
            new Markup($"[bold yellow]{Markup.Escape(result)}[/]")))
        {
            Border = BoxBorder.Double,
            BorderStyle = Style.Parse("yellow")
        });
    }

    private void RenderQuestionResult(QuestionSubmission submission, Guid selfId)
    {
        QuestionResolution resolution = submission.Resolution
            ?? throw new InvalidOperationException();
        string result;
        if (resolution.WinnerPlayerId == selfId)
        {
            result = GameText.QuestionWinnerYou;
        }
        else if (resolution.WinnerPlayerId is Guid winnerId)
        {
            string winnerName = submission.Snapshot.Players.Single(
                player => player.PlayerId == winnerId).DisplayName;
            result = Format(GameText.QuestionWinnerOpponent, winnerName);
        }
        else
        {
            result = GameText.QuestionNoWinner;
        }

        var rows = new List<IRenderable>
        {
            new Markup($"[bold yellow]{Markup.Escape(result)}[/]")
        };
        if (resolution.EnergyGranted > 0)
        {
            rows.Add(new Markup($"[green]{Markup.Escape(Format(
                GameText.QuestionEnergyReward,
                resolution.EnergyGranted))}[/]"));
        }

        rows.Add(new Markup($"[grey]{Markup.Escape(Format(
            GameText.QuestionExplanation,
            resolution.Explanation))}[/]"));
        _console.Write(new Panel(new Rows(rows))
        {
            Border = BoxBorder.Rounded,
            BorderStyle = Style.Parse("yellow")
        });
    }

    private void RenderLastChanceChallenge(LastChanceSnapshot challenge)
    {
        _console.Write(new Panel(new Rows(
            new Markup($"[bold red]{Markup.Escape(GameText.LastChanceTarget)}[/]"),
            new Markup($"[white]{Markup.Escape(challenge.Prompt)}[/]"),
            new Markup($"[yellow]{Markup.Escape(Format(
                GameText.LastChanceProgress,
                challenge.CurrentCount,
                challenge.RequiredCount,
                RemainingSeconds(challenge.DeadlineUtc)))}[/]")))
        {
            Header = new PanelHeader($" {Markup.Escape(GameText.LastChanceHeader)} ", Justify.Center),
            Border = BoxBorder.Double,
            BorderStyle = Style.Parse("red")
        });
    }

    private void RenderLastChanceResult(LastChanceSubmission submission)
    {
        string result;
        if (submission.Status == LastChanceSubmissionStatus.Succeeded)
        {
            result = Format(GameText.LastChanceSucceeded, submission.RestoredHealth);
        }
        else
        {
            result = GameText.LastChanceFailed;
        }

        _console.Write(new Panel(Align.Center(
            new Markup($"[bold {(submission.Status == LastChanceSubmissionStatus.Succeeded ? "green" : "red")}]{Markup.Escape(result)}[/]")))
        {
            Border = BoxBorder.Heavy,
            BorderStyle = Style.Parse(submission.Status == LastChanceSubmissionStatus.Succeeded
                ? "green"
                : "red")
        });
    }

    private static string CreateHealthBar(int health, int maximum, int width)
    {
        int filled = maximum == 0
            ? 0
            : Math.Clamp((int)Math.Round((double)health / maximum * width), 0, width);
        string color = health <= maximum * 0.2 ? "red" : health <= maximum * 0.5 ? "yellow" : "green";
        return $"[{color}]{new string('█', filled)}[/][grey]{new string('░', width - filled)}[/]";
    }

    private static string GetActionGlyph(CombatAction action)
    {
        return action switch
        {
            CombatAction.Attack => $"[red]⚔ {Markup.Escape(GameText.ActionAttack)}[/]",
            CombatAction.Guard => $"[blue]⛨ {Markup.Escape(GameText.ActionGuard)}[/]",
            CombatAction.Break => $"[yellow]◆ {Markup.Escape(GameText.ActionBreak)}[/]",
            CombatAction.Heal => $"[green]✚ {Markup.Escape(GameText.ActionHeal)}[/]",
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        };
    }

    private static string GetCategoryName(QuestionCategory category)
    {
        return category switch
        {
            QuestionCategory.Language => GameText.CategoryLanguage,
            QuestionCategory.Math => GameText.CategoryMath,
            QuestionCategory.English => GameText.CategoryEnglish,
            QuestionCategory.History => GameText.CategoryHistory,
            QuestionCategory.Geography => GameText.CategoryGeography,
            QuestionCategory.Biology => GameText.CategoryBiology,
            QuestionCategory.CommonSense => GameText.CategoryCommonSense,
            QuestionCategory.Logic => GameText.CategoryLogic,
            QuestionCategory.BrainTeaser => GameText.CategoryBrainTeaser,
            _ => throw new ArgumentOutOfRangeException(nameof(category))
        };
    }

    private static void AddResultLine(
        ICollection<IRenderable> rows,
        string displayName,
        int damage,
        int healing)
    {
        if (healing > 0)
        {
            rows.Add(new Markup($"[green]+[/] {Markup.Escape(Format(
                GameText.NetworkHealing,
                displayName,
                healing))}"));
        }

        if (damage > 0)
        {
            rows.Add(new Markup($"[red]-[/] {Markup.Escape(Format(
                GameText.NetworkDamage,
                displayName,
                damage))}"));
        }
    }

    private static void ThrowIfError(IncomingServerMessage message)
    {
        if (message.Type == ServerMessageTypes.Error)
        {
            ProtocolError error = message.ReadPayload<ProtocolError>();
            throw new OnlineServerException(Format(GameText.NetworkServerError, error.Code));
        }
    }

    private static string CreateRequestId() => Guid.NewGuid().ToString("N");

    private static int RemainingSeconds(DateTimeOffset deadlineUtc)
    {
        return Math.Max(0, (int)Math.Ceiling((deadlineUtc - DateTimeOffset.UtcNow).TotalSeconds));
    }

    private static bool IsWinner(OnlineMatchSnapshot snapshot, Guid playerId)
    {
        if (snapshot.Outcome == BattleOutcome.Draw)
        {
            return false;
        }

        OnlinePlayerSlot winningSlot = snapshot.Outcome == BattleOutcome.HumanWin
            ? OnlinePlayerSlot.Host
            : OnlinePlayerSlot.Guest;
        return snapshot.Players.Single(player => player.Slot == winningSlot).PlayerId
            == playerId;
    }

    private static string Format(string template, params object[] arguments)
        => string.Format(template, arguments);

    private sealed record ActionChoice(CombatAction Action, string Label);
    private sealed record QuestionChoice(int Index, string Option);
    private sealed record TagChoice(string? Code, string DisplayName);

    private sealed class OnlineServerException : Exception
    {
        public OnlineServerException(string message)
            : base(message)
        {
        }
    }
}
