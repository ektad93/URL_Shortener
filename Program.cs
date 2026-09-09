using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<UrlStore>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();
var store = app.Services.GetRequiredService<UrlStore>();
store.Initialize();

app.UseSwagger();
app.UseSwaggerUI();

app.MapGet("/health", () => Results.Ok(new { status = "ok" }));

app.MapPost("/api/v1/urls", (CreateUrlRequest request, UrlStore urls, IConfiguration config) =>
{
    if (!Uri.TryCreate(request.Url, UriKind.Absolute, out var destination) ||
        (destination.Scheme != Uri.UriSchemeHttp && destination.Scheme != Uri.UriSchemeHttps))
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["url"] = ["A valid HTTP or HTTPS URL is required."]
        });
    }

    if (request.ExpiresAt is not null && request.ExpiresAt <= DateTimeOffset.UtcNow)
    {
        return Results.ValidationProblem(new Dictionary<string, string[]>
        {
            ["expiresAt"] = ["expiresAt must be in the future."]
        });
    }

    var created = urls.Create(destination.ToString(), request.ExpiresAt);
    var baseUrl = config["BASE_URL"] ?? $"{app.Urls.FirstOrDefault() ?? "http://localhost:5000"}";
    return Results.Created($"/api/v1/urls/{created.ShortCode}", new
    {
        shortCode = created.ShortCode,
        shortUrl = $"{baseUrl.TrimEnd('/')}/{created.ShortCode}",
        originalUrl = created.OriginalUrl,
        createdAt = created.CreatedAt,
        expiresAt = created.ExpiresAt
    });
});

app.MapGet("/{shortCode}", (string shortCode, HttpRequest request, UrlStore urls) =>
{
    if (shortCode.Length is < 4 or > 32 || !shortCode.All(char.IsLetterOrDigit))
        return Results.NotFound(new { detail = "Short URL not found" });

    var url = urls.Find(shortCode);
    if (url is null) return Results.NotFound(new { detail = "Short URL not found" });
    if (url.Status != "active") return Results.StatusCode(StatusCodes.Status410Gone);
    if (url.ExpiresAt is not null && url.ExpiresAt <= DateTimeOffset.UtcNow)
        return Results.Problem("Short URL has expired", statusCode: StatusCodes.Status410Gone);

    urls.RecordClick(url.Id, request.Headers.UserAgent.ToString(), request.Headers.Referer.ToString());
    return Results.Redirect(url.OriginalUrl, permanent: false, preserveMethod: false);
});

app.MapGet("/api/v1/urls/{shortCode}/analytics", (string shortCode, UrlStore urls) =>
{
    var url = urls.Find(shortCode);
    if (url is null) return Results.NotFound(new { detail = "Short URL not found" });
    if (url.ExpiresAt is not null && url.ExpiresAt <= DateTimeOffset.UtcNow)
        return Results.Problem("Short URL has expired", statusCode: StatusCodes.Status410Gone);
    return Results.Ok(url);
});

app.Run();

public sealed record CreateUrlRequest(string Url, DateTimeOffset? ExpiresAt);
public sealed record ShortUrl(long Id, string ShortCode, string OriginalUrl, DateTimeOffset CreatedAt,
    DateTimeOffset? ExpiresAt, string Status, long ClickCount, DateTimeOffset? LastAccessedAt);

public sealed class UrlStore(IConfiguration configuration)
{
    private readonly string _connectionString = configuration.GetConnectionString("Default")
        ?? "Data Source=url_shortener.db";

    public void Initialize()
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = """
            CREATE TABLE IF NOT EXISTS short_urls (
              id INTEGER PRIMARY KEY AUTOINCREMENT,
              short_code TEXT NOT NULL UNIQUE,
              original_url TEXT NOT NULL,
              created_at TEXT NOT NULL,
              expires_at TEXT NULL,
              status TEXT NOT NULL,
              click_count INTEGER NOT NULL DEFAULT 0,
              last_accessed_at TEXT NULL
            );
            CREATE INDEX IF NOT EXISTS ix_short_urls_code ON short_urls(short_code);
            """;
        command.ExecuteNonQuery();
    }

    public ShortUrl Create(string originalUrl, DateTimeOffset? expiresAt)
    {
        for (var attempt = 0; attempt < 5; attempt++)
        {
            var code = RandomNumberGenerator.GetString("abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789", 7);
            try
            {
                using var connection = Open();
                using var command = connection.CreateCommand();
                command.CommandText = """
                    INSERT INTO short_urls(short_code, original_url, created_at, expires_at, status)
                    VALUES ($code, $url, $created, $expires, 'active');
                    SELECT id, short_code, original_url, created_at, expires_at, status, click_count, last_accessed_at
                    FROM short_urls WHERE id = last_insert_rowid();
                    """;
                command.Parameters.AddWithValue("$code", code);
                command.Parameters.AddWithValue("$url", originalUrl);
                command.Parameters.AddWithValue("$created", DateTimeOffset.UtcNow.ToString("O"));
                command.Parameters.AddWithValue("$expires", (object?)expiresAt?.ToString("O") ?? DBNull.Value);
                using var reader = command.ExecuteReader();
                reader.Read();
                return Read(reader);
            }
            catch (SqliteException) when (attempt < 4) { }
        }
        throw new InvalidOperationException("Unable to allocate a unique short code.");
    }

    public ShortUrl? Find(string code)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "SELECT id, short_code, original_url, created_at, expires_at, status, click_count, last_accessed_at FROM short_urls WHERE short_code = $code";
        command.Parameters.AddWithValue("$code", code);
        using var reader = command.ExecuteReader();
        return reader.Read() ? Read(reader) : null;
    }

    public void RecordClick(long id, string? userAgent, string? referer)
    {
        using var connection = Open();
        using var command = connection.CreateCommand();
        command.CommandText = "UPDATE short_urls SET click_count = click_count + 1, last_accessed_at = $accessed WHERE id = $id";
        command.Parameters.AddWithValue("$id", id);
        command.Parameters.AddWithValue("$accessed", DateTimeOffset.UtcNow.ToString("O"));
        command.ExecuteNonQuery();
    }

    private SqliteConnection Open()
    {
        var connection = new SqliteConnection(_connectionString);
        connection.Open();
        return connection;
    }

    private static ShortUrl Read(SqliteDataReader reader) => new(
        reader.GetInt64(0), reader.GetString(1), reader.GetString(2),
        DateTimeOffset.Parse(reader.GetString(3)),
        reader.IsDBNull(4) ? null : DateTimeOffset.Parse(reader.GetString(4)),
        reader.GetString(5), reader.GetInt64(6),
        reader.IsDBNull(7) ? null : DateTimeOffset.Parse(reader.GetString(7)));
}

public partial class Program;
