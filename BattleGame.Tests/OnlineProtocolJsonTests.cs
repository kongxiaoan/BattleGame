using System.Text.Json;
using BattleGame.Core;
using BattleGame.Online;

namespace BattleGame.Tests;

[TestFixture]
public sealed class OnlineProtocolJsonTests
{
    [Test]
    public void Serialize_ShouldUseCamelCaseAndStringEnumsForStableCrossFrontendProtocol()
    {
        var snapshot = new ResolvedRoundSnapshot(
            3,
            CombatAction.Attack,
            CombatAction.Guard,
            5,
            20,
            0,
            0);
        var message = new ServerMessage(ServerMessageTypes.RoundResolved, "req-1", snapshot);

        string json = OnlineProtocolJson.Serialize(message);

        using JsonDocument document = JsonDocument.Parse(json);
        Assert.Multiple(() =>
        {
            Assert.That(document.RootElement.GetProperty("type").GetString(),
                Is.EqualTo("roundResolved"));
            Assert.That(document.RootElement.GetProperty("requestId").GetString(),
                Is.EqualTo("req-1"));
            Assert.That(document.RootElement.GetProperty("payload")
                .GetProperty("hostAction").GetString(), Is.EqualTo("attack"));
        });
    }

    [Test]
    public void SerializeRoomAccess_ShouldNotPutReconnectTokenInsidePublicSnapshot()
    {
        Guid playerId = Guid.NewGuid();
        var access = new RoomAccess(
            "SAFE01",
            playerId,
            "private-token",
            new OnlineMatchSnapshot(
                "SAFE01",
                OnlineRoomStatus.WaitingForGuest,
                0,
                BattleOutcome.Ongoing,
                new[]
                {
                    new PlayerSnapshot(
                        playerId,
                        "甲",
                        OnlinePlayerSlot.Host,
                        100,
                        100,
                        0,
                        false)
                }));

        string json = OnlineProtocolJson.Serialize(
            new ServerMessage(ServerMessageTypes.RoomAccess, null, access));
        using JsonDocument document = JsonDocument.Parse(json);
        JsonElement payload = document.RootElement.GetProperty("payload");

        Assert.Multiple(() =>
        {
            Assert.That(payload.GetProperty("reconnectToken").GetString(),
                Is.EqualTo("private-token"));
            Assert.That(payload.GetProperty("snapshot").GetRawText(),
                Does.Not.Contain("private-token"));
        });
    }
}
