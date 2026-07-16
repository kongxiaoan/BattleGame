using BattleGame.Core;

namespace BattleGame.Cli.Ai;

/// <summary>
/// 网络或模型不可用时的确定性后备，保证单机游戏不会因 API 故障卡死。
/// </summary>
public sealed class LocalGameIntelligence : IOpponentAgent, IEventDirector, IAppealJudge
{
    private readonly IRandomSource _random;

    public LocalGameIntelligence(IRandomSource random)
    {
        _random = random;
    }

    public bool IsOnline => false;

    public Task<IReadOnlyList<CombatAction>> RankActionsAsync(
        BattleSnapshot snapshot,
        IReadOnlyList<CombatAction> legalActions,
        GameDifficulty difficulty,
        CancellationToken cancellationToken)
    {
        var ranked = new List<CombatAction>();
        if (snapshot.ComputerHealth <= 40 && legalActions.Contains(CombatAction.Heal))
        {
            ranked.Add(CombatAction.Heal);
        }

        if (snapshot.RecentHistory.Contains("Guard", StringComparison.OrdinalIgnoreCase))
        {
            ranked.Add(CombatAction.Break);
        }

        ranked.Add(CombatAction.Attack);
        ranked.Add(CombatAction.Guard);
        ranked.Add(CombatAction.Break);
        foreach (CombatAction action in legalActions)
        {
            if (!ranked.Contains(action))
            {
                ranked.Add(action);
            }
        }

        return Task.FromResult<IReadOnlyList<CombatAction>>(
            ranked.Where(legalActions.Contains).Distinct().ToArray());
    }

    public Task<AppealClaim> ConsiderAppealAsync(
        BattleSnapshot snapshot,
        BattleEvent battleEvent,
        CancellationToken cancellationToken)
    {
        bool worthAppealing = battleEvent.DisadvantagedSide == BattleSide.Computer &&
                              (battleEvent.Magnitude >= 12 || snapshot.ComputerHealth <= 35);
        string argument = worthAppealing
            ? GameText.LocalAppealArgument
            : string.Empty;
        return Task.FromResult(new AppealClaim(worthAppealing, argument));
    }

    public Task<BattleEvent> CreateEventAsync(
        BattleSnapshot snapshot,
        BattleSide favoredSide,
        CancellationToken cancellationToken)
    {
        int eventType = _random.Next(3);
        BattleSide opposingSide = favoredSide == BattleSide.Human
            ? BattleSide.Computer
            : BattleSide.Human;
        BattleEvent result = eventType switch
        {
            0 => BattleEvent.CreateHealthRestore(favoredSide, 15, GameText.LocalEventRestore),
            1 => BattleEvent.CreateHealthLoss(opposingSide, 12, GameText.LocalEventLoss),
            _ => BattleEvent.CreateEnergyGain(favoredSide, GameText.LocalEventEnergy)
        };
        return Task.FromResult(result);
    }

    public Task<AppealVerdict> JudgeAsync(
        BattleSnapshot snapshot,
        BattleEvent battleEvent,
        BattleSide appellant,
        string argument,
        CancellationToken cancellationToken)
    {
        string[] evidenceWords = { "规则", "记录", "连续", "重复", "公平", "致命" };
        bool hasEvidence = argument.Length >= 8 && evidenceWords.Any(argument.Contains);
        return Task.FromResult(hasEvidence
            ? new AppealVerdict(AppealDecision.Revoke, "GROUNDED_ARGUMENT", GameText.LocalJudgeRevoke)
            : new AppealVerdict(AppealDecision.Uphold, "INSUFFICIENT_EVIDENCE", GameText.LocalJudgeUphold));
    }
}
