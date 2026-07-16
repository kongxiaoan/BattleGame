using BattleGame.Core;

namespace BattleGame.Online;

public sealed record TagDefinition(
    string Code,
    string DisplayName,
    TimeSpan? Lifetime);

public sealed record PlayerTag(
    string Code,
    string DisplayName,
    Guid GrantedByProfileId,
    string SourceRoomCode,
    DateTimeOffset AwardedAt,
    DateTimeOffset? ExpiresAt);

public sealed record PlayerProfile(
    Guid ProfileId,
    string DisplayName,
    int GamesPlayed,
    int Wins,
    int Losses,
    int Draws,
    IReadOnlyList<PlayerTag> Tags);

public enum ProfileErrorCode
{
    ProfileNotFound = 1,
    MatchNotFound = 2,
    NotMatchWinner = 3,
    TagNotAllowed = 4,
    TagAlreadyGranted = 5,
    InvalidDeviceToken = 6
}

public sealed class ProfileException : Exception
{
    public ProfileException(ProfileErrorCode code)
        : base(code.ToString())
    {
        Code = code;
    }

    public ProfileErrorCode Code { get; }
}

public interface ITagCatalog
{
    IReadOnlyList<TagDefinition> GetAll();
}

public interface IPlayerProfileService
{
    Task InitializeAsync(CancellationToken cancellationToken = default);

    Task<PlayerProfile> GetOrCreateAsync(
        string deviceToken,
        string displayName,
        CancellationToken cancellationToken = default);

    Task<PlayerProfile> GetByDeviceTokenAsync(
        string deviceToken,
        CancellationToken cancellationToken = default);

    Task RecordMatchAsync(
        string roomCode,
        Guid hostProfileId,
        Guid guestProfileId,
        BattleOutcome outcome,
        CancellationToken cancellationToken = default);

    Task<IReadOnlyList<TagDefinition>> GetTagCandidatesAsync(
        string roomCode,
        Guid requesterProfileId,
        CancellationToken cancellationToken = default);

    Task<PlayerProfile> GrantTagAsync(
        string roomCode,
        Guid granterProfileId,
        string tagCode,
        CancellationToken cancellationToken = default);
}
