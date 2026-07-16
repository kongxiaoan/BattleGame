using BattleGame.Core;
using BattleGame.Online;
using BattleGame.Persistence;

namespace BattleGame.Tests;

[TestFixture]
public sealed class SqlitePlayerProfileServiceTests
{
    private string _databasePath = null!;
    private ManualTimeProvider _clock = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(
            Path.GetTempPath(),
            $"BattleGame-profile-{Guid.NewGuid():N}.db");
        _clock = new ManualTimeProvider(
            new DateTimeOffset(2026, 7, 15, 12, 0, 0, TimeSpan.Zero));
    }

    [TearDown]
    public void TearDown()
    {
        File.Delete(_databasePath);
        File.Delete(_databasePath + "-shm");
        File.Delete(_databasePath + "-wal");
    }

    [Test]
    public async Task GetOrCreate_ShouldPersistProfileAcrossServiceInstances()
    {
        var first = CreateService();
        await first.InitializeAsync();
        PlayerProfile profile = await first.GetOrCreateAsync(
            "device-token-aaaaaaaaaaaaaaaaaaaa",
            "玩家甲");

        var second = CreateService();
        await second.InitializeAsync();
        PlayerProfile restored = await second.GetOrCreateAsync(
            "device-token-aaaaaaaaaaaaaaaaaaaa",
            "玩家甲的新名字");

        Assert.Multiple(() =>
        {
            Assert.That(restored.ProfileId, Is.EqualTo(profile.ProfileId));
            Assert.That(restored.DisplayName, Is.EqualTo("玩家甲的新名字"));
            Assert.That(restored.GamesPlayed, Is.Zero);
        });
    }

    [Test]
    public async Task RecordMatch_ShouldBeIdempotentAndUpdateBothProfiles()
    {
        var service = CreateService();
        await service.InitializeAsync();
        PlayerProfile host = await service.GetOrCreateAsync(
            "device-token-host-aaaaaaaaaaaaaaaa",
            "甲");
        PlayerProfile guest = await service.GetOrCreateAsync(
            "device-token-guest-aaaaaaaaaaaaaaa",
            "乙");

        await service.RecordMatchAsync(
            "ROOM01",
            host.ProfileId,
            guest.ProfileId,
            BattleOutcome.HumanWin);
        await service.RecordMatchAsync(
            "ROOM01",
            host.ProfileId,
            guest.ProfileId,
            BattleOutcome.HumanWin);

        PlayerProfile hostResult = await service.GetByDeviceTokenAsync(
            "device-token-host-aaaaaaaaaaaaaaaa");
        PlayerProfile guestResult = await service.GetByDeviceTokenAsync(
            "device-token-guest-aaaaaaaaaaaaaaa");
        Assert.Multiple(() =>
        {
            Assert.That(hostResult.GamesPlayed, Is.EqualTo(1));
            Assert.That(hostResult.Wins, Is.EqualTo(1));
            Assert.That(guestResult.GamesPlayed, Is.EqualTo(1));
            Assert.That(guestResult.Losses, Is.EqualTo(1));
        });
    }

    [Test]
    public async Task Winner_ShouldGrantOneAuditedTagToLoser()
    {
        var service = CreateService();
        await service.InitializeAsync();
        PlayerProfile host = await service.GetOrCreateAsync(
            "device-token-host-bbbbbbbbbbbbbbbb",
            "甲");
        PlayerProfile guest = await service.GetOrCreateAsync(
            "device-token-guest-bbbbbbbbbbbbbbb",
            "乙");
        await service.RecordMatchAsync(
            "ROOM02",
            host.ProfileId,
            guest.ProfileId,
            BattleOutcome.HumanWin);

        IReadOnlyList<TagDefinition> candidates = await service.GetTagCandidatesAsync(
            "ROOM02",
            host.ProfileId);
        PlayerProfile tagged = await service.GrantTagAsync(
            "ROOM02",
            host.ProfileId,
            "never_give_up");
        ProfileException hiddenCandidate = Assert.ThrowsAsync<ProfileException>(async () =>
            await service.GrantTagAsync("ROOM99", host.ProfileId, "fate_survivor"))!;

        Assert.Multiple(() =>
        {
            Assert.That(candidates, Has.Count.EqualTo(3));
            Assert.That(candidates.Select(tag => tag.Code), Does.Contain("never_give_up"));
            Assert.That(tagged.ProfileId, Is.EqualTo(guest.ProfileId));
            Assert.That(tagged.Tags.Single().Code, Is.EqualTo("never_give_up"));
            Assert.That(tagged.Tags.Single().GrantedByProfileId, Is.EqualTo(host.ProfileId));
            Assert.That(hiddenCandidate.Code, Is.EqualTo(ProfileErrorCode.TagNotAllowed));
        });
    }

    [Test]
    public async Task LoserOrSecondGrant_ShouldBeRejected()
    {
        var service = CreateService();
        await service.InitializeAsync();
        PlayerProfile host = await service.GetOrCreateAsync(
            "device-token-host-cccccccccccccccc",
            "甲");
        PlayerProfile guest = await service.GetOrCreateAsync(
            "device-token-guest-ccccccccccccccc",
            "乙");
        await service.RecordMatchAsync(
            "ROOM03",
            host.ProfileId,
            guest.ProfileId,
            BattleOutcome.HumanWin);

        ProfileException loserError = Assert.ThrowsAsync<ProfileException>(() =>
            service.GrantTagAsync("ROOM03", guest.ProfileId, "never_give_up"))!;
        await service.GrantTagAsync("ROOM03", host.ProfileId, "never_give_up");
        ProfileException duplicateError = Assert.ThrowsAsync<ProfileException>(() =>
            service.GrantTagAsync("ROOM03", host.ProfileId, "defense_master"))!;

        Assert.Multiple(() =>
        {
            Assert.That(loserError.Code, Is.EqualTo(ProfileErrorCode.NotMatchWinner));
            Assert.That(duplicateError.Code, Is.EqualTo(ProfileErrorCode.TagAlreadyGranted));
        });
    }

    private SqlitePlayerProfileService CreateService()
    {
        return new SqlitePlayerProfileService(
            _databasePath,
            new StubTagCatalog(),
            _clock);
    }

    private sealed class StubTagCatalog : ITagCatalog
    {
        private static readonly TagDefinition[] Tags =
        {
            new("never_give_up", "永不言弃", TimeSpan.FromHours(24)),
            new("defense_master", "防守大师", TimeSpan.FromHours(24)),
            new("close_call", "差一题就赢", TimeSpan.FromHours(24)),
            new("fate_survivor", "绝境幸存者", null)
        };

        public IReadOnlyList<TagDefinition> GetAll() => Tags;
    }

    private sealed class ManualTimeProvider : TimeProvider
    {
        private readonly DateTimeOffset _utcNow;

        public ManualTimeProvider(DateTimeOffset utcNow)
        {
            _utcNow = utcNow;
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;
    }
}
