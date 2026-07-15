using BattleGame.Core;

namespace BattleGame.Cli;

/// <summary>
/// 组织一局玩家对电脑的控制台战斗。
/// 输入和输出通过构造函数传入，使交互流程无需启动真实控制台也能进行测试。
/// </summary>
public sealed class ConsoleGame
{
    private const int MaxHealth = 100;
    private const int AttackPower = 20;
    private const int HealingAmount = 15;

    private readonly TextReader _input;
    private readonly TextWriter _output;

    public ConsoleGame(TextReader input, TextWriter output)
    {
        _input = input ?? throw new ArgumentNullException(nameof(input));
        _output = output ?? throw new ArgumentNullException(nameof(output));
    }

    public void Run()
    {
        WriteLine(GameText.Title);
        string? playerName = ReadPlayerName();
        if (playerName == null)
        {
            WriteLine(GameText.InputEnded);
            return;
        }

        var human = new Player(playerName, MaxHealth, AttackPower);
        var computer = new Player(GameText.ComputerName, MaxHealth, AttackPower);
        var battle = new Battle(human, computer);

        WriteLine(Format(GameText.Versus, human.Name, computer.Name));
        WriteLine(Format(GameText.Rules, MaxHealth, AttackPower, HealingAmount));

        while (!battle.IsFinished)
        {
            PrintStatus(human, computer);
            BattleAction? action = ReadHumanAction();
            if (action == null)
            {
                WriteLine(GameText.InputEnded);
                return;
            }

            ExecuteAndDescribeHumanTurn(battle, human, computer, action.Value);
            if (battle.IsFinished)
            {
                break;
            }

            ExecuteAndDescribeComputerTurn(battle, human);
        }

        WriteLine(Format(GameText.Winner, battle.Winner!.Name));
    }

    private string? ReadPlayerName()
    {
        while (true)
        {
            _output.Write(GameText.NamePrompt);
            string? name = _input.ReadLine();
            if (name == null)
            {
                return null;
            }

            if (!string.IsNullOrWhiteSpace(name))
            {
                return name.Trim();
            }

            WriteLine(GameText.NameEmpty);
        }
    }

    private BattleAction? ReadHumanAction()
    {
        while (true)
        {
            _output.Write(GameText.ActionPrompt);
            string? value = _input.ReadLine();
            if (value == null)
            {
                return null;
            }

            if (value == "1")
            {
                return BattleAction.Attack;
            }

            if (value == "2")
            {
                return BattleAction.Heal;
            }

            // 无效输入不调用 Battle，因此不会意外消耗玩家回合。
            WriteLine(GameText.InvalidAction);
        }
    }

    private void ExecuteAndDescribeHumanTurn(
        Battle battle,
        Player human,
        Player computer,
        BattleAction action)
    {
        if (action == BattleAction.Attack)
        {
            WriteLine(Format(GameText.Attack, human.Name, human.AttackPower));
            battle.ExecuteTurn(BattleAction.Attack, HealingAmount);
            WriteLine(Format(GameText.Health, computer.Name, computer.Health, computer.MaxHealth));
            return;
        }

        int healthBeforeHealing = human.Health;
        battle.ExecuteTurn(BattleAction.Heal, HealingAmount);
        // 展示生命值差值而不是配置值，满血治疗时才能准确显示实际效果。
        int actualHealing = human.Health - healthBeforeHealing;
        WriteLine(Format(GameText.Heal, human.Name, actualHealing));
        WriteLine(Format(GameText.Health, human.Name, human.Health, human.MaxHealth));
    }

    private void ExecuteAndDescribeComputerTurn(Battle battle, Player human)
    {
        Player computer = battle.CurrentAttacker;
        WriteLine(Format(GameText.Attack, computer.Name, computer.AttackPower));
        battle.ExecuteTurn(BattleAction.Attack, HealingAmount);
        WriteLine(Format(GameText.Health, human.Name, human.Health, human.MaxHealth));
    }

    private void PrintStatus(Player human, Player computer)
    {
        WriteLine(string.Empty);
        WriteLine(Format(
            GameText.Status,
            human.Name,
            human.Health,
            human.MaxHealth,
            computer.Name,
            computer.Health,
            computer.MaxHealth));
    }

    private void WriteLine(string value)
    {
        _output.WriteLine(value);
    }

    private static string Format(string template, params object[] arguments)
    {
        return string.Format(template, arguments);
    }
}
