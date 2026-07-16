using BattleGame.Cli.Online;

namespace BattleGame.Tests;

[TestFixture]
public sealed class ServerConnectionProfileTests
{
    [TestCase("192.168.1.25", "ws://192.168.1.25:5088/ws")]
    [TestCase("192.168.1.25:6000", "ws://192.168.1.25:6000/ws")]
    [TestCase("ws://192.168.1.25:6000/ws", "ws://192.168.1.25:6000/ws")]
    [TestCase("http://192.168.1.25:5088", "ws://192.168.1.25:5088/ws")]
    public void CreateLan_ShouldNormalizeFriendlyAddress(string input, string expected)
    {
        ServerConnectionProfile profile = ServerConnectionProfile.CreateLan(input);

        Assert.Multiple(() =>
        {
            Assert.That(profile.Mode, Is.EqualTo(ServerConnectionMode.Lan));
            Assert.That(profile.WebSocketEndpoint.AbsoluteUri, Is.EqualTo(expected));
        });
    }

    [TestCase("wss://game.example.com/ws", "wss://game.example.com/ws")]
    [TestCase("https://game.example.com", "wss://game.example.com/ws")]
    public void CreateOnline_ShouldUseSameProtocolOverSecureEndpoint(
        string input,
        string expected)
    {
        ServerConnectionProfile profile = ServerConnectionProfile.CreateOnline(input);

        Assert.Multiple(() =>
        {
            Assert.That(profile.Mode, Is.EqualTo(ServerConnectionMode.Online));
            Assert.That(profile.WebSocketEndpoint.AbsoluteUri, Is.EqualTo(expected));
        });
    }

    [TestCase("")]
    [TestCase("ftp://game.example.com")]
    [TestCase("not a host name")]
    public void CreateOnline_WithInvalidEndpoint_ShouldReject(string input)
    {
        Assert.Throws<ArgumentException>(() => ServerConnectionProfile.CreateOnline(input));
    }
}
