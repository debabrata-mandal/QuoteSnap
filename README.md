# QuoteSnap

Self-hosted C#/.NET microservice that fetches last traded prices (LTP) for hundreds of NSE/BSE stocks in one request via the Fyers API. Submit a batch, get a request ID, poll for results. It also serves the latest mutual fund NAVs from AMFI's daily file, and the month-by-month portfolios of the funds you follow, scraped from ScanX. PostgreSQL storage.

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
| `src/QuoteSnap.Client` | .NET client for the API, including the Fyers login flow; used by the login tool and the desktop app |
| `src/QuoteSnap.LoginTool` | `quotesnap-login`, the console tool for the Fyers login |
| `src/QuoteSnap.Desktop` | Windows desktop app (WPF): status dashboard, quote requests, history, mutual fund NAVs, fund holdings and the Fyers login |
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

Or log in from the desktop app (step 5): its **Fyers login** page does the same login without a console.

Check the session at any time:

```powershell
dotnet run --project src/QuoteSnap.LoginTool -- status
```

Repeat the login when the status shows `ReloginRequired`, roughly every 15 days.

To log out, for example before handing the machine over or when switching Fyers accounts:

```powershell
dotnet run --project src/QuoteSnap.LoginTool -- logout
```

It asks first (`--yes` skips the question), then the service asks Fyers to revoke its access token (`POST /api/v3/logout`; this only affects QuoteSnap's app, not your Fyers web or mobile sessions) and forgets the stored login, including the app details and PIN. Quote requests are refused with "Fyers re-authentication required" until the next login. If Fyers can't be reached, the login is forgotten anyway, so nothing can use or renew the token, and the tool says so; the token then stops working by itself at its next daily expiry. The desktop app's **Fyers login** page has the same **Log out** button, and both call `DELETE /admin/fyers/session` with the admin key.

### 5. Open the desktop app (optional)

```powershell
dotnet run --project src/QuoteSnap.Desktop
```

The app talks to the API at `http://127.0.0.1:8080/` with `Auth__ApiKey` from `.env`, so it needs no setup. The pages in its sidebar:

- **Dashboard:** whether the API is reachable, the Fyers login state and when it expires, whether NAVs have been downloaded, and today's quote requests. It refreshes every 15 seconds; the title bar repeats the three states on every page. When the API is offline or a Fyers login is needed it says what to do, with Try again and Open Settings buttons. Click a recent request to open it in History.
- **New request:** paste symbols, or drop or load a `.txt` or `.csv` file, then fetch. Progress and results update live as the batches complete; invalid symbols are listed. Filter the results and export them to CSV.
- **History:** every past request, filtered by status; select one to see, filter and export its quotes.
- **Mutual funds:** your own list of funds with their latest NAVs. Search by any part of the name (word order, typos, "midcap" or "mid cap", "absl", "dividend" for IDCW all work) or by ISIN or scheme code, and add or remove funds. The list is stored by the API (`/api/my-funds`), so every app pointed at the same service shares it, and it can be exported to CSV. The page also shows when AMFI's NAV file was last downloaded and can download it again.
- **Holdings:** what the funds on your list own. Fetch holdings scrapes every listed fund's portfolio from ScanX and shows live progress: a progress bar, the fund being fetched and a status per fund (Waiting, Fetching, Updated, Not on ScanX, Failed). A fetch keeps running in the service if you leave the page or close the app, and the page picks it up again when reopened. Select a fund to see its holdings with weight, sector, shares and the change in weight since the month before; step back through the 12 stored months, filter, and export to CSV.
- **Fyers login:** the stored Fyers session (account, days left, when the token renews) and the login itself, without the login tool. Enter the App ID, Secret ID and PIN (prefilled from `Fyers__AppId`, `Fyers__SecretId` and `Fyers__Pin` in `.env` if set) and press **Log in with Fyers**. The Fyers page opens in your browser; after you log in, Fyers redirects to `http://127.0.0.1:8085/callback`, where the app receives it and hands the login to the service. If that port can't be used, **Paste the redirect URL instead** has you copy the address from the browser. Turn on **Remember these details on this PC** to keep them for next time in `%LOCALAPPDATA%\QuoteSnap\fyers-login.json`, with the Secret ID and PIN encrypted for your Windows account. **Log out** on the session card (after a confirmation) revokes the token at Fyers and makes the service forget the login, as `quotesnap-login logout` does; the details remembered on this PC stay, ready for the next login. Logging in and out needs the admin API key (`Auth__AdminApiKey`), which the app reads from `.env` or Settings. The dashboard's "Fyers login needed" alert links here.
- **Settings:** point the app at another API address, API key or admin API key. Saved settings go to `%LOCALAPPDATA%\QuoteSnap\desktop-settings.json`, with the keys encrypted for your Windows account; until you save, the app keeps reading `.env`.

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
| `GET /api/nav?isin=...` | Latest mutual fund NAVs by ISIN (see below) |
| `GET /api/nav/search?q=...&limit=20` | Find funds by a loosely typed name, ISIN or scheme code (see below) |
| `GET /api/nav/schemes?code=...` | Latest NAVs by AMFI scheme code, in the order asked |
| `GET /api/my-funds` | Your fund list, in the order added, each with its latest NAV |
| `PUT /api/my-funds/{schemeCode}` | Add a Direct Plan scheme to your list (`201`, or `200` if it's already there) |
| `DELETE /api/my-funds/{schemeCode}` | Remove a scheme from your list (`204`, also when it wasn't there) |
| `POST /api/holdings/fetches` | Start fetching the holdings of every listed fund from ScanX (`202`; `200` with the running fetch if one is already going) |
| `GET /api/holdings/fetches/{id}` | A fetch's status and per-fund progress; `GET /api/holdings/fetches/latest` for the most recent one |
| `GET /api/my-funds/{schemeCode}/holdings?month=yyyy-MM` | A listed fund's stored portfolio for a month (default: the latest) |
| `POST /api/nav/refresh` | Download AMFI's latest NAV file into the service |
| `GET /api/nav/status` | How many NAVs are stored, the latest NAV date, and when they were downloaded (`loadedAt` is null before the first refresh) |
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
await quoteSnapClient.RefreshNavsAsync();
NavLookup navs = await quoteSnapClient.GetNavsAsync(["INF846K01WO1", "INF846K01WP8"]);
NavSearchResult found = await quoteSnapClient.SearchNavsAsync("parag parikh flexi");
await quoteSnapClient.AddMyFundAsync(122639);
MyFundList mine = await quoteSnapClient.GetMyFundsAsync();
HoldingFetch started = await quoteSnapClient.StartHoldingFetchAsync();
HoldingFetch done = await quoteSnapClient.WaitForHoldingFetchAsync(started.Id, new Progress<HoldingFetch>(f => Console.WriteLine($"{f.FinishedFunds}/{f.TotalFunds}")));
FundHoldings portfolio = await quoteSnapClient.GetFundHoldingsAsync(122639);
```

### Mutual fund NAVs

`POST /api/nav/refresh` downloads AMFI's [NAVAll.txt](https://portal.amfiindia.com/spages/NAVAll.txt), the latest NAV of every scheme, and stores its Direct Plan schemes (about 6,800) in PostgreSQL, replacing the previous download. The file leaves the plan blank for some schemes, Motilal Oswal Midcap Fund among them, so the refresh also downloads AMFI's [scheme master](https://portal.amfiindia.com/DownloadSchemeData_Po.aspx?mf=0) (about 4 MB), whose NAV names say which of those are Direct Plans. If that download fails the refresh still succeeds without them; set `Nav__SchemeMasterUrl` to an empty value to skip it. It takes a few seconds and returns the scheme count and the latest NAV date. Nothing downloads on its own: call it whenever you want fresh NAVs, for example once before looking them up. Fund houses publish each day's NAVs by 23:00. A failed download returns `502` and leaves the stored NAVs untouched. None of this needs a Fyers login.

```powershell
Invoke-RestMethod http://127.0.0.1:8080/api/nav/refresh -Method Post -Headers $h
Invoke-RestMethod "http://127.0.0.1:8080/api/nav?isin=INF846K01WO1,INF846K01WP8" -Headers $h
```

- Pass one ISIN (`?isin=INF760K01FR2`) or several separated by commas (`?isin=INF760K01FR2,INF846K01WO1`); either the growth/payout or the reinvestment ISIN matches. Up to about 500 fit in one URL.
- Only Direct Plan schemes are stored, so a Regular Plan ISIN comes back in `notFound`.
- Each fund comes back with its scheme code, name, fund house, category, plan, option, `nav` and `navDate`, in the order you asked. ISINs AMFI doesn't list are returned in `notFound`, and `loadedAt` says when the file was downloaded.
- Check `navDate`: closed and matured schemes stay in the file with their last NAV, which can be years old. A few ISINs belong to more than one such scheme, and then all of them are returned.

Most people know a fund by name, not ISIN, so `GET /api/nav/search?q=...` takes whatever they type:

```powershell
Invoke-RestMethod "http://127.0.0.1:8080/api/nav/search?q=nippon power infra" -Headers $h
```

- Case, punctuation, word order and "&" versus "and" don't matter; words may be joined or split ("midcap", "mid cap", "nifty50") or cut short ("icici pru"), and small typos are forgiven ("prudentail", "tecnology").
- Fund house initials work ("absl", "sbi"), and option words narrow the results ("growth", "idcw", or "dividend", the old name for IDCW).
- An exact ISIN or scheme code returns just that scheme.
- Schemes with a current NAV come before matured or wound-up ones, and Growth options before IDCW ones. `q` needs at least 2 characters; `limit` is 1 to 50 (default 20).
- If no fund matches every word, the closest ones come back with `partial: true`, which helps with renamed schemes.
- Before the first refresh it returns `503`.

`GET /api/nav/schemes?code=122639,118763` returns the latest NAVs for scheme codes, in the order asked, with unknown codes in `notFound`.

Your fund list lives in the `MyFunds` table, keyed by scheme code:

```powershell
Invoke-RestMethod http://127.0.0.1:8080/api/my-funds/122639 -Method Put -Headers $h
Invoke-RestMethod http://127.0.0.1:8080/api/my-funds -Headers $h
Invoke-RestMethod http://127.0.0.1:8080/api/my-funds/122639 -Method Delete -Headers $h
```

- Only Direct Plan schemes in the current NAV file can be added (`404` otherwise, `503` before the first refresh).
- Each fund keeps the name, fund house, option and category it had when added. If AMFI later drops the scheme, it stays on the list with `latest: null`.
- NAV refreshes don't touch the list.

### Fund holdings

[ScanX](https://scanx.trade/insight/) publishes each fund's portfolio for the last 12 months. `POST /api/holdings/fetches` fetches it for every fund on your list and stores it in the `FundHoldings` table. It reads the JSON behind ScanX's pages, so no browser or login is needed.

```powershell
$f = Invoke-RestMethod http://127.0.0.1:8080/api/holdings/fetches -Method Post -Headers $h
Invoke-RestMethod "http://127.0.0.1:8080/api/holdings/fetches/$($f.id)" -Headers $h
Invoke-RestMethod "http://127.0.0.1:8080/api/my-funds/152712/holdings?month=2026-09" -Headers $h
```

- A background worker goes through the funds one at a time, pausing between ScanX calls (`Holdings__DelayBetweenRequestsMs`, default 750). A fund takes one or two seconds, so the fetch returns `202` straight away. Poll it for progress; while it runs, the response carries `Retry-After: 1`. Only one fetch runs at a time, and one cut short by a restart resumes when the service starts.
- Each fund ends `Ok` with its holding count and month, `NotFound`, or `Error` with the reason. The fetch ends `Completed`, `CompletedWithErrors`, or `Failed` when no fund succeeded.
- Funds are matched to ScanX schemes by name and fund house. Plan, option, punctuation and words like "Fund" are ignored, and small spelling differences are tolerated within the same fund house. ScanX covers equity, hybrid, index and most other funds but few debt, liquid or fixed-maturity ones; those come back `NotFound`.
- A successful fetch replaces all 12 months stored for the fund. A failed one leaves them as they were. Removing a fund from the list deletes its holdings.
- Each holding has its name, NSE symbol (for listed shares), ISIN, asset type (Equity, Debt, TREPS, Net Payables, ...), sector, weight in percent of the fund's assets and share count. The holdings endpoint also returns the stored months and each holding's weight in the month before (`previousPercent`), ordered by weight. `GET /api/my-funds` includes a `holdings` summary (month, count, fetch time) for funds that have them.
- ScanX's address is `Holdings__BaseUrl`; `Holdings__PageSize` (default 500) sets how many holdings each call returns.

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

The integration tests start their own PostgreSQL container, so Docker must be running. The desktop app and its tests target `net10.0-windows`, so the full solution builds on Windows only.

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
