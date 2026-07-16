using BattleGame.Core;
using BattleGame.Online;

namespace BattleGame.Tests;

[TestFixture]
public sealed class OnlineActionTimeoutTests
{
    [Test]
    public void ExpireActions_ShouldKeepLockedActionAndAutoChooseForMissingPlayer()
    {
        var clock = new ManualTimeProvider(
            new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero));
        var manager = new OnlineRoomManager(
            new StubRoomCodeGenerator(),
            timeProvider: clock);
        RoomAccess host = manager.CreateRoom("甲");
        manager.JoinRoom(host.RoomCode, "乙");
        manager.SubmitAction(host.RoomCode, host.PlayerId, 1, CombatAction.Guard);

        clock.Advance(TimeSpan.FromSeconds(15));
        ActionSubmission submission = manager.ExpireActions().Single();

        Assert.Multiple(() =>
        {
            Assert.That(submission.Status, Is.EqualTo(ActionSubmissionStatus.RoundResolved));
            Assert.That(submission.ResolvedRound!.HostAction, Is.EqualTo(CombatAction.Guard));
            Assert.That(submission.Snapshot.RoundNumber, Is.EqualTo(2));
        });
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private DateTimeOffset _now;
        public ManualTimeProvider(DateTimeOffset now) => _now = now;
        public override DateTimeOffset GetUtcNow() => _now;
        public void Advance(TimeSpan duration) => _now += duration;
    }

    private sealed class StubRoomCodeGenerator : IRoomCodeGenerator
    {
        public string Create() => "TIME01";
    }
}
