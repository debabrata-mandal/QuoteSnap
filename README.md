# QuoteSnap

Self-hosted C#/.NET microservice that fetches last traded prices (LTP) for hundreds of NSE/BSE stocks in one request via the Fyers API. Submit a batch, get a request ID, poll for results. PostgreSQL storage.

## How it works

1. `POST /api/quote-requests` with any number of symbols. The service validates them, stores the request in PostgreSQL, and returns `202 Accepted` with a request ID.
2. A background worker fetches quotes from Fyers in batches of 50, pausing 250 ms between batches to stay within Fyers' rate limits, and writes each batch as it arrives. 600 symbols take 12 calls, roughly 5 seconds.
3. `GET /api/quote-requests/{id}` returns the status, progress and results. While the request is still running, the response carries `Retry-After: 2`.

The service needs no Fyers settings of its own. The login tool sends the Fyers App ID, Secret ID and PIN together with each login, and the service stores them encrypted with the tokens. Every quote request then uses that stored session. Fyers access tokens expire daily; the service refreshes them automatically with the stored refresh token and PIN. About every 15 days the refresh token expires too, and you log in again with the login tool. Quote requests are rejected with a clear `503` until the first login and whenever a new one is due.

## Repository layout

| Path | What it is |
|---|---|
| `src/QuoteSnap.Api` | The microservice: endpoints, background worker, Fyers client (`Fyers/`), PostgreSQL via EF Core (`Data/`) |
| `src/QuoteSnap.Contracts` | Request and response types shared by the API and its clients |
| `src/QuoteSnap.Client` | .NET client for the API, including the Fyers login flow; meant for the login tool and a future Windows app |
| `src/QuoteSnap.LoginTool` | `quotesnap-login`, the console tool for the Fyers login |
| `tests/` | Unit tests and integration tests (real API, PostgreSQL in Docker, fake Fyers) |
| `docker-compose.yml` | PostgreSQL for local development |

## Running locally

### 1. Set the local secrets (once)

Secrets are kept out of the repository, in a `.env` file at the repository root (gitignored). docker compose, the API (in Development) and the login tool all read it.

```powershell
Copy-Item .env.example .env
```

Then fill in `.env`: a database password and two API keys (random strings of at least 16 characters). The API key goes in the `X-Api-Key` header of `/api` calls, and the admin key in the `X-Admin-Key` header of `/admin` calls; the login tool uses the admin key automatically.

PostgreSQL applies the password only when its volume is first created. To change it later, update `.env` and run `docker exec quotesnap-postgres psql -U quotesnap -c "ALTER USER quotesnap PASSWORD '<new password>'"`.

### 2. Start the database

```powershell
docker compose up -d
```

This starts PostgreSQL on `127.0.0.1:5432` (reachable from this machine only), with its data in the `quotesnap-postgres` Docker volume, and Adminer, a web UI for browsing it. Open [http://127.0.0.1:8081/?pgsql=postgres&username=quotesnap&db=quotesnap](http://127.0.0.1:8081/?pgsql=postgres&username=quotesnap&db=quotesnap) and enter your password; the link preselects PostgreSQL, which the login form otherwise defaults to MySQL. If something else on your machine uses port 5432, stop it or change both the port in `docker-compose.yml` and the connection string.

### 3. Start the API

Start it from Visual Studio, or:

```powershell
dotnet run --project src/QuoteSnap.Api
```

It listens on `http://127.0.0.1:8080`, connects to PostgreSQL (waiting up to a minute if the database is still starting), and creates or updates the tables automatically.

If a secret from step 1 is missing, the API refuses to start and says which one and how to set it.

### 4. Log in to Fyers

In the [Fyers API dashboard](https://myapi.fyers.in), create an app and set its **Redirect URI** to `http://127.0.0.1:8085/callback`. Note the **App ID** and **Secret ID**.

Optionally fill in `Fyers__AppId`, `Fyers__SecretId` and `Fyers__Pin` in `.env` so the login tool doesn't ask for them each time. Then log in:

```powershell
dotnet run --project src/QuoteSnap.LoginTool -- login
```

The tool asks for any Fyers detail you didn't set (the Secret ID and PIN are masked) and opens the Fyers login page in your browser. After you log in, Fyers redirects back to the tool, which sends the auth code and the app details to the service. The service exchanges the code for tokens and stores everything encrypted. If the local callback can't run, use `login --manual` and paste the redirect URL instead.

Check the session at any time:

```powershell
dotnet run --project src/QuoteSnap.LoginTool -- status
```

Repeat the login when the status shows `ReloginRequired`, roughly every 15 days.

## Using the API

All `/api` calls need the `X-Api-Key` header.

```powershell
$h = @{ 'X-Api-Key' = '<api key>' }

$r = Invoke-RestMethod http://127.0.0.1:8080/api/quote-requests -Method Post -Headers $h `
    -ContentType 'application/json' -Body '{ "symbols": ["NSE:SBIN-EQ", "NSE:RELIANCE-EQ", "NSE:NIFTY50-INDEX"] }'

Invoke-RestMethod "http://127.0.0.1:8080$($r.statusUrl)" -Headers $h
```

| Endpoint | Purpose |
|---|---|
| `POST /api/quote-requests` | Submit `{ "symbols": [...] }`; returns `requestId`, `statusUrl`, invalid symbols, and days until the next Fyers login is due |
| `GET /api/quote-requests/{id}` | Status (`Queued`, `Running`, `Completed`, `CompletedWithErrors`, `Failed`), counts and per-symbol results |
| `GET /api/quote-requests?limit=20&before=...` | Past requests, newest first; pass `nextBefore` from one page as `before` for the next |
| `GET /api/status` | Service version and Fyers session state |
| `GET /health` | Liveness check, no key needed |
| `GET /openapi/v1.json` | OpenAPI description |

Symbols use the Fyers format `EXCHANGE:SYMBOL-SERIES`, for example `NSE:SBIN-EQ`, `BSE:SENSEX-INDEX` or `NSE:NIFTY50-INDEX`. Results come back in the order you sent them. Requests are kept for 30 days (`QuoteSnap__RetentionDays`).

From .NET, use `QuoteSnap.Client`:

```csharp
services.AddQuoteSnapClient(o =>
{
    o.BaseAddress = new Uri("http://127.0.0.1:8080/");
    o.ApiKey = configuration["QuoteSnap:ApiKey"];
});

QuoteRequestDetails snapshot = await quoteSnapClient.FetchSnapshotAsync(symbols);
```

## Deploying with Docker

The `Dockerfile` builds the API image. It doesn't read `.env`; give it a PostgreSQL connection string and the two API keys as environment variables, for example from an env file kept on the server:

```text
ConnectionStrings__QuoteSnap=Host=<db host>;Port=5432;Database=quotesnap;Username=quotesnap;Password=<password>
Auth__ApiKey=<api key>
Auth__AdminApiKey=<admin key>
Fyers__ExpectedFyersId=<optional, e.g. XY12345>
```

```powershell
docker build -t quotesnap .
docker run -d --name quotesnap --restart unless-stopped `
    -p 127.0.0.1:8080:8080 --env-file quotesnap.env -v quotesnap-data:/data quotesnap
```

The `quotesnap-data` volume holds the encryption keys for the stored Fyers session. Keep it, and back it up with the database: without the keys the stored session can't be decrypted and you have to log in again.

## Development

```powershell
dotnet build
dotnet test
```

The integration tests start their own PostgreSQL container, so Docker must be running.

After changing the data model, add a migration:

```powershell
dotnet tool restore
dotnet ef migrations add <Name> --project src/QuoteSnap.Api --output-dir Data/Migrations
```

Migrations are applied automatically at startup.

## Notes

- **Fyers endpoints and error codes** are configured in `appsettings.json` (`Fyers__ApiBaseUrl`, `Fyers__DataBaseUrl`) and `Fyers/FyersExceptions.cs`. Check them against the current Fyers v3 API reference if Fyers changes its API.
- **Rate limits:** the batches of a request are spaced by `Fyers__DelayBetweenBatchesMs` (250 ms by default), and every call to Fyers, including logins and token refreshes, is also capped at 5 per second and 150 per minute, below Fyers' limits. Above about 7,500 symbols a request takes longer than a minute, because it waits for the per-minute cap. 429 responses are not retried, because repeatedly exceeding the per-minute limit can get the account blocked for the day.
- **Encryption at rest:** the Fyers Secret ID, PIN and tokens are encrypted in the database with ASP.NET Core Data Protection. The keys live in `QuoteSnap__DataDirectory/keys`, outside the database, so a database backup alone doesn't expose them. On Linux the key files themselves are unencrypted; to encrypt them, mount a PFX certificate and set `QuoteSnap__DataProtectionCertificatePath` and `QuoteSnap__DataProtectionCertificatePassword`.
- **Run one API instance.** The work queue is in memory, so a second instance would not pick up the first one's requests.
- **No secrets in the repository:** the database password and API keys live in `.env` locally and in environment variables in Docker; never in `appsettings*.json` or `docker-compose.yml`.
