using BattleGame.Core;

namespace BattleGame.Online;

public sealed record FateAppealVerdict(
    FateAppealDecision Decision,
    string Explanation);

public interface IOnlineFateAgent
{
    Task<FateEventProposal> CreateEventAsync(
        OnlineMatchSnapshot snapshot,
        int eventNumber,
        CancellationToken cancellationToken);

    Task<FateAppealVerdict> JudgeAsync(
        FateAppealContext appeal,
        CancellationToken cancellationToken);
}

/// <summary>
/// 没有 DeepSeek 或模型失败时的确定性裁判。候选事件全部经过 BattleEvent 工厂二次验证。
/// </summary>
public sealed class BuiltInOnlineFateAgent : IOnlineFateAgent
{
    public Task<FateEventProposal> CreateEventAsync(
        OnlineMatchSnapshot snapshot,
        int eventNumber,
        CancellationToken cancellationToken)
    {
        FateEventProposal proposal = (eventNumber % 4) switch
        {
            1 => new(BattleEventType.LoseHealth, OnlinePlayerSlot.Guest, 10, FateEventText.LossGuest),
            2 => new(BattleEventType.RestoreHealth, OnlinePlayerSlot.Host, 12, FateEventText.RestoreHost),
            3 => new(BattleEventType.GainEnergy, OnlinePlayerSlot.Guest, 1, FateEventText.EnergyGuest),
            _ => new(BattleEventType.LoseHealth, OnlinePlayerSlot.Host, 10, FateEventText.LossHost)
        };
        return Task.FromResult(proposal);
    }

    public Task<FateAppealVerdict> JudgeAsync(
        FateAppealContext appeal,
        CancellationToken cancellationToken)
    {
        // 本地回退只认可可验证的局势词，避免“求情越长越容易成功”的错误激励。
        bool grounded = new[] { "领先", "连续", "生命", "能量", "失衡", "规则" }
            .Any(term => appeal.Argument.Contains(term, StringComparison.Ordinal));
        return Task.FromResult(grounded
            ? new FateAppealVerdict(FateAppealDecision.Revoke, FateEventText.JudgeRevoke)
            : new FateAppealVerdict(FateAppealDecision.Uphold, FateEventText.JudgeUphold));
    }
}
