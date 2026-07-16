using System.Text.Json;

namespace BattleGame.Online;

/// <summary>
/// 将不可信 JSON 命令转换为房间状态机调用。该层只编排和广播，规则仍由
/// OnlineRoom 与 StrategicBattle 掌握。
/// </summary>
public sealed class OnlineCommandProcessor
{
    private readonly OnlineRoomManager _roomManager;
    private readonly OnlineConnectionRegistry _connections;
    private readonly IPlayerProfileService? _profileService;
    private readonly IOnlineFateAgent? _fateAgent;

    public OnlineCommandProcessor(
        OnlineRoomManager roomManager,
        OnlineConnectionRegistry connections,
        IPlayerProfileService? profileService = null,
        IOnlineFateAgent? fateAgent = null)
    {
        _roomManager = roomManager ?? throw new ArgumentNullException(nameof(roomManager));
        _connections = connections ?? throw new ArgumentNullException(nameof(connections));
        _profileService = profileService;
        _fateAgent = fateAgent;
    }

    public async Task ProcessAsync(
        OnlineClientConnection connection,
        string json,
        CancellationToken cancellationToken = default)
    {
        string? requestId = null;
        try
        {
            ClientEnvelope envelope = JsonSerializer.Deserialize<ClientEnvelope>(
                json,
                OnlineProtocolJson.Options) ?? throw new JsonException();
            requestId = envelope.RequestId;
            if (string.IsNullOrWhiteSpace(envelope.Type)
                || envelope.Payload.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
            {
                throw new JsonException();
            }

            switch (envelope.Type)
            {
                case ClientMessageTypes.CreateRoom:
                    await CreateRoomAsync(connection, envelope, cancellationToken)
                        .ConfigureAwait(false);
                    break;
                case ClientMessageTypes.JoinRoom:
                    await JoinRoomAsync(connection, envelope, cancellationToken)
                        .ConfigureAwait(false);
                    break;
                case ClientMessageTypes.Reconnect:
                    await ReconnectAsync(connection, envelope, cancellationToken)
                        .ConfigureAwait(false);
                    break;
                case ClientMessageTypes.SubmitAction:
                    await SubmitActionAsync(connection, envelope, cancellationToken)
                        .ConfigureAwait(false);
                    break;
                case ClientMessageTypes.SubmitAnswer:
                    await SubmitAnswerAsync(connection, envelope, cancellationToken)
                        .ConfigureAwait(false);
                    break;
                case ClientMessageTypes.SubmitLastChanceEntry:
                    await SubmitLastChanceEntryAsync(connection, envelope, cancellationToken)
                        .ConfigureAwait(false);
                    break;
                case ClientMessageTypes.GetState:
                    await SendStateAsync(connection, envelope.RequestId, cancellationToken)
                        .ConfigureAwait(false);
                    break;
                case ClientMessageTypes.Ping:
                    await connection.SendAsync(
                        new ServerMessage(ServerMessageTypes.Pong, envelope.RequestId, null),
                        cancellationToken).ConfigureAwait(false);
                    break;
                case ClientMessageTypes.GetProfile:
                    await GetProfileAsync(connection, envelope, cancellationToken)
                        .ConfigureAwait(false);
                    break;
                case ClientMessageTypes.GetTagCandidates:
                    await GetTagCandidatesAsync(connection, envelope, cancellationToken)
                        .ConfigureAwait(false);
                    break;
                case ClientMessageTypes.GrantTag:
                    await GrantTagAsync(connection, envelope, cancellationToken)
                        .ConfigureAwait(false);
                    break;
                case ClientMessageTypes.SubmitFateDecision:
                    await SubmitFateDecisionAsync(connection, envelope, cancellationToken)
                        .ConfigureAwait(false);
                    break;
                default:
                    await SendErrorAsync(
                        connection,
                        envelope.RequestId,
                        ProtocolErrorCodes.UnknownCommand,
                        cancellationToken).ConfigureAwait(false);
                    break;
            }
        }
        catch (OnlineGameException exception)
        {
            await SendErrorAsync(
                connection,
                requestId,
                exception.Code.ToString().ToUpperInvariant(),
                cancellationToken).ConfigureAwait(false);
        }
        catch (ProtocolCommandException exception)
        {
            await SendErrorAsync(
                connection,
                requestId,
                exception.Code,
                cancellationToken).ConfigureAwait(false);
        }
        catch (ProfileException exception)
        {
            await SendErrorAsync(
                connection,
                requestId,
                exception.Code.ToString().ToUpperInvariant(),
                cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (
            exception is JsonException
                or NotSupportedException
                or ArgumentException)
        {
            await SendErrorAsync(
                connection,
                requestId,
                ProtocolErrorCodes.InvalidMessage,
                cancellationToken).ConfigureAwait(false);
        }
    }

    public void Disconnect(OnlineClientConnection connection)
    {
        _connections.Remove(connection);
    }

    public async Task ProcessTimeoutsAsync(CancellationToken cancellationToken)
    {
        foreach (QuestionSubmission submission in _roomManager.ExpireQuestions())
        {
            await _connections.BroadcastAsync(
                submission.Snapshot,
                new ServerMessage(
                    ServerMessageTypes.QuestionResolved,
                    null,
                    submission),
                cancellationToken).ConfigureAwait(false);
        }


        foreach (LastChanceSubmission submission in _roomManager.ExpireLastChanceChallenges())
        {
            await RecordCompletedMatchAsync(submission.Snapshot, cancellationToken)
                .ConfigureAwait(false);
            await _connections.BroadcastAsync(
                submission.Snapshot,
                new ServerMessage(
                    ServerMessageTypes.LastChanceResolved,
                    null,
                    submission),
                cancellationToken).ConfigureAwait(false);
        }


        foreach (FateEventSubmission submission in _roomManager.ExpireFateEvents())
        {
            await _connections.BroadcastAsync(
                submission.Snapshot,
                new ServerMessage(ServerMessageTypes.FateResolved, null, submission),
                cancellationToken).ConfigureAwait(false);
        }


        foreach (ActionSubmission expired in _roomManager.ExpireActions())
        {
            ActionSubmission submission = await StartFateIfRequiredAsync(
                expired,
                cancellationToken).ConfigureAwait(false);
            await RecordCompletedMatchAsync(submission.Snapshot, cancellationToken)
                .ConfigureAwait(false);
            await _connections.BroadcastAsync(
                submission.Snapshot,
                new ServerMessage(ServerMessageTypes.RoundResolved, null, submission),
                cancellationToken).ConfigureAwait(false);
        }
    }

    private async Task CreateRoomAsync(
        OnlineClientConnection connection,
        ClientEnvelope envelope,
        CancellationToken cancellationToken)
    {
        CreateRoomCommand command = DeserializePayload<CreateRoomCommand>(envelope);
        Guid? profileId = await ResolveProfileIdAsync(
            command.DeviceToken,
            command.DisplayName!,
            cancellationToken).ConfigureAwait(false);
        RoomAccess access = _roomManager.CreateRoom(command.DisplayName!, profileId);
        Bind(connection, access);
        await connection.SendAsync(
            new ServerMessage(ServerMessageTypes.RoomAccess, envelope.RequestId, access),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task JoinRoomAsync(
        OnlineClientConnection connection,
        ClientEnvelope envelope,
        CancellationToken cancellationToken)
    {
        JoinRoomCommand command = DeserializePayload<JoinRoomCommand>(envelope);
        Guid? profileId = await ResolveProfileIdAsync(
            command.DeviceToken,
            command.DisplayName!,
            cancellationToken).ConfigureAwait(false);
        RoomAccess access = _roomManager.JoinRoom(
            command.RoomCode!,
            command.DisplayName!,
            profileId);
        Bind(connection, access);
        await connection.SendAsync(
            new ServerMessage(ServerMessageTypes.RoomAccess, envelope.RequestId, access),
            cancellationToken).ConfigureAwait(false);
        await BroadcastStateAsync(access.Snapshot, null, cancellationToken).ConfigureAwait(false);
    }

    private async Task ReconnectAsync(
        OnlineClientConnection connection,
        ClientEnvelope envelope,
        CancellationToken cancellationToken)
    {
        ReconnectCommand command = DeserializePayload<ReconnectCommand>(envelope);
        RoomAccess access = _roomManager.Reconnect(
            command.RoomCode!,
            command.PlayerId,
            command.ReconnectToken!);
        Bind(connection, access);
        await connection.SendAsync(
            new ServerMessage(ServerMessageTypes.RoomAccess, envelope.RequestId, access),
            cancellationToken).ConfigureAwait(false);
        await connection.SendAsync(
            new ServerMessage(ServerMessageTypes.GameState, null, access.Snapshot),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task SubmitActionAsync(
        OnlineClientConnection connection,
        ClientEnvelope envelope,
        CancellationToken cancellationToken)
    {
        (string roomCode, Guid playerId) = RequireSession(connection);
        SubmitActionCommand command = DeserializePayload<SubmitActionCommand>(envelope);
        ActionSubmission submission = _roomManager.SubmitAction(
            roomCode,
            playerId,
            command.ExpectedRound,
            command.Action);
        submission = await StartFateIfRequiredAsync(submission, cancellationToken)
            .ConfigureAwait(false);
        string messageType = submission.Status == ActionSubmissionStatus.RoundResolved
            ? ServerMessageTypes.RoundResolved
            : ServerMessageTypes.ActionStatus;
        await RecordCompletedMatchAsync(submission.Snapshot, cancellationToken)
            .ConfigureAwait(false);
        await _connections.BroadcastAsync(
            submission.Snapshot,
            new ServerMessage(messageType, envelope.RequestId, submission),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<ActionSubmission> StartFateIfRequiredAsync(
        ActionSubmission submission,
        CancellationToken cancellationToken)
    {
        if (_fateAgent == null
            || submission.Status != ActionSubmissionStatus.RoundResolved
            || submission.ResolvedRound!.RoundNumber % 3 != 0
            || submission.Snapshot.Status != OnlineRoomStatus.InProgress
            || submission.Snapshot.Phase != OnlineMatchPhase.ChoosingActions)
        {
            return submission;
        }

        FateEventProposal proposal = await _fateAgent.CreateEventAsync(
            submission.Snapshot,
            submission.ResolvedRound.RoundNumber / 3,
            cancellationToken).ConfigureAwait(false);
        _roomManager.StartFateEvent(submission.Snapshot.RoomCode, proposal);
        return submission with
        {
            Snapshot = _roomManager.GetSnapshot(submission.Snapshot.RoomCode)
        };
    }

    private async Task SubmitFateDecisionAsync(
        OnlineClientConnection connection,
        ClientEnvelope envelope,
        CancellationToken cancellationToken)
    {
        (string roomCode, Guid playerId) = RequireSession(connection);
        SubmitFateDecisionCommand command =
            DeserializePayload<SubmitFateDecisionCommand>(envelope);
        FateEventSubmission submission = _roomManager.SubmitFateDecision(
            roomCode,
            playerId,
            command.EventId!,
            command.Argument ?? string.Empty);
        if (submission.Status == FateEventSubmissionStatus.AwaitingJudgment)
        {
            IOnlineFateAgent agent = _fateAgent
                ?? throw new ProtocolCommandException(ProtocolErrorCodes.UnknownCommand);
            FateAppealVerdict verdict = await agent.JudgeAsync(
                submission.Appeal!,
                cancellationToken).ConfigureAwait(false);
            submission = _roomManager.ResolveFateAppeal(
                roomCode,
                command.EventId!,
                verdict.Decision,
                verdict.Explanation);
        }

        await _connections.BroadcastAsync(
            submission.Snapshot,
            new ServerMessage(ServerMessageTypes.FateResolved, envelope.RequestId, submission),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task SendStateAsync(
        OnlineClientConnection connection,
        string? requestId,
        CancellationToken cancellationToken)
    {
        (string roomCode, _) = RequireSession(connection);
        OnlineMatchSnapshot snapshot = _roomManager.GetSnapshot(roomCode);
        await connection.SendAsync(
            new ServerMessage(ServerMessageTypes.GameState, requestId, snapshot),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task SubmitAnswerAsync(
        OnlineClientConnection connection,
        ClientEnvelope envelope,
        CancellationToken cancellationToken)
    {
        (string roomCode, Guid playerId) = RequireSession(connection);
        SubmitAnswerCommand command = DeserializePayload<SubmitAnswerCommand>(envelope);
        QuestionSubmission submission = _roomManager.SubmitAnswer(
            roomCode,
            playerId,
            command.QuestionId!,
            command.SelectedOptionIndex);
        string messageType = submission.Status == QuestionSubmissionStatus.Resolved
            ? ServerMessageTypes.QuestionResolved
            : ServerMessageTypes.QuestionStatus;
        await _connections.BroadcastAsync(
            submission.Snapshot,
            new ServerMessage(messageType, envelope.RequestId, submission),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task SubmitLastChanceEntryAsync(
        OnlineClientConnection connection,
        ClientEnvelope envelope,
        CancellationToken cancellationToken)
    {
        (string roomCode, Guid playerId) = RequireSession(connection);
        SubmitLastChanceEntryCommand command =
            DeserializePayload<SubmitLastChanceEntryCommand>(envelope);
        LastChanceSubmission submission = _roomManager.SubmitLastChanceEntry(
            roomCode,
            playerId,
            command.ChallengeId!,
            command.Entry!);
        string messageType = submission.Status == LastChanceSubmissionStatus.InProgress
            ? ServerMessageTypes.LastChanceProgress
            : ServerMessageTypes.LastChanceResolved;
        await RecordCompletedMatchAsync(submission.Snapshot, cancellationToken)
            .ConfigureAwait(false);
        await _connections.BroadcastAsync(
            submission.Snapshot,
            new ServerMessage(messageType, envelope.RequestId, submission),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task GetProfileAsync(
        OnlineClientConnection connection,
        ClientEnvelope envelope,
        CancellationToken cancellationToken)
    {
        IPlayerProfileService service = RequireProfileService();
        GetProfileCommand command = DeserializePayload<GetProfileCommand>(envelope);
        PlayerProfile profile = await service.GetByDeviceTokenAsync(
            command.DeviceToken!,
            cancellationToken).ConfigureAwait(false);
        await connection.SendAsync(
            new ServerMessage(ServerMessageTypes.Profile, envelope.RequestId, profile),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task GetTagCandidatesAsync(
        OnlineClientConnection connection,
        ClientEnvelope envelope,
        CancellationToken cancellationToken)
    {
        IPlayerProfileService service = RequireProfileService();
        (string roomCode, _) = RequireSession(connection);
        Guid profileId = connection.ProfileId
            ?? throw new ProtocolCommandException(ProtocolErrorCodes.NotAuthenticated);
        IReadOnlyList<TagDefinition> candidates = await service.GetTagCandidatesAsync(
            roomCode,
            profileId,
            cancellationToken).ConfigureAwait(false);
        await connection.SendAsync(
            new ServerMessage(
                ServerMessageTypes.TagCandidates,
                envelope.RequestId,
                candidates),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task GrantTagAsync(
        OnlineClientConnection connection,
        ClientEnvelope envelope,
        CancellationToken cancellationToken)
    {
        IPlayerProfileService service = RequireProfileService();
        (string roomCode, _) = RequireSession(connection);
        Guid profileId = connection.ProfileId
            ?? throw new ProtocolCommandException(ProtocolErrorCodes.NotAuthenticated);
        GrantTagCommand command = DeserializePayload<GrantTagCommand>(envelope);
        PlayerProfile profile = await service.GrantTagAsync(
            roomCode,
            profileId,
            command.TagCode!,
            cancellationToken).ConfigureAwait(false);
        await connection.SendAsync(
            new ServerMessage(ServerMessageTypes.TagGranted, envelope.RequestId, profile),
            cancellationToken).ConfigureAwait(false);
    }

    private async Task<Guid?> ResolveProfileIdAsync(
        string? deviceToken,
        string displayName,
        CancellationToken cancellationToken)
    {
        if (_profileService == null || string.IsNullOrWhiteSpace(deviceToken))
        {
            return null;
        }

        PlayerProfile profile = await _profileService.GetOrCreateAsync(
            deviceToken,
            displayName,
            cancellationToken).ConfigureAwait(false);
        return profile.ProfileId;
    }

    private async Task RecordCompletedMatchAsync(
        OnlineMatchSnapshot snapshot,
        CancellationToken cancellationToken)
    {
        if (_profileService == null || snapshot.Status != OnlineRoomStatus.Finished
            || snapshot.Players.Count != 2)
        {
            return;
        }

        PlayerSnapshot host = snapshot.Players.Single(
            player => player.Slot == OnlinePlayerSlot.Host);
        PlayerSnapshot guest = snapshot.Players.Single(
            player => player.Slot == OnlinePlayerSlot.Guest);
        if (host.ProfileId is not Guid hostProfileId
            || guest.ProfileId is not Guid guestProfileId)
        {
            return;
        }

        await _profileService.RecordMatchAsync(
            snapshot.RoomCode,
            hostProfileId,
            guestProfileId,
            snapshot.Outcome,
            cancellationToken).ConfigureAwait(false);
    }

    private IPlayerProfileService RequireProfileService()
    {
        return _profileService
            ?? throw new ProtocolCommandException(ProtocolErrorCodes.UnknownCommand);
    }

    private async Task BroadcastStateAsync(
        OnlineMatchSnapshot snapshot,
        string? requestId,
        CancellationToken cancellationToken)
    {
        await _connections.BroadcastAsync(
            snapshot,
            new ServerMessage(ServerMessageTypes.GameState, requestId, snapshot),
            cancellationToken).ConfigureAwait(false);
    }

    private void Bind(OnlineClientConnection connection, RoomAccess access)
    {
        connection.Bind(access);
        _connections.Register(access.PlayerId, connection);
    }

    private static T DeserializePayload<T>(ClientEnvelope envelope)
    {
        return envelope.Payload.Deserialize<T>(OnlineProtocolJson.Options)
            ?? throw new JsonException();
    }

    private static (string RoomCode, Guid PlayerId) RequireSession(
        OnlineClientConnection connection)
    {
        if (connection.RoomCode == null || connection.PlayerId is not Guid playerId)
        {
            throw new ProtocolCommandException(ProtocolErrorCodes.NotAuthenticated);
        }

        return (connection.RoomCode, playerId);
    }

    private static Task SendErrorAsync(
        OnlineClientConnection connection,
        string? requestId,
        string code,
        CancellationToken cancellationToken)
    {
        return connection.SendAsync(
            new ServerMessage(
                ServerMessageTypes.Error,
                requestId,
                new ProtocolError(code)),
            cancellationToken);
    }

    private sealed class ProtocolCommandException : Exception
    {
        public ProtocolCommandException(string code)
            : base(code)
        {
            Code = code;
        }

        public string Code { get; }
    }
}
