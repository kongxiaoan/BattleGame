using BattleGame.Core;

namespace BattleGame.Tests;

public sealed class DifficultyPolicyTests
{
    private static readonly CombatAction[] RankedActions =
    {
        CombatAction.Break,
        CombatAction.Attack,
        CombatAction.Guard
    };

    [TestCase(GameDifficulty.Easy, 39, CombatAction.Break)]
    [TestCase(GameDifficulty.Easy, 40, CombatAction.Attack)]
    [TestCase(GameDifficulty.Medium, 69, CombatAction.Break)]
    [TestCase(GameDifficulty.Medium, 70, CombatAction.Attack)]
    [TestCase(GameDifficulty.Hard, 89, CombatAction.Break)]
    [TestCase(GameDifficulty.Hard, 90, CombatAction.Attack)]
    public void Select_UsesCodeControlledOptimalActionThreshold(
        GameDifficulty difficulty,
        int roll,
        CombatAction expected)
    {
        CombatAction selected = DifficultyPolicy.Select(RankedActions, difficulty, roll);

        Assert.That(selected, Is.EqualTo(expected));
    }

    [Test]
    public void Select_FiltersOutActionsThatAreNotCurrentlyLegal()
    {
        CombatAction selected = DifficultyPolicy.Select(
            RankedActions,
            GameDifficulty.Hard,
            roll: 0,
            legalActions: new[] { CombatAction.Attack, CombatAction.Guard });

        Assert.That(selected, Is.EqualTo(CombatAction.Attack));
    }
}
