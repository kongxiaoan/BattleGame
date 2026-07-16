using System.Collections.Concurrent;
using BattleGame.Core;

namespace BattleGame.Online;

/// <summary>
/// 单实例好友房目录。后续切换 Redis 时可保留本类型的公共契约，替换存储实现。
/// </summary>
public sealed class OnlineRoomManager
{
    private const int MaximumRoomCodeAttempts = 20;
    private readonly ConcurrentDictionary<string, OnlineRoom> _rooms =
        new(StringComparer.OrdinalIgnoreCase);
    private readonly IRoomCodeGenerator _roomCodeGenerator;
    private readonly IQuestionProvider? _questionProvider;
    private readonly TimeProvider _timeProvider;
    private readonly ILastChanceProvider? _lastChanceProvider;

    public OnlineRoomManager(
        IRoomCodeGenerator roomCodeGenerator,
        IQuestionProvider? questionProvider = null,
        TimeProvider? timeProvider = null,
        ILastChanceProvider? lastChanceProvider = null)
    {
        _roomCodeGenerator = roomCodeGenerator
            ?? throw new ArgumentNullException(nameof(roomCodeGenerator));
        _questionProvider = questionProvider;
        _timeProvider = timeProvider ?? TimeProvider.System;
        _lastChanceProvider = lastChanceProvider;
    }

    public RoomAccess CreateRoom(string displayName, Guid? profileId = null)
    {
        string normalizedName = ValidateDisplayName(displayName);
        var host = new RoomPlayer(
            Guid.NewGuid(),
            normalizedName,
            OnlinePlayerSlot.Host,
            SecureTokenGenerator.Create(),
            profileId);

        for (int attempt = 0; attempt < MaximumRoomCodeAttempts; attempt++)
        {
            string code = _roomCodeGenerator.Create().ToUpperInvariant();
            var room = new OnlineRoom(
                code,
                host,
                _questionProvider,
                _timeProvider,
                _lastChanceProvider);
            if (_rooms.TryAdd(code, room))
            {
                return CreateAccess(room, host);
            }
        }

        throw new OnlineGameException(OnlineErrorCode.RoomCodeUnavailable);
    }

    public RoomAccess JoinRoom(
        string roomCode,
        string displayName,
        Guid? profileId = null)
    {
        OnlineRoom room = GetRoom(roomCode);
        var guest = new RoomPlayer(
            Guid.NewGuid(),
            ValidateDisplayName(displayName),
            OnlinePlayerSlot.Guest,
            SecureTokenGenerator.Create(),
            profileId);
        room.Join(guest);
        return CreateAccess(room, guest);
    }

    public RoomAccess Reconnect(string roomCode, Guid playerId, string reconnectToken)
    {
        OnlineRoom room = GetRoom(roomCode);
        RoomPlayer player = room.Authenticate(playerId, reconnectToken);
        return CreateAccess(room, player);
    }

    public ActionSubmission SubmitAction(
        string roomCode,
        Guid playerId,
        int expectedRound,
        CombatAction action)
    {
        return GetRoom(roomCode).SubmitAction(playerId, expectedRound, action);
    }

    public OnlineMatchSnapshot GetSnapshot(string roomCode)
    {
        return GetRoom(roomCode).CreateSnapshot();
    }

    public QuestionSubmission SubmitAnswer(
        string roomCode,
        Guid playerId,
        string questionId,
        int selectedOptionIndex)
    {
        return GetRoom(roomCode).SubmitAnswer(playerId, questionId, selectedOptionIndex);
    }

    public IReadOnlyList<QuestionSubmission> ExpireQuestions()
    {
        var expired = new List<QuestionSubmission>();
        foreach (OnlineRoom room in _rooms.Values)
        {
            QuestionSubmission? submission = room.TryExpireQuestion();
            if (submission != null)
            {
                expired.Add(submission);
            }
        }

        return expired;
    }

    public LastChanceSubmission SubmitLastChanceEntry(
        string roomCode,
        Guid playerId,
        string challengeId,
        string entry)
    {
        return GetRoom(roomCode).SubmitLastChanceEntry(playerId, challengeId, entry);
    }

    public IReadOnlyList<LastChanceSubmission> ExpireLastChanceChallenges()
    {
        var expired = new List<LastChanceSubmission>();
        foreach (OnlineRoom room in _rooms.Values)
        {
            LastChanceSubmission? submission = room.TryExpireLastChance();
            if (submission != null)
            {
                expired.Add(submission);
            }
        }

        return expired;
    }

    public void StartFateEvent(string roomCode, FateEventProposal proposal)
    {
        GetRoom(roomCode).StartFateEvent(proposal);
    }

    public FateEventSubmission SubmitFateDecision(
        string roomCode,
        Guid playerId,
        string eventId,
        string argument)
    {
        return GetRoom(roomCode).SubmitFateDecision(playerId, eventId, argument);
    }

    public FateEventSubmission ResolveFateAppeal(
        string roomCode,
        string eventId,
        FateAppealDecision decision,
        string explanation)
    {
        return GetRoom(roomCode).ResolveFateAppeal(eventId, decision, explanation);
    }

    public IReadOnlyList<FateEventSubmission> ExpireFateEvents()
    {
        var expired = new List<FateEventSubmission>();
        foreach (OnlineRoom room in _rooms.Values)
        {
            FateEventSubmission? submission = room.TryExpireFateEvent();
            if (submission != null)
            {
                expired.Add(submission);
            }
        }

        return expired;
    }

    public IReadOnlyList<ActionSubmission> ExpireActions()
    {
        var expired = new List<ActionSubmission>();
        foreach (OnlineRoom room in _rooms.Values)
        {
            ActionSubmission? submission = room.TryExpireActions();
            if (submission != null)
            {
                expired.Add(submission);
            }
        }

        return expired;
    }

    private OnlineRoom GetRoom(string roomCode)
    {
        string normalized = string.IsNullOrWhiteSpace(roomCode)
            ? string.Empty
            : roomCode.Trim();
        if (!_rooms.TryGetValue(normalized, out OnlineRoom? room))
        {
            throw new OnlineGameException(OnlineErrorCode.RoomNotFound);
        }

        return room;
    }

    private static RoomAccess CreateAccess(OnlineRoom room, RoomPlayer player)
    {
        return new RoomAccess(
            room.Code,
            player.PlayerId,
            player.ReconnectToken,
            room.CreateSnapshot(),
            player.ProfileId);
    }

    private static string ValidateDisplayName(string displayName)
    {
        string normalized = displayName?.Trim() ?? string.Empty;
        if (normalized.Length is < 1 or > 20)
        {
            throw new OnlineGameException(OnlineErrorCode.InvalidPlayerName);
        }

        return normalized;
    }
}
