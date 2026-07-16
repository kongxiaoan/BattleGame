using BattleGame.Cli.Online;

namespace BattleGame.Tests;

[TestFixture]
public sealed class OnlineServerConfigurationTests
{
    [Test]
    public void Resolve_WhenEnvironmentValueExists_ShouldTakePriority()
    {
        string? value = OnlineServerConfiguration.Resolve(
            "wss://env.example/ws",
            new[] { "wss://file.example/ws" });

        Assert.That(value, Is.EqualTo("wss://env.example/ws"));
    }

    [Test]
    public void Resolve_ShouldIgnoreCommentsAndUseFirstConfiguredLine()
    {
        string? value = OnlineServerConfiguration.Resolve(
            null,
            new[] { "# 配置说明", "", "  wss://game.example/ws  ", "wss://backup/ws" });

        Assert.That(value, Is.EqualTo("wss://game.example/ws"));
    }

    [Test]
    public void Resolve_WhenNothingConfigured_ShouldReturnNull()
    {
        Assert.That(
            OnlineServerConfiguration.Resolve(null, new[] { "# 暂未配置", " " }),
            Is.Null);
    }
}
