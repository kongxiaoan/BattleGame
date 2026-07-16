using System.Text.Json;
using System.Text.Json.Serialization;

namespace BattleGame.Online;

public static class ClientMessageTypes
{
    public const string CreateRoom = "createRoom";
    public const string JoinRoom = "joinRoom";
    public const string Reconnect = "reconnect";
    public const string SubmitAction = "submitAction";
    public const string SubmitAnswer = "submitAnswer";
    public const string SubmitLastChanceEntry = "submitLastChanceEntry";
    public const string GetState = "getState";
    public const string Ping = "ping";
    public const string GetProfile = "getProfile";
    public const string GetTagCandidates = "getTagCandidates";
    public const string GrantTag = "grantTag";
    public const string SubmitFateDecision = "submitFateDecision";
}

public static class ServerMessageTypes
{
    public const string RoomAccess = "roomAccess";
    public const string GameState = "gameState";
    public const string ActionStatus = "actionStatus";
    public const string RoundResolved = "roundResolved";
    public const string QuestionStatus = "questionStatus";
    public const string QuestionResolved = "questionResolved";
    public const string LastChanceProgress = "lastChanceProgress";
    public const string LastChanceResolved = "lastChanceResolved";
    public const string Pong = "pong";
    public const string Error = "error";
    public const string Profile = "profile";
    public const string TagCandidates = "tagCandidates";
    public const string TagGranted = "tagGranted";
    public const string FateStatus = "fateStatus";
    public const string FateResolved = "fateResolved";
}

public static class ProtocolErrorCodes
{
    public const string InvalidMessage = "INVALID_MESSAGE";
    public const string UnknownCommand = "UNKNOWN_COMMAND";
    public const string NotAuthenticated = "NOT_AUTHENTICATED";
}

public sealed record ServerMessage(string Type, string? RequestId, object? Payload);

public sealed record ProtocolError(string Code);

internal sealed record CreateRoomCommand(string? DisplayName, string? DeviceToken);

internal sealed record JoinRoomCommand(
    string? RoomCode,
    string? DisplayName,
    string? DeviceToken);

internal sealed record ReconnectCommand(
    string? RoomCode,
    Guid PlayerId,
    string? ReconnectToken);

internal sealed record SubmitActionCommand(
    int ExpectedRound,
    BattleGame.Core.CombatAction Action);

internal sealed record SubmitAnswerCommand(
    string? QuestionId,
    int SelectedOptionIndex);

internal sealed record SubmitLastChanceEntryCommand(
    string? ChallengeId,
    string? Entry);

internal sealed record GetProfileCommand(string? DeviceToken);

internal sealed record GrantTagCommand(string? TagCode);

internal sealed record SubmitFateDecisionCommand(
    string? EventId,
    string? Argument);

internal sealed record ClientEnvelope(
    string? Type,
    string? RequestId,
    JsonElement Payload);

public static class OnlineProtocolJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    public static string Serialize(ServerMessage message)
    {
        return JsonSerializer.Serialize(message, Options);
    }

    private static JsonSerializerOptions CreateOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            PropertyNameCaseInsensitive = true
        };
        options.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, false));
        return options;
    }
}
