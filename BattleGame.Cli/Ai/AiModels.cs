using BattleGame.Core;

namespace BattleGame.Cli.Ai;

public enum AppealDecision
{
    Uphold = 1,
    Revoke = 2
}

public sealed class AppealVerdict
{
    public AppealVerdict(AppealDecision decision, string reasonCode, string explanation)
    {
        Decision = decision;
        ReasonCode = reasonCode;
        Explanation = explanation;
    }

    public AppealDecision Decision { get; }
    public string ReasonCode { get; }
    public string Explanation { get; }
}

public sealed class AppealClaim
{
    public AppealClaim(bool shouldAppeal, string argument)
    {
        ShouldAppeal = shouldAppeal;
        Argument = argument;
    }

    public bool ShouldAppeal { get; }
    public string Argument { get; }
}

public sealed class BattleSnapshot
{
    public BattleSnapshot(StrategicBattle battle, string recentHistory)
    {
        Round = battle.RoundNumber;
        HumanHealth = battle.Human.Health;
        HumanEnergy = battle.Human.Energy;
        ComputerHealth = battle.Computer.Health;
        ComputerEnergy = battle.Computer.Energy;
        RecentHistory = recentHistory;
    }

    public int Round { get; }
    public int HumanHealth { get; }
    public int HumanEnergy { get; }
    public int ComputerHealth { get; }
    public int ComputerEnergy { get; }
    public string RecentHistory { get; }

    public override string ToString()
    {
        return $"round={Round}; humanHp={HumanHealth}; humanEnergy={HumanEnergy}; " +
               $"computerHp={ComputerHealth}; computerEnergy={ComputerEnergy}; history={RecentHistory}";
    }
}

public interface IOpponentAgent
{
    bool IsOnline { get; }

    Task<IReadOnlyList<CombatAction>> RankActionsAsync(
        BattleSnapshot snapshot,
        IReadOnlyList<CombatAction> legalActions,
        GameDifficulty difficulty,
        CancellationToken cancellationToken);

    Task<AppealClaim> ConsiderAppealAsync(
        BattleSnapshot snapshot,
        BattleEvent battleEvent,
        CancellationToken cancellationToken);
}

public interface IEventDirector
{
    Task<BattleEvent> CreateEventAsync(
        BattleSnapshot snapshot,
        BattleSide favoredSide,
        CancellationToken cancellationToken);
}

public interface IAppealJudge
{
    Task<AppealVerdict> JudgeAsync(
        BattleSnapshot snapshot,
        BattleEvent battleEvent,
        BattleSide appellant,
        string argument,
        CancellationToken cancellationToken);
}

public interface IRandomSource
{
    int Next(int maximumExclusive);
}

public sealed class SystemRandomSource : IRandomSource
{
    private readonly Random _random;

    public SystemRandomSource(int? seed = null)
    {
        _random = seed.HasValue ? new Random(seed.Value) : new Random();
    }

    public int Next(int maximumExclusive)
    {
        return _random.Next(maximumExclusive);
    }
}
