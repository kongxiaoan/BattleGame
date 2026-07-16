using BattleGame.Cli.Ai;
using BattleGame.Core;

namespace BattleGame.Cli;

/// <summary>
/// AI 命运法庭的控制台编排层。领域状态、外部 AI 与输入输出通过接口隔离，便于完整流程测试。
/// </summary>
public sealed class ConsoleGame
{
    private const int MaximumEvents = 2;

    private readonly TextReader _input;
    private readonly TextWriter _output;
    private readonly IOpponentAgent _opponent;
    private readonly IEventDirector _eventDirector;
    private readonly IAppealJudge _judge;
    private readonly IRandomSource _random;
    private readonly ConsoleTheme _theme;
    private readonly List<string> _history = new();

    public ConsoleGame(TextReader input, TextWriter output)
        : this(input, output, CreateDefaultServices(), !Console.IsOutputRedirected)
    {
    }

    private ConsoleGame(
        TextReader input,
        TextWriter output,
        GameServices services,
        bool useColors)
        : this(
            input,
            output,
            services.Opponent,
            services.EventDirector,
            services.Judge,
            services.Random,
            useColors)
    {
    }

    public ConsoleGame(
        TextReader input,
        TextWriter output,
        IOpponentAgent opponent,
        IEventDirector eventDirector,
        IAppealJudge judge,
        IRandomSource random,
        bool useColors = true)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _output = output ?? throw new ArgumentNullException(nameof(output));
        _opponent = opponent ?? throw new ArgumentNullException(nameof(opponent));
        _eventDirector = eventDirector ?? throw new ArgumentNullException(nameof(eventDirector));
        _judge = judge ?? throw new ArgumentNullException(nameof(judge));
        _random = random ?? throw new ArgumentNullException(nameof(random));
        _theme = new ConsoleTheme(output, useColors);
    }

    public void Run()
    {
        RunAsync(CancellationToken.None).GetAwaiter().GetResult();
    }

    public async Task RunAsync(CancellationToken cancellationToken)
    {
        _theme.WriteBanner(GameText.Title, GameText.Subtitle);
        string? playerName = ReadPlayerName();
        if (playerName == null)
        {
            WriteLine(GameText.InputEnded);
            return;
        }

        GameDifficulty? difficulty = ReadDifficulty();
        FateMode? fateMode = ReadFateMode();
        if (difficulty == null || fateMode == null)
        {
            WriteLine(GameText.InputEnded);
            return;
        }

        var battle = new StrategicBattle(
            new Combatant(playerName),
            new Combatant(GameText.ComputerName));
        WriteLine(_opponent.IsOnline ? GameText.AiOnline : GameText.AiOffline);
        _theme.WriteSection(Format(GameText.BattleIntro, battle.Human.Name, battle.Computer.Name));
        WriteLine(GameText.BattleRules);

        int eventCount = 0;
        while (!battle.IsFinished)
        {
            _theme.WriteSection(Format(GameText.RoundHeader, battle.RoundNumber + 1));
            PrintStatus(battle);

            BattleSnapshot snapshot = CreateSnapshot(battle);
            IReadOnlyList<CombatAction> computerLegal = battle.GetLegalActions(BattleSide.Computer);
            WriteLine(GameText.AiThinking);
            // 电脑在读取玩家选择前开始决策，保证秘密同时选招，不窥视本回合输入。
            Task<IReadOnlyList<CombatAction>> rankingTask = _opponent.RankActionsAsync(
                snapshot,
                computerLegal,
                difficulty.Value,
                cancellationToken);

            CombatAction? humanAction = ReadHumanAction(battle);
            if (humanAction == null)
            {
                WriteLine(GameText.InputEnded);
                return;
            }

            IReadOnlyList<CombatAction> ranking = await rankingTask;
            CombatAction computerAction = DifficultyPolicy.Select(
                ranking,
                difficulty.Value,
                _random.Next(100),
                computerLegal);
            RoundResult result = battle.ResolveRound(humanAction.Value, computerAction);
            PrintRoundResult(battle, result);
            _history.Add($"H:{humanAction.Value}/C:{computerAction}");

            if (!battle.IsFinished && battle.RoundNumber % 3 == 0 && eventCount < MaximumEvents)
            {
                await ResolveEventAsync(battle, fateMode.Value, cancellationToken);
                eventCount++;
            }
        }

        _theme.WriteSection(GameText.ResultHeader);
        switch (battle.Outcome)
        {
            case BattleOutcome.HumanWin:
                WriteLine(Format(GameText.HumanWinner, battle.Human.Name));
                break;
            case BattleOutcome.ComputerWin:
                WriteLine(Format(GameText.ComputerWinner, battle.Computer.Name));
                break;
            case BattleOutcome.Draw:
                WriteLine(GameText.Draw);
                break;
        }

        WriteLine(GameText.Farewell);
    }

    private async Task ResolveEventAsync(
        StrategicBattle battle,
        FateMode fateMode,
        CancellationToken cancellationToken)
    {
        int humanBenefitThreshold = fateMode switch
        {
            FateMode.Heroic => 65,
            FateMode.Fair => 50,
            FateMode.Harsh => 35,
            _ => throw new ArgumentOutOfRangeException(nameof(fateMode))
        };
        BattleSide favoredSide = _random.Next(100) < humanBenefitThreshold
            ? BattleSide.Human
            : BattleSide.Computer;
        BattleSnapshot snapshot = CreateSnapshot(battle);
        BattleEvent battleEvent = await _eventDirector.CreateEventAsync(
            snapshot,
            favoredSide,
            cancellationToken);

        _theme.WriteSection(GameText.EventHeader);
        WriteLine(Format(GameText.EventProposal, battleEvent.Narrative));
        WriteLine(Format(
            GameText.EventEffect,
            GetSideName(battle, battleEvent.Target),
            DescribeEffect(battleEvent)));

        AppealClaim? claim = null;
        BattleSide appellant = battleEvent.DisadvantagedSide;
        if (battle.CanAppeal(appellant))
        {
            claim = appellant == BattleSide.Human
                ? ReadHumanAppeal()
                : await _opponent.ConsiderAppealAsync(snapshot, battleEvent, cancellationToken);
        }

        if (claim is { ShouldAppeal: true })
        {
            battle.UseAppeal(appellant);
            if (appellant == BattleSide.Computer)
            {
                WriteLine(Format(GameText.ComputerAppeal, battle.Computer.Name, claim.Argument));
            }

            WriteLine(GameText.JudgeThinking);
            AppealVerdict verdict = await _judge.JudgeAsync(
                snapshot,
                battleEvent,
                appellant,
                claim.Argument,
                cancellationToken);
            WriteLine(verdict.Decision == AppealDecision.Revoke
                ? GameText.VerdictRevoke
                : GameText.VerdictUphold);
            WriteLine(Format(GameText.VerdictReason, verdict.Explanation));
            if (verdict.Decision == AppealDecision.Revoke)
            {
                WriteLine(GameText.EventCancelled);
                return;
            }
        }

        battle.ApplyEvent(battleEvent);
        WriteLine(GameText.EventApplied);
    }

    private AppealClaim ReadHumanAppeal()
    {
        _output.Write(GameText.HumanAppealPrompt);
        string? answer = _input.ReadLine();
        bool shouldAppeal = answer != null &&
                            (answer.Equals("y", StringComparison.OrdinalIgnoreCase) || answer == "是");
        if (!shouldAppeal)
        {
            return new AppealClaim(false, string.Empty);
        }

        _output.Write(GameText.AppealArgumentPrompt);
        string argument = _input.ReadLine() ?? string.Empty;
        if (argument.Length > 200)
        {
            argument = argument.Substring(0, 200);
        }

        return new AppealClaim(true, argument);
    }

    private string? ReadPlayerName()
    {
        while (true)
        {
            _output.Write(GameText.NamePrompt);
            string? value = _input.ReadLine();
            if (value == null)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(value))
            {
                return value.Trim();
            }

            WriteLine(GameText.NameEmpty);
        }
    }

    private GameDifficulty? ReadDifficulty()
    {
        return ReadEnumChoice<GameDifficulty>(GameText.DifficultyPrompt);
    }

    private FateMode? ReadFateMode()
    {
        return ReadEnumChoice<FateMode>(GameText.FatePrompt);
    }

    private TEnum? ReadEnumChoice<TEnum>(string prompt)
        where TEnum : struct, Enum
    {
        while (true)
        {
            _output.Write(prompt);
            string? value = _input.ReadLine();
            if (value == null)
            {
                return null;
            }

            if (int.TryParse(value, out int number) &&
                Enum.IsDefined(typeof(TEnum), number))
            {
                return (TEnum)Enum.ToObject(typeof(TEnum), number);
            }

            WriteLine(GameText.InvalidChoice);
        }
    }

    private CombatAction? ReadHumanAction(StrategicBattle battle)
    {
        while (true)
        {
            _output.Write(GameText.ActionPrompt);
            string? value = _input.ReadLine();
            if (value == null)
            {
                return null;
            }

            if (!int.TryParse(value, out int number) ||
                !Enum.IsDefined(typeof(CombatAction), number))
            {
                WriteLine(GameText.InvalidChoice);
                continue;
            }

            var action = (CombatAction)number;
            if (action == CombatAction.Heal &&
                !battle.GetLegalActions(BattleSide.Human).Contains(action))
            {
                WriteLine(GameText.HealEnergyMissing);
                continue;
            }

            return action;
        }
    }

    private void PrintRoundResult(StrategicBattle battle, RoundResult result)
    {
        string humanAction = GetActionName(result.HumanAction);
        string computerAction = GetActionName(result.ComputerAction);
        WriteLine(result.HumanAction == result.ComputerAction
            ? Format(GameText.BothAction, humanAction)
            : Format(
                GameText.ActionReveal,
                battle.Human.Name,
                humanAction,
                battle.Computer.Name,
                computerAction));

        if (result.HumanHealing > 0)
        {
            WriteLine(Format(GameText.HealingLine, battle.Human.Name, result.HumanHealing));
        }

        if (result.ComputerHealing > 0)
        {
            WriteLine(Format(GameText.HealingLine, battle.Computer.Name, result.ComputerHealing));
        }

        if (result.HumanDamageTaken > 0)
        {
            WriteLine(Format(GameText.DamageLine, battle.Human.Name, result.HumanDamageTaken));
        }

        if (result.ComputerDamageTaken > 0)
        {
            WriteLine(Format(GameText.DamageLine, battle.Computer.Name, result.ComputerDamageTaken));
        }

        if (result.HumanAction == CombatAction.Guard &&
            result.ComputerAction == CombatAction.Attack)
        {
            WriteLine(Format(GameText.GuardEnergy, battle.Human.Name));
        }

        if (result.ComputerAction == CombatAction.Guard &&
            result.HumanAction == CombatAction.Attack)
        {
            WriteLine(Format(GameText.GuardEnergy, battle.Computer.Name));
        }
    }

    private void PrintStatus(StrategicBattle battle)
    {
        PrintCombatant(battle.Human, battle.CanAppeal(BattleSide.Human));
        PrintCombatant(battle.Computer, battle.CanAppeal(BattleSide.Computer));
    }

    private void PrintCombatant(Combatant value, bool appealAvailable)
    {
        WriteLine(Format(GameText.StatusName, value.Name));
        WriteLine(Format(
            GameText.StatusHealth,
            value.Health,
            value.MaxHealth,
            ConsoleTheme.CreateBar(value.Health, value.MaxHealth, 20)));
        WriteLine(Format(
            GameText.StatusEnergy,
            value.Energy,
            ConsoleTheme.CreateEnergyBar(value.Energy)));
        WriteLine(Format(
            GameText.StatusAppeal,
            appealAvailable ? GameText.AppealAvailable : GameText.AppealUsed));
    }

    private BattleSnapshot CreateSnapshot(StrategicBattle battle)
    {
        return new BattleSnapshot(battle, string.Join(" | ", _history.TakeLast(6)));
    }

    private static string GetSideName(StrategicBattle battle, BattleSide side)
    {
        return side == BattleSide.Human ? battle.Human.Name : battle.Computer.Name;
    }

    private static string DescribeEffect(BattleEvent battleEvent)
    {
        return battleEvent.Type switch
        {
            BattleEventType.RestoreHealth => Format(GameText.EffectRestore, battleEvent.Magnitude),
            BattleEventType.LoseHealth => Format(GameText.EffectLoss, battleEvent.Magnitude),
            BattleEventType.GainEnergy => Format(GameText.EffectEnergy, battleEvent.Magnitude),
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    private static string GetActionName(CombatAction action)
    {
        return action switch
        {
            CombatAction.Attack => GameText.ActionAttack,
            CombatAction.Guard => GameText.ActionGuard,
            CombatAction.Break => GameText.ActionBreak,
            CombatAction.Heal => GameText.ActionHeal,
            _ => throw new ArgumentOutOfRangeException(nameof(action))
        };
    }

    private void WriteLine(string value)
    {
        _theme.WriteLine(value);
    }

    private static string Format(string template, params object[] arguments)
    {
        return string.Format(template, arguments);
    }

    private static GameServices CreateDefaultServices()
    {
        var random = new SystemRandomSource();
        var local = new LocalGameIntelligence(random);
        string? apiKey = Environment.GetEnvironmentVariable("DEEPSEEK_API_KEY");
        if (string.IsNullOrWhiteSpace(apiKey))
        {
            return new GameServices(local, local, local, random);
        }

        string model = Environment.GetEnvironmentVariable("DEEPSEEK_MODEL") ?? "deepseek-chat";
        var httpClient = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        var client = new DeepSeekJsonClient(httpClient, apiKey, model);
        return new GameServices(
            new DeepSeekOpponentAgent(client, local),
            new DeepSeekEventDirector(client, local),
            new DeepSeekAppealJudge(client, local),
            random);
    }

    private sealed class GameServices
    {
        public GameServices(
            IOpponentAgent opponent,
            IEventDirector eventDirector,
            IAppealJudge judge,
            IRandomSource random)
        {
            Opponent = opponent;
            EventDirector = eventDirector;
            Judge = judge;
            Random = random;
        }

        public IOpponentAgent Opponent { get; }
        public IEventDirector EventDirector { get; }
        public IAppealJudge Judge { get; }
        public IRandomSource Random { get; }
    }
}
