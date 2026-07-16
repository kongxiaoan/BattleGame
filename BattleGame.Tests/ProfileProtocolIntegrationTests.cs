using BattleGame.Online;
using BattleGame.Persistence;
using System.Text.Json;

namespace BattleGame.Tests;

[TestFixture]
public sealed class ProfileProtocolIntegrationTests
{
    private string _databasePath = null!;

    [SetUp]
    public void SetUp()
    {
        _databasePath = Path.Combine(
            Path.GetTempPath(),
            $"BattleGame-protocol-{Guid.NewGuid():N}.db");
    }

    [TearDown]
    public void TearDown()
    {
        File.Delete(_databasePath);
        File.Delete(_databasePath + "-shm");
        File.Delete(_databasePath + "-wal");
    }

    [Test]
    public async Task CompletedMatch_ShouldPersistProfilesAndAllowWinnerToGrantTag()
    {
        var profiles = new SqlitePlayerProfileService(
            _databasePath,
            new BuiltInTagCatalog());
        await profiles.InitializeAsync();
        var manager = new OnlineRoomManager(new StubRoomCodeGenerator());
        var processor = new OnlineCommandProcessor(
            manager,
            new OnlineConnectionRegistry(),
            profiles);
        var host = new RecordingConnection("host");
        var guest = new RecordingConnection("guest");

        await processor.ProcessAsync(host.Connection, """
            {"type":"createRoom","payload":{"displayName":"甲","deviceToken":"device-host-12345678901234567890"}}
            """);
        await processor.ProcessAsync(guest.Connection, """
            {"type":"joinRoom","payload":{"roomCode":"PROF01","displayName":"乙","deviceToken":"device-guest-1234567890123456789"}}
            """);
        RoomAccess hostAccess = (RoomAccess)host.Messages.Single(
            message => message.Type == ServerMessageTypes.RoomAccess).Payload!;
        RoomAccess guestAccess = (RoomAccess)guest.Messages.Single(
            message => message.Type == ServerMessageTypes.RoomAccess).Payload!;

        for (int round = 1; round <= 4; round++)
        {
            await processor.ProcessAsync(host.Connection, CreateAction(round, "break"));
            await processor.ProcessAsync(guest.Connection, CreateAction(round, "guard"));
        }

        PlayerProfile hostProfile = await profiles.GetByDeviceTokenAsync(
            "device-host-12345678901234567890");
        PlayerProfile guestProfile = await profiles.GetByDeviceTokenAsync(
            "device-guest-1234567890123456789");
        host.Messages.Clear();
        await processor.ProcessAsync(host.Connection, """
            {"type":"getTagCandidates","payload":{}}
            """);
        IReadOnlyList<TagDefinition> candidates = (IReadOnlyList<TagDefinition>)
            host.Messages.Single().Payload!;
        host.Messages.Clear();
        await processor.ProcessAsync(host.Connection, """
            {"type":"grantTag","payload":{"tagCode":"never_give_up"}}
            """);
        PlayerProfile taggedProfile = (PlayerProfile)host.Messages.Single().Payload!;

        Assert.Multiple(() =>
        {
            Assert.That(hostAccess.ProfileId, Is.EqualTo(hostProfile.ProfileId));
            Assert.That(guestAccess.ProfileId, Is.EqualTo(guestProfile.ProfileId));
            Assert.That(hostProfile.Wins, Is.EqualTo(1));
            Assert.That(guestProfile.Losses, Is.EqualTo(1));
            Assert.That(candidates.Select(tag => tag.Code), Does.Contain("never_give_up"));
            Assert.That(taggedProfile.ProfileId, Is.EqualTo(guestProfile.ProfileId));
            Assert.That(taggedProfile.Tags.Single().Code, Is.EqualTo("never_give_up"));
        });
    }

    private static string CreateAction(int round, string action)
    {
        return JsonSerializer.Serialize(new
        {
            type = "submitAction",
            payload = new { expectedRound = round, action }
        });
    }

    private sealed class RecordingConnection
    {
        public RecordingConnection(string id)
        {
            Connection = new OnlineClientConnection(
                id,
                (message, _) =>
                {
                    Messages.Add(message);
                    return Task.CompletedTask;
                });
        }

        public OnlineClientConnection Connection { get; }
        public List<ServerMessage> Messages { get; } = new();
    }

    private sealed class StubRoomCodeGenerator : IRoomCodeGenerator
    {
        public string Create() => "PROF01";
    }
}
