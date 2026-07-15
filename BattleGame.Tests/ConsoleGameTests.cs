using BattleGame.Cli;

namespace BattleGame.Tests;

public sealed class ConsoleGameTests
{
    [Test]
    public void Run_WithRepeatedAttacks_CompletesBattleAndPrintsHumanWinner()
    {
        using var input = new StringReader("勇者\n1\n1\n1\n1\n1\n");
        using var output = new StringWriter();
        var game = new ConsoleGame(input, output);

        game.Run();

        string transcript = output.ToString();
        Assert.Multiple(() =>
        {
            Assert.That(transcript, Does.Contain("勇者 对战 电脑"));
            Assert.That(transcript, Does.Contain("勇者 发起攻击"));
            Assert.That(transcript, Does.Contain("胜者：勇者"));
        });
    }

    [Test]
    public void Run_WithInvalidNameAndAction_RePromptsWithoutAdvancingBattle()
    {
        using var input = new StringReader("   \n勇者\n错误指令\n1\n1\n1\n1\n1\n");
        using var output = new StringWriter();
        var game = new ConsoleGame(input, output);

        game.Run();

        string transcript = output.ToString();
        Assert.Multiple(() =>
        {
            Assert.That(transcript, Does.Contain("名字不能为空"));
            Assert.That(transcript, Does.Contain("请输入 1 或 2"));
            Assert.That(CountOccurrences(transcript, "电脑 发起攻击"), Is.EqualTo(4));
            Assert.That(transcript, Does.Contain("胜者：勇者"));
        });
    }

    [Test]
    public void Run_WithHealAction_HealsHumanAndThenRunsComputerTurn()
    {
        // 先攻击一次并承受伤害，第二个玩家回合才具有可观察的治疗效果。
        using var input = new StringReader("勇者\n1\n2\n1\n1\n1\n1\n");
        using var output = new StringWriter();
        var game = new ConsoleGame(input, output);

        game.Run();

        string transcript = output.ToString();
        Assert.Multiple(() =>
        {
            Assert.That(transcript, Does.Contain("勇者 恢复 15 点生命"));
            Assert.That(transcript, Does.Contain("当前生命：95/100"));
            Assert.That(transcript, Does.Contain("电脑 发起攻击"));
            Assert.That(transcript, Does.Contain("胜者：勇者"));
        });
    }

    [Test]
    public void Run_WithHealAtFullHealth_PrintsZeroActualHealing()
    {
        // 后续持续攻击以正常结束游戏，避免测试停留在半局状态。
        using var input = new StringReader("勇者\n2\n1\n1\n1\n1\n1\n");
        using var output = new StringWriter();
        var game = new ConsoleGame(input, output);

        game.Run();

        Assert.That(output.ToString(), Does.Contain("勇者 恢复 0 点生命"));
    }

    [Test]
    public void Run_WhenHumanOnlyHeals_EventuallyPrintsComputerWinner()
    {
        string healingActions = string.Join('\n', Enumerable.Repeat("2", 20));
        using var input = new StringReader($"勇者\n{healingActions}\n");
        using var output = new StringWriter();
        var game = new ConsoleGame(input, output);

        game.Run();

        Assert.Multiple(() =>
        {
            Assert.That(output.ToString(), Does.Contain("胜者：电脑"));
            Assert.That(output.ToString(), Does.Contain("勇者 当前生命：0/100"));
        });
    }

    private static int CountOccurrences(string source, string value)
    {
        return source.Split(value, StringSplitOptions.None).Length - 1;
    }
}
