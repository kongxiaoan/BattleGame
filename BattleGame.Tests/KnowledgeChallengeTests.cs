using BattleGame.Core;
using BattleGame.Online;

namespace BattleGame.Tests;

[TestFixture]
public sealed class KnowledgeChallengeTests
{
    [Test]
    public void ResolveSecondRound_ShouldStartQuestionWithoutExposingAnswer()
    {
        var (manager, host, guest) = CreateStartedRoom("QUIZ01");
        ResolveRound(manager, host, guest, 1);

        ActionSubmission second = ResolveRound(manager, host, guest, 2);

        Assert.Multiple(() =>
        {
            Assert.That(second.Snapshot.Phase, Is.EqualTo(OnlineMatchPhase.AnsweringQuestion));
            Assert.That(second.Snapshot.ActiveQuestion, Is.Not.Null);
            Assert.That(second.Snapshot.ActiveQuestion!.QuestionId, Is.EqualTo("math-1"));
            Assert.That(second.Snapshot.ActiveQuestion.Options, Is.EqualTo(new[] { "3", "5", "7", "9" }));
            Assert.That(second.Snapshot.ActiveQuestion.GetType().GetProperty("CorrectOptionIndex"), Is.Null);
        });
    }

    [Test]
    public void SubmitAction_DuringQuestion_ShouldRejectWrongPhase()
    {
        var (manager, host, guest) = CreateStartedRoom("QUIZ02");
        ResolveRound(manager, host, guest, 1);
        ResolveRound(manager, host, guest, 2);

        OnlineGameException error = Assert.Throws<OnlineGameException>(() =>
            manager.SubmitAction(host.RoomCode, host.PlayerId, 3, CombatAction.Attack))!;

        Assert.That(error.Code, Is.EqualTo(OnlineErrorCode.WrongPhase));
    }

    [Test]
    public void CorrectAnswer_ShouldResolveQuestionAndGrantOneEnergy()
    {
        var (manager, host, guest) = CreateStartedRoom("QUIZ03");
        ResolveRound(manager, host, guest, 1);
        ResolveRound(manager, host, guest, 2);

        QuestionSubmission result = manager.SubmitAnswer(
            host.RoomCode,
            host.PlayerId,
            "math-1",
            selectedOptionIndex: 1);

        PlayerSnapshot hostState = result.Snapshot.Players.Single(
            player => player.PlayerId == host.PlayerId);
        Assert.Multiple(() =>
        {
            Assert.That(result.Status, Is.EqualTo(QuestionSubmissionStatus.Resolved));
            Assert.That(result.Resolution!.WinnerPlayerId, Is.EqualTo(host.PlayerId));
            Assert.That(result.Resolution.CorrectOptionIndex, Is.EqualTo(1));
            Assert.That(hostState.Energy, Is.EqualTo(1));
            Assert.That(result.Snapshot.Phase, Is.EqualTo(OnlineMatchPhase.ChoosingActions));
            Assert.That(result.Snapshot.ActiveQuestion, Is.Null);
        });
    }

    [Test]
    public void BothWrongAnswers_ShouldResolveWithoutReward()
    {
        var (manager, host, guest) = CreateStartedRoom("QUIZ04");
        ResolveRound(manager, host, guest, 1);
        ResolveRound(manager, host, guest, 2);

        QuestionSubmission first = manager.SubmitAnswer(
            host.RoomCode,
            host.PlayerId,
            "math-1",
            0);
        QuestionSubmission second = manager.SubmitAnswer(
            guest.RoomCode,
            guest.PlayerId,
            "math-1",
            2);

        Assert.Multiple(() =>
        {
            Assert.That(first.Status, Is.EqualTo(QuestionSubmissionStatus.WaitingForOpponent));
            Assert.That(second.Status, Is.EqualTo(QuestionSubmissionStatus.Resolved));
            Assert.That(second.Resolution!.WinnerPlayerId, Is.Null);
            Assert.That(second.Snapshot.Players.All(player => player.Energy == 0), Is.True);
        });
    }

    [Test]
    public void DuplicateAnswer_ShouldRejectAndKeepFirstAnswer()
    {
        var (manager, host, guest) = CreateStartedRoom("QUIZ05");
        ResolveRound(manager, host, guest, 1);
        ResolveRound(manager, host, guest, 2);
        manager.SubmitAnswer(host.RoomCode, host.PlayerId, "math-1", 0);

        OnlineGameException error = Assert.Throws<OnlineGameException>(() =>
            manager.SubmitAnswer(host.RoomCode, host.PlayerId, "math-1", 1))!;
        QuestionSubmission guestResult = manager.SubmitAnswer(
            guest.RoomCode,
            guest.PlayerId,
            "math-1",
            2);

        Assert.Multiple(() =>
        {
            Assert.That(error.Code, Is.EqualTo(OnlineErrorCode.AnswerAlreadySubmitted));
            Assert.That(guestResult.Resolution!.WinnerPlayerId, Is.Null);
        });
    }

    [Test]
    public void ExpireQuestion_AfterServerDeadline_ShouldResolveWithoutClientTimer()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 7, 15, 10, 0, 0, TimeSpan.Zero));
        var (manager, host, guest) = CreateStartedRoom("QUIZ06", clock);
        ResolveRound(manager, host, guest, 1);
        ActionSubmission round = ResolveRound(manager, host, guest, 2);
        DateTimeOffset deadline = round.Snapshot.ActiveQuestion!.DeadlineUtc;

        clock.SetUtcNow(deadline.AddMilliseconds(1));
        IReadOnlyList<QuestionSubmission> expired = manager.ExpireQuestions();

        Assert.Multiple(() =>
        {
            Assert.That(expired, Has.Count.EqualTo(1));
            Assert.That(expired[0].Resolution!.WinnerPlayerId, Is.Null);
            Assert.That(expired[0].Snapshot.Phase, Is.EqualTo(OnlineMatchPhase.ChoosingActions));
        });
    }

    private static (OnlineRoomManager Manager, RoomAccess Host, RoomAccess Guest)
        CreateStartedRoom(string roomCode, TimeProvider? timeProvider = null)
    {
        var manager = new OnlineRoomManager(
            new StubRoomCodeGenerator(roomCode),
            new StubQuestionProvider(),
            timeProvider);
        RoomAccess host = manager.CreateRoom("甲");
        RoomAccess guest = manager.JoinRoom(roomCode, "乙");
        return (manager, host, guest);
    }

    private static ActionSubmission ResolveRound(
        OnlineRoomManager manager,
        RoomAccess host,
        RoomAccess guest,
        int round)
    {
        manager.SubmitAction(host.RoomCode, host.PlayerId, round, CombatAction.Attack);
        return manager.SubmitAction(guest.RoomCode, guest.PlayerId, round, CombatAction.Attack);
    }

    private sealed class StubRoomCodeGenerator : IRoomCodeGenerator
    {
        private readonly string _roomCode;

        public StubRoomCodeGenerator(string roomCode)
        {
            _roomCode = roomCode;
        }

        public string Create() => _roomCode;
    }

    private sealed class StubQuestionProvider : IQuestionProvider
    {
        public KnowledgeQuestion GetNext(int roundNumber)
        {
            return new KnowledgeQuestion(
                "math-1",
                QuestionCategory.Math,
                "3x + 7 = 22，x 等于多少？",
                new[] { "3", "5", "7", "9" },
                correctOptionIndex: 1,
                "3 × 5 + 7 = 22。");
        }
    }


    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;

        public ManualTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void SetUtcNow(DateTimeOffset value)
        {
            _utcNow = value;
        }
    }
}
