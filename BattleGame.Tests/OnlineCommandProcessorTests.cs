using BattleGame.Online;
using BattleGame.Core;
using System.Text.Json;

namespace BattleGame.Tests;

[TestFixture]
public sealed class OnlineCommandProcessorTests
{
    [Test]
    public async Task CreateAndJoin_ShouldReturnPrivateAccessAndBroadcastPublicState()
    {
        var processor = CreateProcessor("PLAY01");
        var host = new RecordingConnection("host-connection");
        var guest = new RecordingConnection("guest-connection");

        await processor.ProcessAsync(
            host.Connection,
            """{"type":"createRoom","requestId":"r1","payload":{"displayName":"甲"}}""");
        await processor.ProcessAsync(
            guest.Connection,
            """{"type":"joinRoom","requestId":"r2","payload":{"roomCode":"PLAY01","displayName":"乙"}}""");

        RoomAccess hostAccess = (RoomAccess)host.Messages.Single(
            message => message.Type == ServerMessageTypes.RoomAccess).Payload!;
        RoomAccess guestAccess = (RoomAccess)guest.Messages.Single(
            message => message.Type == ServerMessageTypes.RoomAccess).Payload!;
        Assert.Multiple(() =>
        {
            Assert.That(hostAccess.ReconnectToken, Is.Not.Empty);
            Assert.That(guestAccess.ReconnectToken, Is.Not.Empty);
            Assert.That(host.Messages.Any(message => message.Type == ServerMessageTypes.GameState), Is.True);
            Assert.That(guest.Messages.Any(message => message.Type == ServerMessageTypes.GameState), Is.True);
            Assert.That(host.Connection.PlayerId, Is.EqualTo(hostAccess.PlayerId));
            Assert.That(guest.Connection.PlayerId, Is.EqualTo(guestAccess.PlayerId));
        });
    }

    [Test]
    public async Task SubmitActions_ShouldBroadcastSecretLockThenResolvedRound()
    {
        var processor = CreateProcessor("PLAY02");
        var host = new RecordingConnection("host");
        var guest = new RecordingConnection("guest");
        await processor.ProcessAsync(
            host.Connection,
            """{"type":"createRoom","payload":{"displayName":"甲"}}""");
        await processor.ProcessAsync(
            guest.Connection,
            """{"type":"joinRoom","payload":{"roomCode":"PLAY02","displayName":"乙"}}""");
        host.Messages.Clear();
        guest.Messages.Clear();

        await processor.ProcessAsync(
            host.Connection,
            """{"type":"submitAction","requestId":"a1","payload":{"expectedRound":1,"action":"Attack"}}""");

        ActionSubmission pending = (ActionSubmission)guest.Messages.Single().Payload!;
        Assert.Multiple(() =>
        {
            Assert.That(pending.Status, Is.EqualTo(ActionSubmissionStatus.WaitingForOpponent));
            Assert.That(pending.ResolvedRound, Is.Null);
            Assert.That(pending.Snapshot.Players.Single(
                player => player.PlayerId == host.Connection.PlayerId).ActionLocked, Is.True);
        });

        host.Messages.Clear();
        guest.Messages.Clear();
        await processor.ProcessAsync(
            guest.Connection,
            """{"type":"submitAction","requestId":"a2","payload":{"expectedRound":1,"action":"Guard"}}""");

        Assert.Multiple(() =>
        {
            Assert.That(host.Messages.Single().Type, Is.EqualTo(ServerMessageTypes.RoundResolved));
            Assert.That(guest.Messages.Single().Type, Is.EqualTo(ServerMessageTypes.RoundResolved));
            Assert.That(((ActionSubmission)host.Messages.Single().Payload!).ResolvedRound, Is.Not.Null);
        });
    }

    [Test]
    public async Task InvalidJson_ShouldReturnProtocolErrorWithoutClosingConnection()
    {
        var processor = CreateProcessor("PLAY03");
        var client = new RecordingConnection("broken-client");

        await processor.ProcessAsync(client.Connection, "not-json");
        await processor.ProcessAsync(
            client.Connection,
            """{"type":"createRoom","payload":{"displayName":"仍可使用"}}""");

        Assert.Multiple(() =>
        {
            Assert.That(client.Messages[0].Type, Is.EqualTo(ServerMessageTypes.Error));
            Assert.That(((ProtocolError)client.Messages[0].Payload!).Code,
                Is.EqualTo(ProtocolErrorCodes.InvalidMessage));
            Assert.That(client.Messages[1].Type, Is.EqualTo(ServerMessageTypes.RoomAccess));
        });
    }

    [Test]
    public async Task UnknownCommand_ShouldReturnStableErrorCode()
    {
        var processor = CreateProcessor("PLAY04");
        var client = new RecordingConnection("client");

        await processor.ProcessAsync(
            client.Connection,
            """{"type":"deleteEverything","requestId":"bad","payload":{}}""");

        var error = (ProtocolError)client.Messages.Single().Payload!;
        Assert.Multiple(() =>
        {
            Assert.That(error.Code, Is.EqualTo(ProtocolErrorCodes.UnknownCommand));
            Assert.That(client.Messages.Single().RequestId, Is.EqualTo("bad"));
        });
    }

    [Test]
    public async Task CommandBeforeAuthentication_ShouldReturnNotAuthenticated()
    {
        var processor = CreateProcessor("PLAY05");
        var client = new RecordingConnection("client");

        await processor.ProcessAsync(
            client.Connection,
            """{"type":"submitAction","payload":{"expectedRound":1,"action":"Attack"}}""");

        var error = (ProtocolError)client.Messages.Single().Payload!;
        Assert.That(error.Code, Is.EqualTo(ProtocolErrorCodes.NotAuthenticated));
    }

    [Test]
    public async Task Reconnect_ShouldReplaceConnectionAndReceiveCurrentState()
    {
        var processor = CreateProcessor("PLAY06");
        var original = new RecordingConnection("old");
        await processor.ProcessAsync(
            original.Connection,
            """{"type":"createRoom","payload":{"displayName":"甲"}}""");
        RoomAccess access = (RoomAccess)original.Messages.Single(
            message => message.Type == ServerMessageTypes.RoomAccess).Payload!;
        var restored = new RecordingConnection("new");

        string reconnect = JsonSerializer.Serialize(new
        {
            type = "reconnect",
            requestId = "r",
            payload = new
            {
                roomCode = access.RoomCode,
                playerId = access.PlayerId,
                reconnectToken = access.ReconnectToken
            }
        });
        await processor.ProcessAsync(restored.Connection, reconnect);

        Assert.Multiple(() =>
        {
            Assert.That(restored.Connection.PlayerId, Is.EqualTo(access.PlayerId));
            Assert.That(restored.Messages.Any(message => message.Type == ServerMessageTypes.RoomAccess), Is.True);
            Assert.That(restored.Messages.Any(message => message.Type == ServerMessageTypes.GameState), Is.True);
        });
    }

    [Test]
    public async Task ThirdRound_ShouldRunServerFateAgentAndBroadcastAppealVerdict()
    {
        var agent = new StubFateAgent();
        var processor = CreateProcessor("FATE03", agent);
        var host = new RecordingConnection("host");
        var guest = new RecordingConnection("guest");
        await processor.ProcessAsync(host.Connection,
            """{"type":"createRoom","payload":{"displayName":"甲"}}""");
        await processor.ProcessAsync(guest.Connection,
            """{"type":"joinRoom","payload":{"roomCode":"FATE03","displayName":"乙"}}""");

        for (int round = 1; round <= 3; round++)
        {
            string action = JsonSerializer.Serialize(new
            {
                type = ClientMessageTypes.SubmitAction,
                payload = new { expectedRound = round, action = CombatAction.Guard }
            }, OnlineProtocolJson.Options);
            await processor.ProcessAsync(host.Connection, action);
            await processor.ProcessAsync(guest.Connection, action);
        }

        ServerMessage? thirdRoundMessage = host.Messages.LastOrDefault(
            message => message.Type == ServerMessageTypes.RoundResolved);
        Assert.That(
            thirdRoundMessage,
            Is.Not.Null,
            string.Join(",", host.Messages.Select(message => message.Type + ":" +
                (message.Payload is ProtocolError error ? error.Code : string.Empty))));
        ActionSubmission thirdRound = (ActionSubmission)thirdRoundMessage!.Payload!;
        FateEventSnapshot fateEvent = thirdRound.Snapshot.ActiveFateEvent!;
        await processor.ProcessAsync(guest.Connection, JsonSerializer.Serialize(new
        {
            type = ClientMessageTypes.SubmitFateDecision,
            payload = new { eventId = fateEvent.EventId, argument = "生命差会继续扩大" }
        }));

        FateEventSubmission result = (FateEventSubmission)host.Messages.Last(
            message => message.Type == ServerMessageTypes.FateResolved).Payload!;
        Assert.Multiple(() =>
        {
            Assert.That(agent.CreateCalls, Is.EqualTo(1));
            Assert.That(agent.JudgeCalls, Is.EqualTo(1));
            Assert.That(result.Resolution!.EventApplied, Is.False);
            Assert.That(result.Snapshot.Phase, Is.EqualTo(OnlineMatchPhase.ChoosingActions));
        });
    }

    private static OnlineCommandProcessor CreateProcessor(
        string code,
        IOnlineFateAgent? fateAgent = null)
    {
        var manager = new OnlineRoomManager(new StubRoomCodeGenerator(code));
        return new OnlineCommandProcessor(
            manager,
            new OnlineConnectionRegistry(),
            fateAgent: fateAgent);
    }

    private sealed class RecordingConnection
    {
        public RecordingConnection(string connectionId)
        {
            Connection = new OnlineClientConnection(
                connectionId,
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
        private readonly string _code;

        public StubRoomCodeGenerator(string code)
        {
            _code = code;
        }

        public string Create() => _code;
    }

    private sealed class StubFateAgent : IOnlineFateAgent
    {
        public int CreateCalls { get; private set; }
        public int JudgeCalls { get; private set; }

        public Task<FateEventProposal> CreateEventAsync(
            OnlineMatchSnapshot snapshot,
            int eventNumber,
            CancellationToken cancellationToken)
        {
            CreateCalls++;
            return Task.FromResult(new FateEventProposal(
                BattleEventType.LoseHealth,
                OnlinePlayerSlot.Guest,
                10,
                "测试事件"));
        }

        public Task<FateAppealVerdict> JudgeAsync(
            FateAppealContext appeal,
            CancellationToken cancellationToken)
        {
            JudgeCalls++;
            return Task.FromResult(new FateAppealVerdict(
                FateAppealDecision.Revoke,
                "测试撤销"));
        }
    }
}
