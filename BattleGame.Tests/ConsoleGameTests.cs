using BattleGame.Cli;
using BattleGame.Cli.Ai;
using BattleGame.Core;

namespace BattleGame.Tests;

public sealed class ConsoleGameTests
{
    [Test]
    public void Run_WithSimultaneousAttacks_PrintsStrategicBattleAndDraw()
    {
        using var input = new StringReader("勇者\n1\n2\n1\n1\n1\n1\n1\n");
        using var output = new StringWriter();
        var intelligence = new FakeIntelligence(
            BattleEvent.CreateEnergyGain(BattleSide.Human, "星火赐予玩家能量"));
        var game = new ConsoleGame(
            input,
            output,
            intelligence,
            intelligence,
            intelligence,
            new FixedRandomSource(0),
            useColors: false);

        game.Run();

        string transcript = output.ToString();
        Assert.Multiple(() =>
        {
            Assert.That(transcript, Does.Contain("AI 命运法庭"));
            Assert.That(transcript, Does.Contain("第 1 回合"));
            Assert.That(transcript, Does.Contain("双方同时选择了攻击"));
            Assert.That(transcript, Does.Contain("星火赐予玩家能量"));
            Assert.That(transcript, Does.Contain("双方同时倒下，本局平局"));
        });
    }

    [Test]
    public void Run_WhenHumanAppealIsAccepted_CancelsAdverseEvent()
    {
        using var input = new StringReader(
            "勇者\n2\n2\n1\n1\n1\ny\n事件连续针对我，违反公平规则\n1\n1\n");
        using var output = new StringWriter();
        var intelligence = new FakeIntelligence(
            BattleEvent.CreateHealthLoss(BattleSide.Human, 12, "血月侵蚀勇者"),
            new AppealVerdict(AppealDecision.Revoke, "FAIRNESS", "申诉成立，撤销事件。"));
        var game = new ConsoleGame(
            input,
            output,
            intelligence,
            intelligence,
            intelligence,
            new FixedRandomSource(0),
            useColors: false);

        game.Run();

        Assert.Multiple(() =>
        {
            Assert.That(output.ToString(), Does.Contain("申诉成立，撤销事件"));
            Assert.That(output.ToString(), Does.Contain("申诉机会：已使用"));
        });
    }

    [Test]
    public void Run_WhenHealHasNoEnergy_RePromptsWithoutAdvancingRound()
    {
        using var input = new StringReader("勇者\n3\n2\n4\n1\n1\n1\n1\n1\n");
        using var output = new StringWriter();
        var intelligence = new FakeIntelligence(
            BattleEvent.CreateEnergyGain(BattleSide.Human, "能量涌现"));
        var game = new ConsoleGame(
            input,
            output,
            intelligence,
            intelligence,
            intelligence,
            new FixedRandomSource(0),
            useColors: false);

        game.Run();

        Assert.Multiple(() =>
        {
            Assert.That(output.ToString(), Does.Contain("能量不足，治疗需要 2 点能量"));
            Assert.That(CountOccurrences(output.ToString(), "第 1 回合"), Is.EqualTo(1));
        });
    }

    private static int CountOccurrences(string source, string value)
    {
        return source.Split(value, StringSplitOptions.None).Length - 1;
    }

    private sealed class FixedRandomSource : IRandomSource
    {
        private readonly int _value;

        public FixedRandomSource(int value)
        {
            _value = value;
        }

        public int Next(int maximumExclusive)
        {
            return Math.Min(_value, maximumExclusive - 1);
        }
    }

    private sealed class FakeIntelligence : IOpponentAgent, IEventDirector, IAppealJudge
    {
        private readonly BattleEvent _battleEvent;
        private readonly AppealVerdict _verdict;

        public FakeIntelligence(BattleEvent battleEvent, AppealVerdict? verdict = null)
        {
            _battleEvent = battleEvent;
            _verdict = verdict ?? new AppealVerdict(
                AppealDecision.Uphold,
                "NO_APPEAL",
                "维持事件。");
        }

        public bool IsOnline => true;

        public Task<IReadOnlyList<CombatAction>> RankActionsAsync(
            BattleSnapshot snapshot,
            IReadOnlyList<CombatAction> legalActions,
            GameDifficulty difficulty,
            CancellationToken cancellationToken)
        {
            return Task.FromResult<IReadOnlyList<CombatAction>>(
                new[] { CombatAction.Attack, CombatAction.Guard, CombatAction.Break });
        }

        public Task<AppealClaim> ConsiderAppealAsync(
            BattleSnapshot snapshot,
            BattleEvent battleEvent,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(new AppealClaim(false, string.Empty));
        }

        public Task<BattleEvent> CreateEventAsync(
            BattleSnapshot snapshot,
            BattleSide favoredSide,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_battleEvent);
        }

        public Task<AppealVerdict> JudgeAsync(
            BattleSnapshot snapshot,
            BattleEvent battleEvent,
            BattleSide appellant,
            string argument,
            CancellationToken cancellationToken)
        {
            return Task.FromResult(_verdict);
        }
    }
}
