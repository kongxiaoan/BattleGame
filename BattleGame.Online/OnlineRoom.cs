using BattleGame.Core;

namespace BattleGame.Online;

/// <summary>
/// 一间双人好友房的权威状态机。所有公开方法使用同一把锁，避免两条 WebSocket
/// 同时提交时重复结算一个回合；锁内只做内存计算，不执行网络或 AI 调用。
/// </summary>
internal sealed class OnlineRoom
{
    private readonly object _sync = new();
    private readonly RoomPlayer _host;
    private readonly IQuestionProvider? _questionProvider;
    private readonly TimeProvider _timeProvider;
    private readonly ILastChanceProvider? _lastChanceProvider;
    private RoomPlayer? _guest;
    private StrategicBattle? _battle;
    private CombatAction? _hostAction;
    private CombatAction? _guestAction;
    private KnowledgeQuestion? _activeQuestion;
    private DateTimeOffset? _questionDeadlineUtc;
    private readonly HashSet<Guid> _questionAnswers = new();
    private OnlineMatchPhase _phase = OnlineMatchPhase.WaitingForGuest;
    private readonly HashSet<Guid> _usedLastChancePlayers = new();
    private LastChanceDefinition? _activeLastChance;
    private string? _lastChanceId;
    private Guid? _lastChanceTargetPlayerId;
    private int _lastChanceProgress;
    private DateTimeOffset? _lastChanceDeadlineUtc;
    private BattleEvent? _activeFateEvent;
    private string? _fateEventId;
    private DateTimeOffset? _fateDeadlineUtc;
    private bool _fateJudgmentPending;
    private DateTimeOffset? _actionDeadlineUtc;

    public OnlineRoom(
        string code,
        RoomPlayer host,
        IQuestionProvider? questionProvider,
        TimeProvider timeProvider,
        ILastChanceProvider? lastChanceProvider)
    {
        Code = code;
        _host = host;
        _questionProvider = questionProvider;
        _timeProvider = timeProvider;
        _lastChanceProvider = lastChanceProvider;
    }

    public string Code { get; }

    public void Join(RoomPlayer guest)
    {
        lock (_sync)
        {
            if (_guest != null)
            {
                throw new OnlineGameException(OnlineErrorCode.RoomFull);
            }

            _guest = guest;
            _battle = new StrategicBattle(
                new Combatant(_host.DisplayName),
                new Combatant(guest.DisplayName));
            _phase = OnlineMatchPhase.ChoosingActions;
            ResetActionDeadlineUnsafe();
        }
    }

    public RoomPlayer Authenticate(Guid playerId, string reconnectToken)
    {
        lock (_sync)
        {
            RoomPlayer player = GetPlayer(playerId);
            if (!SecureTokenGenerator.Equals(player.ReconnectToken, reconnectToken))
            {
                throw new OnlineGameException(OnlineErrorCode.InvalidReconnectToken);
            }

            return player;
        }
    }

    public ActionSubmission SubmitAction(
        Guid playerId,
        int expectedRound,
        CombatAction action)
    {
        lock (_sync)
        {
            RoomPlayer player = GetPlayer(playerId);
            StrategicBattle battle = GetActiveBattle();
            if (_phase != OnlineMatchPhase.ChoosingActions)
            {
                throw new OnlineGameException(OnlineErrorCode.WrongPhase);
            }
            int currentRound = battle.RoundNumber + 1;
            if (expectedRound != currentRound)
            {
                throw new OnlineGameException(OnlineErrorCode.RoundMismatch);
            }

            BattleSide side = player.Slot == OnlinePlayerSlot.Host
                ? BattleSide.Human
                : BattleSide.Computer;
            if (!battle.GetLegalActions(side).Contains(action))
            {
                throw new OnlineGameException(OnlineErrorCode.IllegalAction);
            }

            CombatAction? existing = player.Slot == OnlinePlayerSlot.Host
                ? _hostAction
                : _guestAction;
            if (existing.HasValue)
            {
                if (existing.Value != action)
                {
                    // 锁招后禁止改招，否则后提交一方可能利用网络时序获得信息优势。
                    throw new OnlineGameException(OnlineErrorCode.ActionAlreadyLocked);
                }

                return new ActionSubmission(
                    ActionSubmissionStatus.AlreadySubmitted,
                    CreateSnapshotUnsafe(),
                    null);
            }

            if (player.Slot == OnlinePlayerSlot.Host)
            {
                _hostAction = action;
            }
            else
            {
                _guestAction = action;
            }

            if (!_hostAction.HasValue || !_guestAction.HasValue)
            {
                return new ActionSubmission(
                    ActionSubmissionStatus.WaitingForOpponent,
                    CreateSnapshotUnsafe(),
                    null);
            }

            RoundResult result = battle.ResolveRound(_hostAction.Value, _guestAction.Value);
            var resolved = new ResolvedRoundSnapshot(
                battle.RoundNumber,
                result.HumanAction,
                result.ComputerAction,
                result.HumanDamageTaken,
                result.ComputerDamageTaken,
                result.HumanHealing,
                result.ComputerHealing);

            _hostAction = null;
            _guestAction = null;
            if (!StartLastChanceIfRequired(battle))
            {
                StartQuestionIfRequired(battle);
            }
            if (_phase == OnlineMatchPhase.ChoosingActions)
            {
                ResetActionDeadlineUnsafe();
            }
            return new ActionSubmission(
                ActionSubmissionStatus.RoundResolved,
                CreateSnapshotUnsafe(),
                resolved);
        }
    }

    public QuestionSubmission SubmitAnswer(
        Guid playerId,
        string questionId,
        int selectedOptionIndex)
    {
        lock (_sync)
        {
            RoomPlayer player = GetPlayer(playerId);
            StrategicBattle battle = GetActiveBattle();
            if (_phase != OnlineMatchPhase.AnsweringQuestion || _activeQuestion == null)
            {
                throw new OnlineGameException(OnlineErrorCode.WrongPhase);
            }

            if (_questionDeadlineUtc is DateTimeOffset deadline
                && _timeProvider.GetUtcNow() >= deadline)
            {
                return ResolveQuestionUnsafe(null, battle);
            }

            if (!string.Equals(
                    _activeQuestion.QuestionId,
                    questionId,
                    StringComparison.Ordinal))
            {
                throw new OnlineGameException(OnlineErrorCode.QuestionNotFound);
            }

            if (selectedOptionIndex < 0 || selectedOptionIndex >= _activeQuestion.Options.Count)
            {
                throw new OnlineGameException(OnlineErrorCode.InvalidAnswer);
            }

            if (!_questionAnswers.Add(playerId))
            {
                throw new OnlineGameException(OnlineErrorCode.AnswerAlreadySubmitted);
            }

            bool isCorrect = selectedOptionIndex == _activeQuestion.CorrectOptionIndex;
            bool bothAnswered = _questionAnswers.Count == 2;
            if (!isCorrect && !bothAnswered)
            {
                return new QuestionSubmission(
                    QuestionSubmissionStatus.WaitingForOpponent,
                    CreateSnapshotUnsafe(),
                    null);
            }

            return ResolveQuestionUnsafe(isCorrect ? player : null, battle);
        }
    }

    public QuestionSubmission? TryExpireQuestion()
    {
        lock (_sync)
        {
            if (_phase != OnlineMatchPhase.AnsweringQuestion
                || _activeQuestion == null
                || _questionDeadlineUtc is not DateTimeOffset deadline
                || _timeProvider.GetUtcNow() < deadline)
            {
                return null;
            }

            return ResolveQuestionUnsafe(null, GetActiveBattle());
        }
    }

    public LastChanceSubmission SubmitLastChanceEntry(
        Guid playerId,
        string challengeId,
        string entry)
    {
        lock (_sync)
        {
            GetPlayer(playerId);
            if (_phase != OnlineMatchPhase.LastChance
                || _activeLastChance == null
                || !string.Equals(_lastChanceId, challengeId, StringComparison.Ordinal))
            {
                throw new OnlineGameException(OnlineErrorCode.ChallengeNotFound);
            }

            if (_lastChanceTargetPlayerId != playerId)
            {
                throw new OnlineGameException(OnlineErrorCode.NotChallengeTarget);
            }

            if (_lastChanceDeadlineUtc is DateTimeOffset deadline
                && _timeProvider.GetUtcNow() >= deadline)
            {
                return ResolveLastChanceUnsafe(success: false);
            }

            if (string.Equals(
                    entry?.Trim(),
                    _activeLastChance.ExpectedText,
                    StringComparison.Ordinal))
            {
                _lastChanceProgress++;
            }

            if (_lastChanceProgress >= _activeLastChance.RequiredCount)
            {
                return ResolveLastChanceUnsafe(success: true);
            }

            return new LastChanceSubmission(
                LastChanceSubmissionStatus.InProgress,
                CreateSnapshotUnsafe(),
                playerId);
        }
    }

    public LastChanceSubmission? TryExpireLastChance()
    {
        lock (_sync)
        {
            if (_phase != OnlineMatchPhase.LastChance
                || _lastChanceDeadlineUtc is not DateTimeOffset deadline
                || _timeProvider.GetUtcNow() < deadline)
            {
                return null;
            }

            return ResolveLastChanceUnsafe(success: false);
        }
    }

    public void StartFateEvent(FateEventProposal proposal)
    {
        lock (_sync)
        {
            StrategicBattle battle = GetActiveBattle();
            if (_phase != OnlineMatchPhase.ChoosingActions || _guest == null)
            {
                throw new OnlineGameException(OnlineErrorCode.WrongPhase);
            }

            BattleSide target = proposal.TargetSlot == OnlinePlayerSlot.Host
                ? BattleSide.Human
                : BattleSide.Computer;
            _activeFateEvent = proposal.Type switch
            {
                BattleEventType.RestoreHealth => BattleEvent.CreateHealthRestore(
                    target, proposal.Magnitude, proposal.Narrative),
                BattleEventType.LoseHealth => BattleEvent.CreateHealthLoss(
                    target, proposal.Magnitude, proposal.Narrative),
                BattleEventType.GainEnergy when proposal.Magnitude == 1 =>
                    BattleEvent.CreateEnergyGain(target, proposal.Narrative),
                _ => throw new ArgumentOutOfRangeException(nameof(proposal))
            };
            _fateEventId = Guid.NewGuid().ToString("N");
            _fateDeadlineUtc = _timeProvider.GetUtcNow().AddSeconds(30);
            _fateJudgmentPending = false;
            _phase = OnlineMatchPhase.FateEvent;
            _actionDeadlineUtc = null;
        }
    }

    public FateEventSubmission SubmitFateDecision(
        Guid playerId,
        string eventId,
        string argument)
    {
        lock (_sync)
        {
            RoomPlayer player = GetPlayer(playerId);
            BattleEvent battleEvent = RequireFateEvent(eventId);
            RoomPlayer disadvantaged = GetPlayerForSide(battleEvent.DisadvantagedSide);
            if (player.PlayerId != disadvantaged.PlayerId)
            {
                throw new OnlineGameException(OnlineErrorCode.NotDisadvantagedPlayer);
            }

            string normalized = argument?.Trim() ?? string.Empty;
            if (normalized.Length > 200)
            {
                throw new OnlineGameException(OnlineErrorCode.InvalidAppealArgument);
            }

            if (normalized.Length == 0)
            {
                return CompleteFateEventUnsafe(eventApplied: true, wasAppealed: false, string.Empty);
            }

            BattleSide side = player.Slot == OnlinePlayerSlot.Host
                ? BattleSide.Human
                : BattleSide.Computer;
            if (!_battle!.CanAppeal(side))
            {
                throw new OnlineGameException(OnlineErrorCode.AppealAlreadyUsed);
            }
            if (_fateJudgmentPending)
            {
                throw new OnlineGameException(OnlineErrorCode.AppealAlreadySubmitted);
            }

            _battle.UseAppeal(side);
            _fateJudgmentPending = true;
            FateEventSnapshot eventSnapshot = CreateFateEventSnapshotUnsafe()!;
            OnlineMatchSnapshot snapshot = CreateSnapshotUnsafe();
            return new FateEventSubmission(
                FateEventSubmissionStatus.AwaitingJudgment,
                snapshot,
                Appeal: new FateAppealContext(snapshot, eventSnapshot, playerId, normalized));
        }
    }

    public FateEventSubmission ResolveFateAppeal(
        string eventId,
        FateAppealDecision decision,
        string explanation)
    {
        lock (_sync)
        {
            RequireFateEvent(eventId);
            if (!_fateJudgmentPending)
            {
                throw new OnlineGameException(OnlineErrorCode.AppealAlreadySubmitted);
            }

            return CompleteFateEventUnsafe(
                decision == FateAppealDecision.Uphold,
                wasAppealed: true,
                explanation?.Trim() ?? string.Empty);
        }
    }

    public FateEventSubmission? TryExpireFateEvent()
    {
        lock (_sync)
        {
            if (_phase != OnlineMatchPhase.FateEvent
                || _fateJudgmentPending
                || _fateDeadlineUtc is not DateTimeOffset deadline
                || _timeProvider.GetUtcNow() < deadline)
            {
                return null;
            }

            return CompleteFateEventUnsafe(eventApplied: true, wasAppealed: false, string.Empty);
        }
    }

    public ActionSubmission? TryExpireActions()
    {
        lock (_sync)
        {
            if (_phase != OnlineMatchPhase.ChoosingActions
                || _actionDeadlineUtc is not DateTimeOffset deadline
                || _timeProvider.GetUtcNow() < deadline)
            {
                return null;
            }

            StrategicBattle battle = GetActiveBattle();
            int round = battle.RoundNumber + 1;
            ActionSubmission? submission = null;
            // 先拍下缺席方；第二次提交可能完成回合并清空锁招字段，不能据此误判下一回合也缺席。
            bool hostMissing = !_hostAction.HasValue;
            bool guestMissing = !_guestAction.HasValue;
            if (hostMissing)
            {
                submission = SubmitAction(
                    _host.PlayerId,
                    round,
                    battle.GetLegalActions(BattleSide.Human)[0]);
            }
            if (guestMissing)
            {
                submission = SubmitAction(
                    _guest!.PlayerId,
                    round,
                    battle.GetLegalActions(BattleSide.Computer)[0]);
            }

            return submission;
        }
    }

    public OnlineMatchSnapshot CreateSnapshot()
    {
        lock (_sync)
        {
            return CreateSnapshotUnsafe();
        }
    }

    private OnlineMatchSnapshot CreateSnapshotUnsafe()
    {
        if (_battle == null || _guest == null)
        {
            return new OnlineMatchSnapshot(
                Code,
                OnlineRoomStatus.WaitingForGuest,
                0,
                BattleOutcome.Ongoing,
                new[]
                {
                    new PlayerSnapshot(
                        _host.PlayerId,
                        _host.DisplayName,
                        _host.Slot,
                        100,
                        100,
                        0,
                        false,
                        _host.ProfileId)
                },
                OnlineMatchPhase.WaitingForGuest);
        }

        OnlineRoomStatus status = _battle.IsFinished && _phase != OnlineMatchPhase.LastChance
            ? OnlineRoomStatus.Finished
            : OnlineRoomStatus.InProgress;
        int visibleRound = _battle.IsFinished
            ? _battle.RoundNumber
            : _battle.RoundNumber + 1;

        return new OnlineMatchSnapshot(
            Code,
            status,
            visibleRound,
            _battle.Outcome,
            new[]
            {
                CreatePlayerSnapshot(_host, _battle.Human, _hostAction.HasValue),
                CreatePlayerSnapshot(_guest, _battle.Computer, _guestAction.HasValue)
            },
            _battle.IsFinished && _phase != OnlineMatchPhase.LastChance
                ? OnlineMatchPhase.Finished
                : _phase,
            _activeQuestion != null && _questionDeadlineUtc.HasValue
                ? _activeQuestion.CreatePublicSnapshot(_questionDeadlineUtc.Value)
                : null,
            CreateLastChanceSnapshotUnsafe(),
            CreateFateEventSnapshotUnsafe(),
            _phase == OnlineMatchPhase.ChoosingActions ? _actionDeadlineUtc : null);
    }

    private void StartQuestionIfRequired(StrategicBattle battle)
    {
        if (battle.IsFinished || _questionProvider == null
            || battle.RoundNumber is not (2 or 4))
        {
            return;
        }

        _activeQuestion = _questionProvider.GetNext(battle.RoundNumber);
        _questionDeadlineUtc = _timeProvider.GetUtcNow().AddSeconds(15);
        _questionAnswers.Clear();
        _phase = OnlineMatchPhase.AnsweringQuestion;
        _actionDeadlineUtc = null;
    }

    private bool StartLastChanceIfRequired(StrategicBattle battle)
    {
        if (!battle.IsFinished || battle.Outcome == BattleOutcome.Draw
            || _lastChanceProvider == null)
        {
            return false;
        }

        RoomPlayer target = battle.Outcome == BattleOutcome.HumanWin
            ? _guest!
            : _host;
        if (_usedLastChancePlayers.Contains(target.PlayerId))
        {
            return false;
        }

        _usedLastChancePlayers.Add(target.PlayerId);
        _activeLastChance = _lastChanceProvider.Create();
        _lastChanceId = Guid.NewGuid().ToString("N");
        _lastChanceTargetPlayerId = target.PlayerId;
        _lastChanceProgress = 0;
        _lastChanceDeadlineUtc = _timeProvider.GetUtcNow().Add(_activeLastChance.Duration);
        _phase = OnlineMatchPhase.LastChance;
        _actionDeadlineUtc = null;
        return true;
    }

    private LastChanceSubmission ResolveLastChanceUnsafe(bool success)
    {
        LastChanceDefinition definition = _activeLastChance
            ?? throw new InvalidOperationException();
        Guid targetPlayerId = _lastChanceTargetPlayerId!.Value;
        int restoredHealth = 0;
        if (success)
        {
            RoomPlayer target = GetPlayer(targetPlayerId);
            BattleSide side = target.Slot == OnlinePlayerSlot.Host
                ? BattleSide.Human
                : BattleSide.Computer;
            restoredHealth = _battle!.ApplyLastChance(side, definition.RestoredHealth);
            _phase = OnlineMatchPhase.ChoosingActions;
            ResetActionDeadlineUnsafe();
        }
        else
        {
            _phase = OnlineMatchPhase.Finished;
        }

        _activeLastChance = null;
        _lastChanceId = null;
        _lastChanceTargetPlayerId = null;
        _lastChanceDeadlineUtc = null;
        LastChanceSubmissionStatus status = success
            ? LastChanceSubmissionStatus.Succeeded
            : LastChanceSubmissionStatus.Failed;
        return new LastChanceSubmission(
            status,
            CreateSnapshotUnsafe(),
            targetPlayerId,
            restoredHealth);
    }

    private LastChanceSnapshot? CreateLastChanceSnapshotUnsafe()
    {
        if (_activeLastChance == null
            || _lastChanceId == null
            || _lastChanceTargetPlayerId == null
            || _lastChanceDeadlineUtc == null)
        {
            return null;
        }

        return new LastChanceSnapshot(
            _lastChanceId,
            _lastChanceTargetPlayerId.Value,
            _activeLastChance.Prompt,
            _activeLastChance.RequiredCount,
            _lastChanceProgress,
            _lastChanceDeadlineUtc.Value);
    }

    private QuestionSubmission ResolveQuestionUnsafe(
        RoomPlayer? winner,
        StrategicBattle battle)
    {
        KnowledgeQuestion question = _activeQuestion
            ?? throw new InvalidOperationException();
        int energyGranted = 0;
        if (winner != null)
        {
            BattleSide side = winner.Slot == OnlinePlayerSlot.Host
                ? BattleSide.Human
                : BattleSide.Computer;
            energyGranted = battle.GrantKnowledgeEnergy(side);
        }

        var resolution = new QuestionResolution(
            question.QuestionId,
            question.CorrectOptionIndex,
            question.Explanation,
            winner?.PlayerId,
            energyGranted);
        _activeQuestion = null;
        _questionDeadlineUtc = null;
        _questionAnswers.Clear();
        _phase = OnlineMatchPhase.ChoosingActions;
        ResetActionDeadlineUnsafe();
        return new QuestionSubmission(
            QuestionSubmissionStatus.Resolved,
            CreateSnapshotUnsafe(),
            resolution);
    }

    private PlayerSnapshot CreatePlayerSnapshot(
        RoomPlayer player,
        Combatant combatant,
        bool actionLocked)
    {
        return new PlayerSnapshot(
            player.PlayerId,
            player.DisplayName,
            player.Slot,
            combatant.Health,
            combatant.MaxHealth,
            combatant.Energy,
            actionLocked,
            player.ProfileId,
            _battle!.CanAppeal(player.Slot == OnlinePlayerSlot.Host
                ? BattleSide.Human
                : BattleSide.Computer));
    }

    private BattleEvent RequireFateEvent(string eventId)
    {
        if (_phase != OnlineMatchPhase.FateEvent
            || _activeFateEvent == null
            || !string.Equals(_fateEventId, eventId, StringComparison.Ordinal))
        {
            throw new OnlineGameException(OnlineErrorCode.EventNotFound);
        }

        return _activeFateEvent;
    }

    private RoomPlayer GetPlayerForSide(BattleSide side)
    {
        return side == BattleSide.Human
            ? _host
            : _guest ?? throw new OnlineGameException(OnlineErrorCode.PlayerNotFound);
    }

    private FateEventSnapshot? CreateFateEventSnapshotUnsafe()
    {
        if (_activeFateEvent == null || _fateEventId == null || _fateDeadlineUtc == null)
        {
            return null;
        }

        return new FateEventSnapshot(
            _fateEventId,
            _activeFateEvent.Type,
            GetPlayerForSide(_activeFateEvent.Target).PlayerId,
            GetPlayerForSide(_activeFateEvent.DisadvantagedSide).PlayerId,
            _activeFateEvent.Magnitude,
            _activeFateEvent.Narrative,
            _fateDeadlineUtc.Value,
            _fateJudgmentPending);
    }

    private FateEventSubmission CompleteFateEventUnsafe(
        bool eventApplied,
        bool wasAppealed,
        string explanation)
    {
        BattleEvent battleEvent = _activeFateEvent ?? throw new InvalidOperationException();
        string eventId = _fateEventId ?? throw new InvalidOperationException();
        string resolvedExplanation = string.IsNullOrWhiteSpace(explanation)
            ? battleEvent.Narrative
            : explanation;
        if (eventApplied)
        {
            _battle!.ApplyEvent(battleEvent);
        }

        _activeFateEvent = null;
        _fateEventId = null;
        _fateDeadlineUtc = null;
        _fateJudgmentPending = false;
        _phase = OnlineMatchPhase.ChoosingActions;
        ResetActionDeadlineUnsafe();
        return new FateEventSubmission(
            FateEventSubmissionStatus.Resolved,
            CreateSnapshotUnsafe(),
            new FateEventResolution(eventId, eventApplied, wasAppealed, resolvedExplanation));
    }

    private StrategicBattle GetActiveBattle()
    {
        if (_battle == null)
        {
            throw new OnlineGameException(OnlineErrorCode.MatchNotStarted);
        }

        if (_battle.IsFinished)
        {
            throw new OnlineGameException(OnlineErrorCode.MatchFinished);
        }

        return _battle;
    }

    private void ResetActionDeadlineUnsafe()
    {
        _actionDeadlineUtc = _timeProvider.GetUtcNow().AddSeconds(15);
    }

    private RoomPlayer GetPlayer(Guid playerId)
    {
        if (_host.PlayerId == playerId)
        {
            return _host;
        }

        if (_guest?.PlayerId == playerId)
        {
            return _guest;
        }

        throw new OnlineGameException(OnlineErrorCode.PlayerNotFound);
    }
}

internal sealed record RoomPlayer(
    Guid PlayerId,
    string DisplayName,
    OnlinePlayerSlot Slot,
    string ReconnectToken,
    Guid? ProfileId);
