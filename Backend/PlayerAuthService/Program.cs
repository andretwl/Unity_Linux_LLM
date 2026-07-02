using System.Security.Cryptography;
using Microsoft.AspNetCore.Mvc;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.UseUrls(builder.Configuration.GetValue<string>("Urls") ?? "http://localhost:5100");
builder.Services.AddSingleton(_ =>
{
    string connectionString = builder.Configuration.GetConnectionString("AuthDatabase")
        ?? throw new InvalidOperationException("Missing connection string 'AuthDatabase'.");
    return NpgsqlDataSource.Create(connectionString);
});

var app = builder.Build();

int sessionHours = Math.Max(1, builder.Configuration.GetValue("Auth:SessionHours", 12));
int rememberedSessionDays = Math.Max(1, builder.Configuration.GetValue("Auth:RememberedSessionDays", 30));
int passwordIterations = Math.Max(10000, builder.Configuration.GetValue("Auth:PasswordIterations", 100000));

await EnsureDatabaseAsync(app.Services, app.Environment.ContentRootPath);

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/api/auth/register", async (
    [FromBody] RegisterRequest request,
    NpgsqlDataSource dataSource) =>
{
    string? validationError = ValidateCredentials(request.Username, request.Password);
    if (validationError != null)
        return Results.BadRequest(new ErrorResponse(validationError));

    string username = request.Username.Trim();
    string normalizedUsername = NormalizeUsername(username);
    byte[] salt = RandomNumberGenerator.GetBytes(16);
    byte[] passwordHash = HashPassword(request.Password, salt, passwordIterations);

    await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync();
    await using NpgsqlCommand command = new(@"
insert into players (username, username_normalized, password_hash, password_salt, password_iterations)
values (@username, @username_normalized, @password_hash, @password_salt, @password_iterations)
returning player_id, username, created_at_utc;", connection);

    command.Parameters.AddWithValue("username", username);
    command.Parameters.AddWithValue("username_normalized", normalizedUsername);
    command.Parameters.AddWithValue("password_hash", passwordHash);
    command.Parameters.AddWithValue("password_salt", salt);
    command.Parameters.AddWithValue("password_iterations", passwordIterations);

    try
    {
        await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
        await reader.ReadAsync();

        return Results.Created($"/api/auth/players/{reader.GetGuid(0)}", new RegisterResponse(
            reader.GetGuid(0).ToString(),
            reader.GetString(1),
            ToIsoUtc(reader.GetFieldValue<DateTime>(2))));
    }
    catch (PostgresException ex) when (ex.SqlState == PostgresErrorCodes.UniqueViolation)
    {
        return Results.Conflict(new ErrorResponse("Username is already taken."));
    }
});

app.MapPost("/api/auth/login", async (
    [FromBody] LoginRequest request,
    NpgsqlDataSource dataSource) =>
{
    string? validationError = ValidateCredentials(request.Username, request.Password);
    if (validationError != null)
        return Results.BadRequest(new ErrorResponse(validationError));

    string normalizedUsername = NormalizeUsername(request.Username);

    await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync();
    await using NpgsqlCommand playerCommand = new(@"
select player_id, username, password_hash, password_salt, password_iterations
from players
where username_normalized = @username_normalized
limit 1;", connection);
    playerCommand.Parameters.AddWithValue("username_normalized", normalizedUsername);

    await using NpgsqlDataReader playerReader = await playerCommand.ExecuteReaderAsync();
    if (!await playerReader.ReadAsync())
        return Results.Unauthorized();

    string playerId = playerReader.GetGuid(0).ToString();
    string username = playerReader.GetString(1);
    byte[] storedHash = playerReader.GetFieldValue<byte[]>(2);
    byte[] storedSalt = playerReader.GetFieldValue<byte[]>(3);
    int iterations = playerReader.GetInt32(4);

    await playerReader.DisposeAsync();

    if (!VerifyPassword(request.Password, storedSalt, storedHash, iterations))
        return Results.Unauthorized();

    string sessionToken = CreateSessionToken();
    string tokenHash = HashToken(sessionToken);
    DateTime expiresAtUtc = DateTime.UtcNow.Add(request.RememberMe ? TimeSpan.FromDays(rememberedSessionDays) : TimeSpan.FromHours(sessionHours));

    await using NpgsqlCommand sessionCommand = new(@"
insert into player_sessions (player_id, session_token_hash, device_id, remember_me, expires_at_utc)
values (@player_id, @session_token_hash, @device_id, @remember_me, @expires_at_utc)
returning session_id, created_at_utc, expires_at_utc, last_seen_at_utc;", connection);

    sessionCommand.Parameters.AddWithValue("player_id", Guid.Parse(playerId));
    sessionCommand.Parameters.AddWithValue("session_token_hash", tokenHash);
    sessionCommand.Parameters.AddWithValue("device_id", (object?)request.DeviceId?.Trim() ?? DBNull.Value);
    sessionCommand.Parameters.AddWithValue("remember_me", request.RememberMe);
    sessionCommand.Parameters.AddWithValue("expires_at_utc", expiresAtUtc);

    await using NpgsqlDataReader sessionReader = await sessionCommand.ExecuteReaderAsync();
    await sessionReader.ReadAsync();

    return Results.Ok(new SessionResponse(
        sessionReader.GetGuid(0).ToString(),
        playerId,
        username,
        sessionToken,
        ToIsoUtc(sessionReader.GetFieldValue<DateTime>(1)),
        ToIsoUtc(sessionReader.GetFieldValue<DateTime>(2)),
        ToIsoUtc(sessionReader.GetFieldValue<DateTime>(3))));
});

app.MapGet("/api/auth/session", async (HttpRequest httpRequest, NpgsqlDataSource dataSource) =>
{
    string? bearerToken = GetBearerToken(httpRequest);
    if (string.IsNullOrWhiteSpace(bearerToken))
        return Results.Unauthorized();

    await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync();
    await using NpgsqlCommand command = new(@"
with updated_session as (
    update player_sessions
    set last_seen_at_utc = timezone('utc', now())
    where session_token_hash = @session_token_hash
      and revoked_at_utc is null
      and expires_at_utc > timezone('utc', now())
    returning session_id, player_id, created_at_utc, expires_at_utc, last_seen_at_utc
)
select updated_session.session_id,
       updated_session.player_id,
       players.username,
       updated_session.created_at_utc,
       updated_session.expires_at_utc,
       updated_session.last_seen_at_utc
from updated_session
join players on players.player_id = updated_session.player_id;", connection);

    command.Parameters.AddWithValue("session_token_hash", HashToken(bearerToken));

    await using NpgsqlDataReader reader = await command.ExecuteReaderAsync();
    if (!await reader.ReadAsync())
        return Results.Unauthorized();

    return Results.Ok(new SessionResponse(
        reader.GetGuid(0).ToString(),
        reader.GetGuid(1).ToString(),
        reader.GetString(2),
        string.Empty,
        ToIsoUtc(reader.GetFieldValue<DateTime>(3)),
        ToIsoUtc(reader.GetFieldValue<DateTime>(4)),
        ToIsoUtc(reader.GetFieldValue<DateTime>(5))));
});

app.MapPost("/api/auth/logout", async (HttpRequest httpRequest, NpgsqlDataSource dataSource) =>
{
    string? bearerToken = GetBearerToken(httpRequest);
    if (string.IsNullOrWhiteSpace(bearerToken))
        return Results.Unauthorized();

    await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync();
    await using NpgsqlCommand command = new(@"
update player_sessions
set revoked_at_utc = timezone('utc', now())
where session_token_hash = @session_token_hash
  and revoked_at_utc is null;", connection);
    command.Parameters.AddWithValue("session_token_hash", HashToken(bearerToken));

    await command.ExecuteNonQueryAsync();
    return Results.Ok(new { success = true });
});

await app.RunAsync();

static async Task EnsureDatabaseAsync(IServiceProvider services, string contentRootPath)
{
    NpgsqlDataSource dataSource = services.GetRequiredService<NpgsqlDataSource>();
    string sqlPath = Path.Combine(contentRootPath, "db", "init.sql");
    string sql = await File.ReadAllTextAsync(sqlPath);

    await using NpgsqlConnection connection = await dataSource.OpenConnectionAsync();
    await using NpgsqlCommand command = new(sql, connection);
    await command.ExecuteNonQueryAsync();
}

static string? ValidateCredentials(string? username, string? password)
{
    if (string.IsNullOrWhiteSpace(username))
        return "Username is required.";
    if (username.Trim().Length < 3)
        return "Username must be at least 3 characters.";
    if (username.Trim().Length > 32)
        return "Username must be 32 characters or fewer.";
    if (!username.Trim().All(ch => char.IsLetterOrDigit(ch) || ch == '_'))
        return "Username may only contain letters, numbers, and underscores.";
    if (string.IsNullOrWhiteSpace(password))
        return "Password is required.";
    if (password.Length < 6)
        return "Password must be at least 6 characters.";
    return null;
}

static string NormalizeUsername(string username)
{
    return username.Trim().ToLowerInvariant();
}

static byte[] HashPassword(string password, byte[] salt, int iterations)
{
    return Rfc2898DeriveBytes.Pbkdf2(password, salt, iterations, HashAlgorithmName.SHA256, 32);
}

static bool VerifyPassword(string password, byte[] salt, byte[] storedHash, int iterations)
{
    byte[] candidateHash = HashPassword(password, salt, iterations);
    return CryptographicOperations.FixedTimeEquals(candidateHash, storedHash);
}

static string CreateSessionToken()
{
    return Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))
        .TrimEnd('=')
        .Replace('+', '-')
        .Replace('/', '_');
}

static string HashToken(string token)
{
    byte[] hash = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(token));
    return Convert.ToHexString(hash);
}

static string? GetBearerToken(HttpRequest request)
{
    string authorization = request.Headers.Authorization.ToString();
    const string bearerPrefix = "Bearer ";
    return authorization.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase)
        ? authorization[bearerPrefix.Length..].Trim()
        : null;
}

static string ToIsoUtc(DateTime timestamp)
{
    return timestamp.ToUniversalTime().ToString("O");
}

sealed record RegisterRequest(string Username, string Password);
sealed record LoginRequest(string Username, string Password, bool RememberMe, string? DeviceId);
sealed record RegisterResponse(string PlayerId, string Username, string CreatedAtUtc);
sealed record SessionResponse(
    string SessionId,
    string PlayerId,
    string Username,
    string SessionToken,
    string CreatedAtUtc,
    string ExpiresAtUtc,
    string LastSeenAtUtc);
sealed record ErrorResponse(string Error);
