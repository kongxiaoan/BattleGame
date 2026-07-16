using BattleGame.Core;

namespace BattleGame.Online;

public enum OnlineRoomStatus
{
    WaitingForGuest = 1,
    InProgress = 2,
    Finished = 3
}

public enum OnlinePlayerSlot
{
    Host = 1,
    Guest = 2
}

public enum ActionSubmissionStatus
{
    WaitingForOpponent = 1,
    AlreadySubmitted = 2,
    RoundResolved = 3
}

public enum OnlineMatchPhase
{
    WaitingForGuest = 1,
    ChoosingActions = 2,
    AnsweringQuestion = 3,
    LastChance = 4,
    FateEvent = 5,
    Finished = 6
}

public enum QuestionSubmissionStatus
{
    WaitingForOpponent = 1,
    Resolved = 2
}

public enum LastChanceSubmissionStatus
{
    InProgress = 1,
    Succeeded = 2,
    Failed = 3
}

public enum OnlineErrorCode
{
    RoomNotFound = 1,
    RoomFull = 2,
    PlayerNotFound = 3,
    InvalidReconnectToken = 4,
    RoundMismatch = 5,
    IllegalAction = 6,
    ActionAlreadyLocked = 7,
    MatchNotStarted = 8,
    MatchFinished = 9,
    InvalidPlayerName = 10,
    RoomCodeUnavailable = 11,
    WrongPhase = 12,
    QuestionNotFound = 13,
    InvalidAnswer = 14,
    AnswerAlreadySubmitted = 15,
    ChallengeNotFound = 16,
    NotChallengeTarget = 17,
    EventNotFound = 18,
    NotDisadvantagedPlayer = 19,
    AppealAlreadyUsed = 20,
    AppealAlreadySubmitted = 21,
    InvalidAppealArgument = 22
}

/// <summary>
/// 联网层只向客户端暴露稳定错误码，展示文案由各客户端的本地化词条决定。
/// </summary>
public sealed class OnlineGameException : Exception
{
    public OnlineGameException(OnlineErrorCode code)
        : base(code.ToString())
    {
        Code = code;
    }

    public OnlineErrorCode Code { get; }
}

public sealed record PlayerSnapshot(
    Guid PlayerId,
    string DisplayName,
    OnlinePlayerSlot Slot,
    int Health,
    int MaxHealth,
    int Energy,
    bool ActionLocked,
    Guid? ProfileId = null,
    bool AppealAvailable = true);

public sealed record OnlineMatchSnapshot(
    string RoomCode,
    OnlineRoomStatus Status,
    int RoundNumber,
    BattleOutcome Outcome,
    IReadOnlyList<PlayerSnapshot> Players,
    OnlineMatchPhase Phase = OnlineMatchPhase.ChoosingActions,
    QuestionSnapshot? ActiveQuestion = null,
    LastChanceSnapshot? ActiveLastChance = null,
    FateEventSnapshot? ActiveFateEvent = null,
    DateTimeOffset? ActionDeadlineUtc = null);

public sealed record ResolvedRoundSnapshot(
    int RoundNumber,
    CombatAction HostAction,
    CombatAction GuestAction,
    int HostDamageTaken,
    int GuestDamageTaken,
    int HostHealing,
    int GuestHealing);

public sealed record ActionSubmission(
    ActionSubmissionStatus Status,
    OnlineMatchSnapshot Snapshot,
    ResolvedRoundSnapshot? ResolvedRound);

public sealed record QuestionSubmission(
    QuestionSubmissionStatus Status,
    OnlineMatchSnapshot Snapshot,
    QuestionResolution? Resolution);

public sealed record QuestionResolution(
    string QuestionId,
    int CorrectOptionIndex,
    string Explanation,
    Guid? WinnerPlayerId,
    int EnergyGranted);

public sealed record LastChanceSnapshot(
    string ChallengeId,
    Guid TargetPlayerId,
    string Prompt,
    int RequiredCount,
    int CurrentCount,
    DateTimeOffset DeadlineUtc);

public sealed record LastChanceSubmission(
    LastChanceSubmissionStatus Status,
    OnlineMatchSnapshot Snapshot,
    Guid? TargetPlayerId = null,
    int RestoredHealth = 0);

public enum FateEventSubmissionStatus
{
    AwaitingJudgment = 1,
    Resolved = 2
}

public enum FateAppealDecision
{
    Uphold = 1,
    Revoke = 2
}

public sealed record FateEventProposal(
    BattleEventType Type,
    OnlinePlayerSlot TargetSlot,
    int Magnitude,
    string Narrative);

public sealed record FateEventSnapshot(
    string EventId,
    BattleEventType Type,
    Guid TargetPlayerId,
    Guid DisadvantagedPlayerId,
    int Magnitude,
    string Narrative,
    DateTimeOffset DeadlineUtc,
    bool JudgmentPending);

public sealed record FateAppealContext(
    OnlineMatchSnapshot BattleSnapshot,
    FateEventSnapshot Event,
    Guid AppellantPlayerId,
    string Argument);

public sealed record FateEventResolution(
    string EventId,
    bool EventApplied,
    bool WasAppealed,
    string Explanation);

public sealed record FateEventSubmission(
    FateEventSubmissionStatus Status,
    OnlineMatchSnapshot Snapshot,
    FateEventResolution? Resolution = null,
    FateAppealContext? Appeal = null);

/// <summary>
/// 房间访问凭据只在创建、加入或成功重连时返回，普通状态广播不包含重连令牌。
/// </summary>
public sealed record RoomAccess(
    string RoomCode,
    Guid PlayerId,
    string ReconnectToken,
    OnlineMatchSnapshot Snapshot,
    Guid? ProfileId = null);

public interface IRoomCodeGenerator
{
    string Create();
}
