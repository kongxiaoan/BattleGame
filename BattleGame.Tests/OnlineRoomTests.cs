using BattleGame.Core;
using BattleGame.Online;

namespace BattleGame.Tests;

[TestFixture]
public sealed class OnlineRoomTests
{
    [Test]
    public void CreateRoom_ShouldReturnWaitingRoomAndHostCredentials()
    {
        var manager = CreateManager("ABCD12");

        RoomAccess access = manager.CreateRoom("小王");

        Assert.Multiple(() =>
        {
            Assert.That(access.RoomCode, Is.EqualTo("ABCD12"));
            Assert.That(access.PlayerId, Is.Not.EqualTo(Guid.Empty));
            Assert.That(access.ReconnectToken, Is.Not.Empty);
            Assert.That(access.Snapshot.Status, Is.EqualTo(OnlineRoomStatus.WaitingForGuest));
            Assert.That(access.Snapshot.Players, Has.Count.EqualTo(1));
            Assert.That(access.Snapshot.Players[0].DisplayName, Is.EqualTo("小王"));
        });
    }

    [Test]
    public void JoinRoom_ShouldStartMatchWithSymmetricInitialState()
    {
        var manager = CreateManager("ROOM01");
        RoomAccess host = manager.CreateRoom("玩家甲");

        RoomAccess guest = manager.JoinRoom(host.RoomCode, "玩家乙");

        Assert.Multiple(() =>
        {
            Assert.That(guest.Snapshot.Status, Is.EqualTo(OnlineRoomStatus.InProgress));
            Assert.That(guest.Snapshot.RoundNumber, Is.EqualTo(1));
            Assert.That(guest.Snapshot.Players, Has.Count.EqualTo(2));
            Assert.That(guest.Snapshot.Players.All(player => player.Health == 100), Is.True);
            Assert.That(guest.Snapshot.Players.All(player => player.Energy == 0), Is.True);
        });
    }

    [Test]
    public void JoinRoom_WhenRoomIsFull_ShouldRejectThirdPlayer()
    {
        var manager = CreateManager("ROOM02");
        RoomAccess host = manager.CreateRoom("玩家甲");
        manager.JoinRoom(host.RoomCode, "玩家乙");

        OnlineGameException error = Assert.Throws<OnlineGameException>(
            () => manager.JoinRoom(host.RoomCode, "玩家丙"))!;

        Assert.That(error.Code, Is.EqualTo(OnlineErrorCode.RoomFull));
    }

    [Test]
    public void SubmitFirstAction_ShouldOnlyExposeLockedStateAndKeepActionSecret()
    {
        var (manager, host, guest) = CreateStartedRoom("ROOM03");

        ActionSubmission submission = manager.SubmitAction(
            host.RoomCode,
            host.PlayerId,
            expectedRound: 1,
            CombatAction.Break);

        PlayerSnapshot hostState = submission.Snapshot.Players.Single(
            player => player.PlayerId == host.PlayerId);
        Assert.Multiple(() =>
        {
            Assert.That(submission.Status, Is.EqualTo(ActionSubmissionStatus.WaitingForOpponent));
            Assert.That(submission.ResolvedRound, Is.Null);
            Assert.That(hostState.ActionLocked, Is.True);
            Assert.That(submission.Snapshot.Players.Single(
                player => player.PlayerId == guest.PlayerId).ActionLocked, Is.False);
        });
    }

    [Test]
    public void SubmitBothActions_ShouldResolveExactlyOneRound()
    {
        var (manager, host, guest) = CreateStartedRoom("ROOM04");
        manager.SubmitAction(host.RoomCode, host.PlayerId, 1, CombatAction.Attack);

        ActionSubmission submission = manager.SubmitAction(
            guest.RoomCode,
            guest.PlayerId,
            1,
            CombatAction.Guard);

        PlayerSnapshot guestState = submission.Snapshot.Players.Single(
            player => player.PlayerId == guest.PlayerId);
        Assert.Multiple(() =>
        {
            Assert.That(submission.Status, Is.EqualTo(ActionSubmissionStatus.RoundResolved));
            Assert.That(submission.ResolvedRound, Is.Not.Null);
            Assert.That(submission.ResolvedRound!.HostAction, Is.EqualTo(CombatAction.Attack));
            Assert.That(submission.ResolvedRound.GuestAction, Is.EqualTo(CombatAction.Guard));
            Assert.That(guestState.Health, Is.EqualTo(95));
            Assert.That(guestState.Energy, Is.EqualTo(1));
            Assert.That(submission.Snapshot.RoundNumber, Is.EqualTo(2));
            Assert.That(submission.Snapshot.Players.All(player => !player.ActionLocked), Is.True);
        });
    }

    [Test]
    public void SubmitAction_WhenRepeated_ShouldBeIdempotentOnlyForSameAction()
    {
        var (manager, host, _) = CreateStartedRoom("ROOM05");
        manager.SubmitAction(host.RoomCode, host.PlayerId, 1, CombatAction.Guard);

        ActionSubmission duplicate = manager.SubmitAction(
            host.RoomCode,
            host.PlayerId,
            1,
            CombatAction.Guard);
        OnlineGameException conflict = Assert.Throws<OnlineGameException>(() =>
            manager.SubmitAction(host.RoomCode, host.PlayerId, 1, CombatAction.Attack))!;

        Assert.Multiple(() =>
        {
            Assert.That(duplicate.Status, Is.EqualTo(ActionSubmissionStatus.AlreadySubmitted));
            Assert.That(conflict.Code, Is.EqualTo(OnlineErrorCode.ActionAlreadyLocked));
        });
    }

    [Test]
    public void SubmitAction_WithStaleRound_ShouldRejectWithoutChangingState()
    {
        var (manager, host, _) = CreateStartedRoom("ROOM06");

        OnlineGameException error = Assert.Throws<OnlineGameException>(() =>
            manager.SubmitAction(host.RoomCode, host.PlayerId, 0, CombatAction.Attack))!;

        Assert.Multiple(() =>
        {
            Assert.That(error.Code, Is.EqualTo(OnlineErrorCode.RoundMismatch));
            Assert.That(manager.GetSnapshot(host.RoomCode).Players.All(player => !player.ActionLocked), Is.True);
        });
    }

    [Test]
    public void SubmitHealWithoutEnergy_ShouldRejectAtAuthoritativeServerBoundary()
    {
        var (manager, host, _) = CreateStartedRoom("ROOM07");

        OnlineGameException error = Assert.Throws<OnlineGameException>(() =>
            manager.SubmitAction(host.RoomCode, host.PlayerId, 1, CombatAction.Heal))!;

        Assert.That(error.Code, Is.EqualTo(OnlineErrorCode.IllegalAction));
    }

    [Test]
    public void Reconnect_WithValidToken_ShouldReturnCurrentAuthoritativeState()
    {
        var (manager, host, _) = CreateStartedRoom("ROOM08");
        manager.SubmitAction(host.RoomCode, host.PlayerId, 1, CombatAction.Break);

        RoomAccess restored = manager.Reconnect(
            host.RoomCode,
            host.PlayerId,
            host.ReconnectToken);

        Assert.Multiple(() =>
        {
            Assert.That(restored.PlayerId, Is.EqualTo(host.PlayerId));
            Assert.That(restored.Snapshot.RoundNumber, Is.EqualTo(1));
            Assert.That(restored.Snapshot.Players.Single(
                player => player.PlayerId == host.PlayerId).ActionLocked, Is.True);
        });
    }

    [Test]
    public void Reconnect_WithInvalidToken_ShouldReject()
    {
        var (manager, host, _) = CreateStartedRoom("ROOM09");

        OnlineGameException error = Assert.Throws<OnlineGameException>(() =>
            manager.Reconnect(host.RoomCode, host.PlayerId, "wrong-token"))!;

        Assert.That(error.Code, Is.EqualTo(OnlineErrorCode.InvalidReconnectToken));
    }

    [Test]
    public void FateEvent_WhenAccepted_ShouldApplyOnlyServerValidatedEffect()
    {
        var (manager, host, guest) = CreateStartedRoom("FATE01");
        manager.StartFateEvent(
            host.RoomCode,
            new FateEventProposal(
                BattleEventType.LoseHealth,
                OnlinePlayerSlot.Guest,
                12,
                "命运天平向房主倾斜。"));

        FateEventSubmission submission = manager.SubmitFateDecision(
            host.RoomCode,
            guest.PlayerId,
            manager.GetSnapshot(host.RoomCode).ActiveFateEvent!.EventId,
            string.Empty);

        Assert.Multiple(() =>
        {
            Assert.That(submission.Status, Is.EqualTo(FateEventSubmissionStatus.Resolved));
            Assert.That(submission.Resolution!.EventApplied, Is.True);
            Assert.That(submission.Snapshot.Players.Single(
                player => player.PlayerId == guest.PlayerId).Health, Is.EqualTo(88));
            Assert.That(submission.Snapshot.Phase, Is.EqualTo(OnlineMatchPhase.ChoosingActions));
        });
    }

    [Test]
    public void FateEvent_AppealCanOnlyBeUsedOnceByDisadvantagedPlayer()
    {
        var (manager, host, guest) = CreateStartedRoom("FATE02");
        manager.StartFateEvent(
            host.RoomCode,
            new FateEventProposal(
                BattleEventType.RestoreHealth,
                OnlinePlayerSlot.Host,
                10,
                "补给抵达。"));
        string eventId = manager.GetSnapshot(host.RoomCode).ActiveFateEvent!.EventId;

        OnlineGameException wrongPlayer = Assert.Throws<OnlineGameException>(() =>
            manager.SubmitFateDecision(host.RoomCode, host.PlayerId, eventId, "不公平"))!;
        FateEventSubmission pending = manager.SubmitFateDecision(
            host.RoomCode,
            guest.PlayerId,
            eventId,
            "对方已经领先，继续恢复会扩大优势。");
        FateEventSubmission resolved = manager.ResolveFateAppeal(
            host.RoomCode,
            eventId,
            FateAppealDecision.Revoke,
            "领先方不应继续获得恢复。" );

        Assert.Multiple(() =>
        {
            Assert.That(wrongPlayer.Code, Is.EqualTo(OnlineErrorCode.NotDisadvantagedPlayer));
            Assert.That(pending.Status, Is.EqualTo(FateEventSubmissionStatus.AwaitingJudgment));
            Assert.That(resolved.Resolution!.EventApplied, Is.False);
            Assert.That(resolved.Snapshot.Players.Single(
                player => player.PlayerId == host.PlayerId).Health, Is.EqualTo(100));
            Assert.That(resolved.Snapshot.Players.Single(
                player => player.PlayerId == guest.PlayerId).AppealAvailable, Is.False);
        });
    }

    private static OnlineRoomManager CreateManager(string roomCode)
    {
        return new OnlineRoomManager(new StubRoomCodeGenerator(roomCode));
    }

    private static (OnlineRoomManager Manager, RoomAccess Host, RoomAccess Guest)
        CreateStartedRoom(string roomCode)
    {
        OnlineRoomManager manager = CreateManager(roomCode);
        RoomAccess host = manager.CreateRoom("玩家甲");
        RoomAccess guest = manager.JoinRoom(roomCode, "玩家乙");
        return (manager, host, guest);
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
}
