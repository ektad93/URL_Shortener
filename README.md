# URL Shortener

ASP.NET Core 8 URL shortener with SQLite persistence, expiration-aware redirects,
analytics, OpenAPI, and secure random short-code generation.

## Run

```powershell
dotnet restore
dotnet run
```

Swagger UI is available at `/swagger`.

## API

Create a URL:

```powershell
curl -X POST http://localhost:5000/api/v1/urls `
  -H "Content-Type: application/json" `
  -d '{\"url\":\"https://example.com/path\",\"expiresAt\":\"2030-01-01T00:00:00Z\"}'
```

`GET /{shortCode}` returns a temporary redirect. `GET
/api/v1/urls/{shortCode}/analytics` returns click count and last access time.
`GET /health` verifies the service is available.

## Design

The minimal API layer handles HTTP contracts, `UrlStore` owns SQLite persistence,
and lifecycle validation occurs before redirecting. Short codes use a unique
database constraint and cryptographically secure random generation. SQLite is
appropriate for this prototype; production should use PostgreSQL, caching,
durable asynchronous analytics, rate limiting, authentication, and monitoring.

Only HTTP and HTTPS destinations are accepted. The service never fetches
destination URLs server-side, avoiding SSRF during normal operation. Request
metadata is bounded and not persisted in this prototype.

## Validation

```powershell
dotnet build
dotnet test
```

Known limitations include no accounts, custom aliases, deletion workflow, or
distributed rate limiting.
