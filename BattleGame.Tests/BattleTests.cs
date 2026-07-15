using BattleGame.Core;

namespace BattleGame.Tests;

public sealed class BattleTests
{
    [Test]
    public void Constructor_WithValidPlayers_InitializesFirstPlayerAsAttacker()
    {
        var firstPlayer = CreatePlayer(attackPower: 20);
        var secondPlayer = CreatePlayer(attackPower: 10);

        var battle = new Battle(firstPlayer, secondPlayer);

        Assert.Multiple(() =>
        {
            // 战斗状态必须保留传入对象的身份，不能偷偷复制出另一份玩家状态。
            Assert.That(battle.CurrentAttacker, Is.SameAs(firstPlayer));
            Assert.That(battle.CurrentDefender, Is.SameAs(secondPlayer));
            Assert.That(battle.IsFinished, Is.False);
            Assert.That(battle.Winner, Is.Null);
        });
    }

    [Test]
    public void ExecuteTurn_WhenDefenderSurvives_SwapsAttackerAndDefender()
    {
        var firstPlayer = CreatePlayer(attackPower: 20);
        var secondPlayer = CreatePlayer(attackPower: 10);
        var battle = new Battle(firstPlayer, secondPlayer);

        battle.ExecuteTurn();

        Assert.Multiple(() =>
        {
            Assert.That(secondPlayer.Health, Is.EqualTo(80));
            Assert.That(battle.CurrentAttacker, Is.SameAs(secondPlayer));
            Assert.That(battle.CurrentDefender, Is.SameAs(firstPlayer));
        });
    }

    [Test]
    public void ExecuteTurn_WithHealAction_HealsCurrentAttackerAndSwapsPlayers()
    {
        var firstPlayer = CreatePlayer();
        var secondPlayer = CreatePlayer();
        firstPlayer.TakeDamage(40);
        var battle = new Battle(firstPlayer, secondPlayer);

        battle.ExecuteTurn(BattleAction.Heal, healingAmount: 15);

        Assert.Multiple(() =>
        {
            Assert.That(firstPlayer.Health, Is.EqualTo(75));
            Assert.That(secondPlayer.Health, Is.EqualTo(secondPlayer.MaxHealth));
            Assert.That(battle.CurrentAttacker, Is.SameAs(secondPlayer));
            Assert.That(battle.CurrentDefender, Is.SameAs(firstPlayer));
        });
    }

    [Test]
    public void ExecuteTurn_WithUnsupportedAction_ThrowsWithoutChangingBattleState()
    {
        var firstPlayer = CreatePlayer();
        var secondPlayer = CreatePlayer();
        var battle = new Battle(firstPlayer, secondPlayer);

        Assert.That(
            () => battle.ExecuteTurn((BattleAction)999, healingAmount: 15),
            Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.Multiple(() =>
        {
            Assert.That(firstPlayer.Health, Is.EqualTo(firstPlayer.MaxHealth));
            Assert.That(secondPlayer.Health, Is.EqualTo(secondPlayer.MaxHealth));
            Assert.That(battle.CurrentAttacker, Is.SameAs(firstPlayer));
        });
    }

    [TestCase(0)]
    [TestCase(-1)]
    public void ExecuteTurn_WithHealActionAndInvalidAmount_ThrowsWithoutAdvancingTurn(
        int healingAmount)
    {
        var firstPlayer = CreatePlayer();
        var secondPlayer = CreatePlayer();
        var battle = new Battle(firstPlayer, secondPlayer);

        Assert.That(
            () => battle.ExecuteTurn(BattleAction.Heal, healingAmount),
            Throws.TypeOf<ArgumentOutOfRangeException>());
        Assert.That(battle.CurrentAttacker, Is.SameAs(firstPlayer));
    }

    [Test]
    public void ExecuteTurn_WhenAttackIsLethal_FinishesBattleAndRecordsWinner()
    {
        var firstPlayer = CreatePlayer(attackPower: 100);
        var secondPlayer = CreatePlayer(attackPower: 10);
        var battle = new Battle(firstPlayer, secondPlayer);

        battle.ExecuteTurn();

        Assert.Multiple(() =>
        {
            Assert.That(battle.IsFinished, Is.True);
            Assert.That(battle.Winner, Is.SameAs(firstPlayer));
            Assert.That(secondPlayer.IsAlive, Is.False);
        });
    }

    [Test]
    public void ExecuteTurn_WhenBattleIsFinished_ThrowsInvalidOperationException()
    {
        var firstPlayer = CreatePlayer(attackPower: 100);
        var secondPlayer = CreatePlayer(attackPower: 10);
        var battle = new Battle(firstPlayer, secondPlayer);
        battle.ExecuteTurn();

        Assert.That(
            () => battle.ExecuteTurn(),
            Throws.TypeOf<InvalidOperationException>());
    }

    [Test]
    public void Constructor_WithSamePlayerOnBothSides_ThrowsArgumentException()
    {
        var player = CreatePlayer();

        Assert.That(
            () => new Battle(player, player),
            Throws.TypeOf<ArgumentException>());
    }

    [Test]
    public void Constructor_WithNullFirstPlayer_ThrowsArgumentNullException()
    {
        var secondPlayer = CreatePlayer();

        Assert.That(
            () => new Battle(null!, secondPlayer),
            Throws.TypeOf<ArgumentNullException>());
    }

    [Test]
    public void Constructor_WithNullSecondPlayer_ThrowsArgumentNullException()
    {
        var firstPlayer = CreatePlayer();

        Assert.That(
            () => new Battle(firstPlayer, null!),
            Throws.TypeOf<ArgumentNullException>());
    }

    [TestCase(true)]
    [TestCase(false)]
    public void Constructor_WithDeadParticipant_ThrowsArgumentException(
        bool firstPlayerIsDead)
    {
        var firstPlayer = CreatePlayer();
        var secondPlayer = CreatePlayer();
        var deadPlayer = firstPlayerIsDead ? firstPlayer : secondPlayer;
        deadPlayer.TakeDamage(deadPlayer.MaxHealth);

        Assert.That(
            () => new Battle(firstPlayer, secondPlayer),
            Throws.TypeOf<ArgumentException>());
    }

    private static Player CreatePlayer(int maxHealth = 100, int attackPower = 20)
    {
        return new Player(nameof(Player), maxHealth, attackPower);
    }
}
