using BattleGame.Core;
using BattleGame.Online;

namespace BattleGame.Tests;

[TestFixture]
public sealed class LastChanceChallengeTests
{
    [Test]
    public void BuiltInProvider_ShouldRotateAcrossThreeBrainChallengeStyles()
    {
        var provider = new BuiltInLastChanceProvider();

        LastChanceDefinition[] challenges = Enumerable.Range(0, 3)
            .Select(_ => provider.Create())
            .ToArray();

        Assert.That(
            challenges.Select(value => value.Prompt).Distinct().ToArray(),
            Has.Length.EqualTo(3));
    }

    [Test]
    public void FatalRound_ShouldStartLastChanceInsteadOfFinishingImmediately()
    {
        var (manager, host, guest) = CreateStartedRoom("LAST01");

        ActionSubmission fatal = ResolveFatalGuestRound(manager, host, guest);

        Assert.Multiple(() =>
        {
            Assert.That(fatal.Snapshot.Status, Is.EqualTo(OnlineRoomStatus.InProgress));
            Assert.That(fatal.Snapshot.Phase, Is.EqualTo(OnlineMatchPhase.LastChance));
            Assert.That(fatal.Snapshot.ActiveLastChance, Is.Not.Null);
            Assert.That(fatal.Snapshot.ActiveLastChance!.TargetPlayerId, Is.EqualTo(guest.PlayerId));
            Assert.That(fatal.Snapshot.ActiveLastChance.RequiredCount, Is.EqualTo(3));
        });
    }

    [Test]
    public void CompleteLastChance_ShouldRestoreFiveHealthAndResumeBattle()
    {
        var (manager, host, guest) = CreateStartedRoom("LAST02");
        ActionSubmission fatal = ResolveFatalGuestRound(manager, host, guest);
        LastChanceSnapshot challenge = fatal.Snapshot.ActiveLastChance!;

        manager.SubmitLastChanceEntry(
            guest.RoomCode,
            guest.PlayerId,
            challenge.ChallengeId,
            "加油");
        manager.SubmitLastChanceEntry(
            guest.RoomCode,
            guest.PlayerId,
            challenge.ChallengeId,
            "加油");
        LastChanceSubmission completed = manager.SubmitLastChanceEntry(
            guest.RoomCode,
            guest.PlayerId,
            challenge.ChallengeId,
            "加油");

        PlayerSnapshot guestState = completed.Snapshot.Players.Single(
            player => player.PlayerId == guest.PlayerId);
        Assert.Multiple(() =>
        {
            Assert.That(completed.Status, Is.EqualTo(LastChanceSubmissionStatus.Succeeded));
            Assert.That(guestState.Health, Is.EqualTo(5));
            Assert.That(completed.Snapshot.Phase, Is.EqualTo(OnlineMatchPhase.ChoosingActions));
            Assert.That(completed.Snapshot.ActiveLastChance, Is.Null);
        });
    }

    [Test]
    public void WrongEntry_ShouldNotIncreaseProgress()
    {
        var (manager, host, guest) = CreateStartedRoom("LAST03");
        LastChanceSnapshot challenge = ResolveFatalGuestRound(manager, host, guest)
            .Snapshot.ActiveLastChance!;

        LastChanceSubmission submission = manager.SubmitLastChanceEntry(
            guest.RoomCode,
            guest.PlayerId,
            challenge.ChallengeId,
            "加油! ");

        Assert.Multiple(() =>
        {
            Assert.That(submission.Status, Is.EqualTo(LastChanceSubmissionStatus.InProgress));
            Assert.That(submission.Snapshot.ActiveLastChance!.CurrentCount, Is.EqualTo(0));
        });
    }

    [Test]
    public void NonTargetPlayer_ShouldNotCompleteOtherPlayersChallenge()
    {
        var (manager, host, guest) = CreateStartedRoom("LAST04");
        LastChanceSnapshot challenge = ResolveFatalGuestRound(manager, host, guest)
            .Snapshot.ActiveLastChance!;

        OnlineGameException error = Assert.Throws<OnlineGameException>(() =>
            manager.SubmitLastChanceEntry(
                host.RoomCode,
                host.PlayerId,
                challenge.ChallengeId,
                "加油"))!;

        Assert.That(error.Code, Is.EqualTo(OnlineErrorCode.NotChallengeTarget));
    }

    [Test]
    public void ExpireLastChance_ShouldFinishOriginalFatalResult()
    {
        var clock = new ManualTimeProvider(new DateTimeOffset(2026, 7, 15, 11, 0, 0, TimeSpan.Zero));
        var (manager, host, guest) = CreateStartedRoom("LAST05", clock);
        LastChanceSnapshot challenge = ResolveFatalGuestRound(manager, host, guest)
            .Snapshot.ActiveLastChance!;
        clock.SetUtcNow(challenge.DeadlineUtc.AddMilliseconds(1));

        IReadOnlyList<LastChanceSubmission> expired = manager.ExpireLastChanceChallenges();

        Assert.Multiple(() =>
        {
            Assert.That(expired, Has.Count.EqualTo(1));
            Assert.That(expired[0].Status, Is.EqualTo(LastChanceSubmissionStatus.Failed));
            Assert.That(expired[0].Snapshot.Status, Is.EqualTo(OnlineRoomStatus.Finished));
            Assert.That(expired[0].Snapshot.Outcome, Is.EqualTo(BattleOutcome.HumanWin));
        });
    }

    private static (OnlineRoomManager Manager, RoomAccess Host, RoomAccess Guest)
        CreateStartedRoom(string roomCode, TimeProvider? timeProvider = null)
    {
        var manager = new OnlineRoomManager(
            new StubRoomCodeGenerator(roomCode),
            questionProvider: null,
            timeProvider: timeProvider,
            lastChanceProvider: new StubLastChanceProvider());
        RoomAccess host = manager.CreateRoom("甲");
        RoomAccess guest = manager.JoinRoom(roomCode, "乙");
        return (manager, host, guest);
    }

    private static ActionSubmission ResolveFatalGuestRound(
        OnlineRoomManager manager,
        RoomAccess host,
        RoomAccess guest)
    {
        ActionSubmission result = null!;
        for (int round = 1; round <= 4; round++)
        {
            manager.SubmitAction(host.RoomCode, host.PlayerId, round, CombatAction.Break);
            result = manager.SubmitAction(
                guest.RoomCode,
                guest.PlayerId,
                round,
                CombatAction.Guard);
        }

        return result;
    }

    private sealed class StubLastChanceProvider : ILastChanceProvider
    {
        public LastChanceDefinition Create()
        {
            return new LastChanceDefinition("30 秒内输入 3 次加油", "加油", 3, TimeSpan.FromSeconds(30), 5);
        }
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

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;

        public ManualTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;
        public void SetUtcNow(DateTimeOffset value) => _utcNow = value;
    }
}
