using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using BattleGame.Core;
using BattleGame.Online;
using Microsoft.Data.Sqlite;

namespace BattleGame.Persistence;

/// <summary>
/// 使用 macOS 系统 SQLite 的玩家数据存储。每局房间码在 matches 中唯一，重复网络消息
/// 不会重复增加胜场；设备令牌只保存 SHA-256，不把可用于冒充身份的原文写入磁盘。
/// </summary>
public sealed class SqlitePlayerProfileService : IPlayerProfileService
{
    private static int _providerInitialized;
    private readonly string _connectionString;
    private readonly ITagCatalog _tagCatalog;
    private readonly TimeProvider _timeProvider;

    public SqlitePlayerProfileService(
        string databasePath,
        ITagCatalog tagCatalog,
        TimeProvider? timeProvider = null)
    {
        if (string.IsNullOrWhiteSpace(databasePath))
        {
            throw new ArgumentException(nameof(databasePath));
        }

        InitializeProvider();
        string fullPath = Path.GetFullPath(databasePath);
        string? directory = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(directory))
        {
            Directory.CreateDirectory(directory);
        }

        _connectionString = new SqliteConnectionStringBuilder
        {
            DataSource = fullPath,
            Mode = SqliteOpenMode.ReadWriteCreate,
            Pooling = true
        }.ToString();
        _tagCatalog = tagCatalog ?? throw new ArgumentNullException(nameof(tagCatalog));
        _timeProvider = timeProvider ?? TimeProvider.System;
    }

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            PRAGMA journal_mode = WAL;
            PRAGMA foreign_keys = ON;

            CREATE TABLE IF NOT EXISTS profiles (
                profile_id TEXT PRIMARY KEY,
                device_token_hash TEXT NOT NULL UNIQUE,
                display_name TEXT NOT NULL,
                games_played INTEGER NOT NULL DEFAULT 0,
                wins INTEGER NOT NULL DEFAULT 0,
                losses INTEGER NOT NULL DEFAULT 0,
                draws INTEGER NOT NULL DEFAULT 0,
                created_utc TEXT NOT NULL,
                updated_utc TEXT NOT NULL
            );

            CREATE TABLE IF NOT EXISTS matches (
                room_code TEXT PRIMARY KEY,
                host_profile_id TEXT NOT NULL,
                guest_profile_id TEXT NOT NULL,
                outcome INTEGER NOT NULL,
                ended_utc TEXT NOT NULL,
                FOREIGN KEY(host_profile_id) REFERENCES profiles(profile_id),
                FOREIGN KEY(guest_profile_id) REFERENCES profiles(profile_id)
            );

            CREATE TABLE IF NOT EXISTS player_tags (
                tag_id INTEGER PRIMARY KEY AUTOINCREMENT,
                owner_profile_id TEXT NOT NULL,
                granted_by_profile_id TEXT NOT NULL,
                source_room_code TEXT NOT NULL UNIQUE,
                tag_code TEXT NOT NULL,
                awarded_utc TEXT NOT NULL,
                expires_utc TEXT NULL,
                FOREIGN KEY(owner_profile_id) REFERENCES profiles(profile_id),
                FOREIGN KEY(granted_by_profile_id) REFERENCES profiles(profile_id),
                FOREIGN KEY(source_room_code) REFERENCES matches(room_code)
            );
            """;
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    public async Task<PlayerProfile> GetOrCreateAsync(
        string deviceToken,
        string displayName,
        CancellationToken cancellationToken = default)
    {
        string tokenHash = HashDeviceToken(deviceToken);
        string normalizedName = NormalizeDisplayName(displayName);
        DateTimeOffset now = _timeProvider.GetUtcNow();

        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        await using (SqliteCommand command = connection.CreateCommand())
        {
            command.CommandText = """
                INSERT INTO profiles (
                    profile_id, device_token_hash, display_name, created_utc, updated_utc)
                VALUES ($profileId, $tokenHash, $displayName, $now, $now)
                ON CONFLICT(device_token_hash) DO UPDATE SET
                    display_name = excluded.display_name,
                    updated_utc = excluded.updated_utc;
                """;
            command.Parameters.AddWithValue("$profileId", Guid.NewGuid().ToString("D"));
            command.Parameters.AddWithValue("$tokenHash", tokenHash);
            command.Parameters.AddWithValue("$displayName", normalizedName);
            command.Parameters.AddWithValue("$now", Format(now));
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        return await ReadByTokenHashAsync(connection, tokenHash, cancellationToken);
    }

    public async Task<PlayerProfile> GetByDeviceTokenAsync(
        string deviceToken,
        CancellationToken cancellationToken = default)
    {
        string tokenHash = HashDeviceToken(deviceToken);
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        return await ReadByTokenHashAsync(connection, tokenHash, cancellationToken);
    }

    public async Task RecordMatchAsync(
        string roomCode,
        Guid hostProfileId,
        Guid guestProfileId,
        BattleOutcome outcome,
        CancellationToken cancellationToken = default)
    {
        if (outcome == BattleOutcome.Ongoing)
        {
            throw new ArgumentOutOfRangeException(nameof(outcome));
        }

        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        await using SqliteTransaction transaction = (SqliteTransaction)
            await connection.BeginTransactionAsync(cancellationToken);
        int inserted;
        await using (SqliteCommand insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT OR IGNORE INTO matches (
                    room_code, host_profile_id, guest_profile_id, outcome, ended_utc)
                VALUES ($roomCode, $hostId, $guestId, $outcome, $endedUtc);
                """;
            insert.Parameters.AddWithValue("$roomCode", NormalizeRoomCode(roomCode));
            insert.Parameters.AddWithValue("$hostId", hostProfileId.ToString("D"));
            insert.Parameters.AddWithValue("$guestId", guestProfileId.ToString("D"));
            insert.Parameters.AddWithValue("$outcome", (int)outcome);
            insert.Parameters.AddWithValue("$endedUtc", Format(_timeProvider.GetUtcNow()));
            inserted = await insert.ExecuteNonQueryAsync(cancellationToken);
        }

        if (inserted == 1)
        {
            await UpdateMatchStatisticsAsync(
                connection,
                transaction,
                hostProfileId,
                guestProfileId,
                outcome,
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    public async Task<IReadOnlyList<TagDefinition>> GetTagCandidatesAsync(
        string roomCode,
        Guid requesterProfileId,
        CancellationToken cancellationToken = default)
    {
        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        MatchParticipants match = await ReadMatchAsync(connection, roomCode, cancellationToken);
        Guid winnerId = GetWinner(match);
        if (winnerId != requesterProfileId)
        {
            throw new ProfileException(ProfileErrorCode.NotMatchWinner);
        }

        if (await HasTagForMatchAsync(connection, roomCode, cancellationToken))
        {
            throw new ProfileException(ProfileErrorCode.TagAlreadyGranted);
        }

        // MVP 固定给三个审核候选，避免长列表拖慢战后流程；后续可用战斗统计替换排序策略。
        return _tagCatalog.GetAll().Take(3).ToArray();
    }

    public async Task<PlayerProfile> GrantTagAsync(
        string roomCode,
        Guid granterProfileId,
        string tagCode,
        CancellationToken cancellationToken = default)
    {
        TagDefinition definition = _tagCatalog.GetAll().Take(3).SingleOrDefault(
            tag => string.Equals(tag.Code, tagCode, StringComparison.Ordinal))
            ?? throw new ProfileException(ProfileErrorCode.TagNotAllowed);
        DateTimeOffset now = _timeProvider.GetUtcNow();

        await using SqliteConnection connection = await OpenAsync(cancellationToken);
        await using SqliteTransaction transaction = (SqliteTransaction)
            await connection.BeginTransactionAsync(cancellationToken);
        MatchParticipants match = await ReadMatchAsync(
            connection,
            roomCode,
            cancellationToken,
            transaction);
        Guid winnerId = GetWinner(match);
        if (winnerId != granterProfileId)
        {
            throw new ProfileException(ProfileErrorCode.NotMatchWinner);
        }

        Guid loserId = winnerId == match.HostProfileId
            ? match.GuestProfileId
            : match.HostProfileId;
        await using (SqliteCommand insert = connection.CreateCommand())
        {
            insert.Transaction = transaction;
            insert.CommandText = """
                INSERT INTO player_tags (
                    owner_profile_id, granted_by_profile_id, source_room_code,
                    tag_code, awarded_utc, expires_utc)
                VALUES ($ownerId, $granterId, $roomCode, $tagCode, $awardedUtc, $expiresUtc);
                """;
            insert.Parameters.AddWithValue("$ownerId", loserId.ToString("D"));
            insert.Parameters.AddWithValue("$granterId", granterProfileId.ToString("D"));
            insert.Parameters.AddWithValue("$roomCode", NormalizeRoomCode(roomCode));
            insert.Parameters.AddWithValue("$tagCode", definition.Code);
            insert.Parameters.AddWithValue("$awardedUtc", Format(now));
            insert.Parameters.AddWithValue(
                "$expiresUtc",
                definition.Lifetime is TimeSpan lifetime
                    ? Format(now.Add(lifetime))
                    : DBNull.Value);
            try
            {
                await insert.ExecuteNonQueryAsync(cancellationToken);
            }
            catch (SqliteException exception) when (exception.SqliteErrorCode == 19)
            {
                throw new ProfileException(ProfileErrorCode.TagAlreadyGranted);
            }
        }

        await transaction.CommitAsync(cancellationToken);
        return await ReadByProfileIdAsync(connection, loserId, cancellationToken);
    }

    private async Task<PlayerProfile> ReadByTokenHashAsync(
        SqliteConnection connection,
        string tokenHash,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT profile_id, display_name, games_played, wins, losses, draws
            FROM profiles
            WHERE device_token_hash = $tokenHash;
            """;
        command.Parameters.AddWithValue("$tokenHash", tokenHash);
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new ProfileException(ProfileErrorCode.ProfileNotFound);
        }

        return await CreateProfileAsync(connection, reader, cancellationToken);
    }

    private async Task<PlayerProfile> ReadByProfileIdAsync(
        SqliteConnection connection,
        Guid profileId,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT profile_id, display_name, games_played, wins, losses, draws
            FROM profiles
            WHERE profile_id = $profileId;
            """;
        command.Parameters.AddWithValue("$profileId", profileId.ToString("D"));
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new ProfileException(ProfileErrorCode.ProfileNotFound);
        }

        return await CreateProfileAsync(connection, reader, cancellationToken);
    }

    private async Task<PlayerProfile> CreateProfileAsync(
        SqliteConnection connection,
        SqliteDataReader reader,
        CancellationToken cancellationToken)
    {
        Guid profileId = Guid.Parse(reader.GetString(0));
        string displayName = reader.GetString(1);
        int gamesPlayed = reader.GetInt32(2);
        int wins = reader.GetInt32(3);
        int losses = reader.GetInt32(4);
        int draws = reader.GetInt32(5);
        await reader.DisposeAsync();
        IReadOnlyList<PlayerTag> tags = await ReadTagsAsync(
            connection,
            profileId,
            cancellationToken);
        return new PlayerProfile(
            profileId,
            displayName,
            gamesPlayed,
            wins,
            losses,
            draws,
            tags);
    }

    private async Task<IReadOnlyList<PlayerTag>> ReadTagsAsync(
        SqliteConnection connection,
        Guid profileId,
        CancellationToken cancellationToken)
    {
        Dictionary<string, TagDefinition> definitions = _tagCatalog.GetAll()
            .ToDictionary(tag => tag.Code, StringComparer.Ordinal);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT tag_code, granted_by_profile_id, source_room_code, awarded_utc, expires_utc
            FROM player_tags
            WHERE owner_profile_id = $profileId
              AND (expires_utc IS NULL OR expires_utc > $now)
            ORDER BY awarded_utc DESC;
            """;
        command.Parameters.AddWithValue("$profileId", profileId.ToString("D"));
        command.Parameters.AddWithValue("$now", Format(_timeProvider.GetUtcNow()));
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        var tags = new List<PlayerTag>();
        while (await reader.ReadAsync(cancellationToken))
        {
            string code = reader.GetString(0);
            string displayName = definitions.TryGetValue(code, out TagDefinition? definition)
                ? definition.DisplayName
                : code;
            tags.Add(new PlayerTag(
                code,
                displayName,
                Guid.Parse(reader.GetString(1)),
                reader.GetString(2),
                Parse(reader.GetString(3)),
                reader.IsDBNull(4) ? null : Parse(reader.GetString(4))));
        }

        return tags;
    }

    private static async Task UpdateMatchStatisticsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid hostProfileId,
        Guid guestProfileId,
        BattleOutcome outcome,
        CancellationToken cancellationToken)
    {
        (int hostWin, int hostLoss, int hostDraw) = outcome switch
        {
            BattleOutcome.HumanWin => (1, 0, 0),
            BattleOutcome.ComputerWin => (0, 1, 0),
            BattleOutcome.Draw => (0, 0, 1),
            _ => throw new ArgumentOutOfRangeException(nameof(outcome))
        };
        await UpdateProfileStatisticsAsync(
            connection,
            transaction,
            hostProfileId,
            hostWin,
            hostLoss,
            hostDraw,
            cancellationToken);
        await UpdateProfileStatisticsAsync(
            connection,
            transaction,
            guestProfileId,
            hostLoss,
            hostWin,
            hostDraw,
            cancellationToken);
    }

    private static async Task UpdateProfileStatisticsAsync(
        SqliteConnection connection,
        SqliteTransaction transaction,
        Guid profileId,
        int wins,
        int losses,
        int draws,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            UPDATE profiles
            SET games_played = games_played + 1,
                wins = wins + $wins,
                losses = losses + $losses,
                draws = draws + $draws
            WHERE profile_id = $profileId;
            """;
        command.Parameters.AddWithValue("$wins", wins);
        command.Parameters.AddWithValue("$losses", losses);
        command.Parameters.AddWithValue("$draws", draws);
        command.Parameters.AddWithValue("$profileId", profileId.ToString("D"));
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static async Task<MatchParticipants> ReadMatchAsync(
        SqliteConnection connection,
        string roomCode,
        CancellationToken cancellationToken,
        SqliteTransaction? transaction = null)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.Transaction = transaction;
        command.CommandText = """
            SELECT host_profile_id, guest_profile_id, outcome
            FROM matches
            WHERE room_code = $roomCode;
            """;
        command.Parameters.AddWithValue("$roomCode", NormalizeRoomCode(roomCode));
        await using SqliteDataReader reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            throw new ProfileException(ProfileErrorCode.MatchNotFound);
        }

        return new MatchParticipants(
            Guid.Parse(reader.GetString(0)),
            Guid.Parse(reader.GetString(1)),
            (BattleOutcome)reader.GetInt32(2));
    }

    private static async Task<bool> HasTagForMatchAsync(
        SqliteConnection connection,
        string roomCode,
        CancellationToken cancellationToken)
    {
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = """
            SELECT EXISTS(
                SELECT 1 FROM player_tags WHERE source_room_code = $roomCode);
            """;
        command.Parameters.AddWithValue("$roomCode", NormalizeRoomCode(roomCode));
        return Convert.ToInt32(await command.ExecuteScalarAsync(cancellationToken), CultureInfo.InvariantCulture) == 1;
    }

    private static Guid GetWinner(MatchParticipants match)
    {
        return match.Outcome switch
        {
            BattleOutcome.HumanWin => match.HostProfileId,
            BattleOutcome.ComputerWin => match.GuestProfileId,
            _ => throw new ProfileException(ProfileErrorCode.NotMatchWinner)
        };
    }

    private async Task<SqliteConnection> OpenAsync(CancellationToken cancellationToken)
    {
        var connection = new SqliteConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using SqliteCommand command = connection.CreateCommand();
        command.CommandText = "PRAGMA foreign_keys = ON;";
        await command.ExecuteNonQueryAsync(cancellationToken);
        return connection;
    }

    private static void InitializeProvider()
    {
        if (Interlocked.Exchange(ref _providerInitialized, 1) == 0)
        {
            SQLitePCL.raw.SetProvider(new SQLitePCL.SQLite3Provider_sqlite3());
        }
    }

    private static string HashDeviceToken(string deviceToken)
    {
        string normalized = deviceToken?.Trim() ?? string.Empty;
        if (normalized.Length < 24)
        {
            throw new ProfileException(ProfileErrorCode.InvalidDeviceToken);
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(normalized)));
    }

    private static string NormalizeDisplayName(string displayName)
    {
        string normalized = displayName?.Trim() ?? string.Empty;
        if (normalized.Length is < 1 or > 20)
        {
            throw new ArgumentException(nameof(displayName));
        }

        return normalized;
    }

    private static string NormalizeRoomCode(string roomCode)
    {
        string normalized = roomCode?.Trim().ToUpperInvariant() ?? string.Empty;
        if (normalized.Length != 6)
        {
            throw new ArgumentException(nameof(roomCode));
        }

        return normalized;
    }

    private static string Format(DateTimeOffset value)
        => value.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture);

    private static DateTimeOffset Parse(string value)
        => DateTimeOffset.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind);

    private sealed record MatchParticipants(
        Guid HostProfileId,
        Guid GuestProfileId,
        BattleOutcome Outcome);
}
