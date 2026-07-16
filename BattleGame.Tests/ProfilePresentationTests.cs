using BattleGame.Cli.Online;
using BattleGame.Online;

namespace BattleGame.Tests;

[TestFixture]
public sealed class ProfilePresentationTests
{
    [Test]
    public void CalculateWinRate_WhenMatchesExist_ShouldRoundToOneDecimalPlace()
    {
        Assert.That(ProfilePresentation.CalculateWinRate(2, 3), Is.EqualTo(66.7));
    }

    [Test]
    public void DescribeTagLifetime_ShouldDistinguishPermanentAndExpiringTags()
    {
        DateTimeOffset expiresAt = new(2026, 7, 20, 8, 30, 0, TimeSpan.Zero);
        var permanent = new PlayerTag("honor", "永不言弃", Guid.NewGuid(), "ABC123", expiresAt, null);
        var temporary = new PlayerTag("quick", "闪电答题王", Guid.NewGuid(), "ABC123", expiresAt, expiresAt);

        Assert.Multiple(() =>
        {
            Assert.That(ProfilePresentation.DescribeTagLifetime(permanent), Is.EqualTo("永久荣誉"));
            Assert.That(ProfilePresentation.DescribeTagLifetime(temporary), Does.Contain("2026-07-20"));
        });
    }
}
