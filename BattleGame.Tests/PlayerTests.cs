using BattleGame.Core;

namespace BattleGame.Tests;

public sealed class PlayerTests
{
    [Test]
    public void TakeDamage_WithPositiveDamage_ReducesHealth()
    {
        var player = new Player(nameof(Player), 100, 20);

        player.TakeDamage(30);

        // 只验证外部可观察结果，不依赖 Player 内部如何保存生命值。
        Assert.That(player.Health, Is.EqualTo(70));
    }

    [Test]
    public void TakeDamage_WithLethalDamage_ClampsHealthToZeroAndKillsPlayer()
    {
        var player = new Player(nameof(Player), 100, 20);

        player.TakeDamage(150);

        Assert.Multiple(() =>
        {
            Assert.That(player.Health, Is.Zero);
            Assert.That(player.IsAlive, Is.False);
        });
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void TakeDamage_WithNonPositiveDamage_ThrowsArgumentOutOfRangeException(
        int damage)
    {
        var player = new Player(nameof(Player), 100, 20);

        Assert.That(
            () => player.TakeDamage(damage),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void Heal_WhenPlayerIsDamaged_IncreasesHealth()
    {
        var player = new Player(nameof(Player), 100, 20);

        // 先制造非致命伤，避免死亡规则干扰普通治疗场景。
        player.TakeDamage(40);
        player.Heal(15);

        Assert.That(player.Health, Is.EqualTo(75));
    }

    [TestCase(50)]
    [TestCase(int.MaxValue)]
    public void Heal_WhenAmountExceedsMissingHealth_ClampsAtMaxHealth(int amount)
    {
        var player = new Player(nameof(Player), 100, 20);
        player.TakeDamage(10);

        player.Heal(amount);

        Assert.That(player.Health, Is.EqualTo(player.MaxHealth));
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void Heal_WithNonPositiveAmount_ThrowsArgumentOutOfRangeException(int amount)
    {
        var player = new Player(nameof(Player), 100, 20);

        Assert.That(
            () => player.Heal(amount),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }

    [Test]
    public void Heal_WhenPlayerIsDead_ThrowsInvalidOperationException()
    {
        var player = new Player(nameof(Player), 100, 20);
        player.TakeDamage(100);

        Assert.That(
            () => player.Heal(15),
            Throws.TypeOf<InvalidOperationException>());
        Assert.That(player.Health, Is.Zero);
    }

    [Test]
    public void Attack_WhenBothPlayersAreAlive_ReducesTargetHealthByAttackPower()
    {
        var attacker = new Player(nameof(Player), 100, 20);
        var target = new Player(nameof(Player), 100, 10);

        attacker.Attack(target);

        Assert.That(target.Health, Is.EqualTo(80));
    }

    [Test]
    public void Attack_WhenAttackerIsDead_ThrowsInvalidOperationException()
    {
        var attacker = new Player(nameof(Player), 100, 20);
        var target = new Player(nameof(Player), 100, 10);
        attacker.TakeDamage(attacker.MaxHealth);

        Assert.That(
            () => attacker.Attack(target),
            Throws.TypeOf<InvalidOperationException>());
        Assert.That(target.Health, Is.EqualTo(target.MaxHealth));
    }

    [Test]
    public void Attack_WhenTargetIsDead_ThrowsInvalidOperationException()
    {
        var attacker = new Player(nameof(Player), 100, 20);
        var target = new Player(nameof(Player), 100, 10);
        target.TakeDamage(target.MaxHealth);

        Assert.That(
            () => attacker.Attack(target),
            Throws.TypeOf<InvalidOperationException>());
        Assert.That(target.Health, Is.Zero);
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void Constructor_WithNonPositiveAttackPower_ThrowsArgumentOutOfRangeException(
        int attackPower)
    {
        Assert.That(
            () => new Player(nameof(Player), 100, attackPower),
            Throws.TypeOf<ArgumentOutOfRangeException>());
    }
}
