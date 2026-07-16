using BattleGame.Core;

namespace BattleGame.Tests;

public sealed class StrategicBattleTests
{
    [Test]
    public void ResolveRound_WhenBothAttack_DamagesBothAtTheSameTime()
    {
        var battle = CreateBattle();

        RoundResult result = battle.ResolveRound(CombatAction.Attack, CombatAction.Attack);

        Assert.Multiple(() =>
        {
            Assert.That(battle.Human.Health, Is.EqualTo(80));
            Assert.That(battle.Computer.Health, Is.EqualTo(80));
            Assert.That(result.HumanDamageTaken, Is.EqualTo(20));
            Assert.That(result.ComputerDamageTaken, Is.EqualTo(20));
            Assert.That(battle.RoundNumber, Is.EqualTo(1));
        });
    }

    [Test]
    public void ResolveRound_WhenGuardMeetsAttack_ReducesDamageAndGainsEnergy()
    {
        var battle = CreateBattle();

        battle.ResolveRound(CombatAction.Guard, CombatAction.Attack);

        Assert.Multiple(() =>
        {
            Assert.That(battle.Human.Health, Is.EqualTo(95));
            Assert.That(battle.Human.Energy, Is.EqualTo(1));
            Assert.That(battle.Computer.Health, Is.EqualTo(100));
        });
    }

    [Test]
    public void ResolveRound_WhenBreakMeetsGuard_DealsPunishingDamage()
    {
        var battle = CreateBattle();

        battle.ResolveRound(CombatAction.Break, CombatAction.Guard);

        Assert.That(battle.Computer.Health, Is.EqualTo(70));
    }

    [Test]
    public void ResolveRound_WhenHealingWithoutEnergy_ThrowsWithoutChangingState()
    {
        var battle = CreateBattle();

        Assert.That(
            () => battle.ResolveRound(CombatAction.Heal, CombatAction.Attack),
            Throws.TypeOf<InvalidOperationException>());
        Assert.Multiple(() =>
        {
            Assert.That(battle.RoundNumber, Is.Zero);
            Assert.That(battle.Human.Health, Is.EqualTo(100));
            Assert.That(battle.Computer.Health, Is.EqualTo(100));
        });
    }

    [Test]
    public void ResolveRound_WhenBothTakeLethalDamage_RecordsDraw()
    {
        var human = new Combatant("玩家", maxHealth: 100, currentHealth: 20);
        var computer = new Combatant("电脑", maxHealth: 100, currentHealth: 20);
        var battle = new StrategicBattle(human, computer);

        battle.ResolveRound(CombatAction.Attack, CombatAction.Attack);

        Assert.That(battle.Outcome, Is.EqualTo(BattleOutcome.Draw));
    }

    [Test]
    public void ApplyEvent_WithDamageEvent_NeverKillsCombatant()
    {
        var human = new Combatant("玩家", maxHealth: 100, currentHealth: 10);
        var battle = new StrategicBattle(human, new Combatant("电脑"));
        var battleEvent = BattleEvent.CreateHealthLoss(BattleSide.Human, 15, "血月侵蚀");

        battle.ApplyEvent(battleEvent);

        Assert.That(human.Health, Is.EqualTo(1));
    }

    [Test]
    public void UseAppeal_AllowsEachSideOnlyOnce()
    {
        var battle = CreateBattle();

        battle.UseAppeal(BattleSide.Human);
        battle.UseAppeal(BattleSide.Computer);

        Assert.Multiple(() =>
        {
            Assert.That(battle.CanAppeal(BattleSide.Human), Is.False);
            Assert.That(battle.CanAppeal(BattleSide.Computer), Is.False);
            Assert.That(
                () => battle.UseAppeal(BattleSide.Human),
                Throws.TypeOf<InvalidOperationException>());
        });
    }

    private static StrategicBattle CreateBattle()
    {
        return new StrategicBattle(new Combatant("玩家"), new Combatant("电脑"));
    }
}
