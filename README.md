# QuoteSnap

Self-hosted C#/.NET microservice that fetches last traded prices (LTP) for hundreds of NSE/BSE stocks in one request via the Fyers API. Submit a batch, get a request ID, poll for results. It also serves the latest mutual fund NAVs from AMFI's daily file, and the month-by-month portfolios of the funds you follow. PostgreSQL storage.

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
| `k8s/` | Kubernetes manifests for the API and PostgreSQL (Docker Desktop's Kubernetes) |

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

- **Dashboard:** whether the API is reachable, the Fyers login state and when it expires, the NIFTY 50, SENSEX, NIFTY BANK, NIFTY MIDCAP 100 and INDIA VIX with their live value and day change, the live price feed (its state, how many symbols it streams out of Fyers' 100-symbol limit, and the last tick), whether NAVs have been downloaded, today's quote requests, your funds' estimated move today (the average of their NAV estimates, with the best and worst fund and their estimated NAVs), and the top 10 stocks across your funds by average weight (weighted by the value of your units once every fund has units), with how many funds hold each and their last live price and day change; above them, how many of all the stocks your funds hold are advancing, declining or unchanged today. It reads everything once when it connects to the API's event hub, then updates from what the API pushes: live prices move the indices, the top holdings, the advance/decline counts and the estimated move as they tick, and changed digits roll like an odometer. The API tile shows whether the hub is connected or reconnecting; the title bar repeats the three states on every page. When the API is offline or a Fyers login is needed it says what to do, with Try again and Open Settings buttons.
- **New request:** find stocks by company name, ticker or ISIN ("tata motors", "hdfcbank") and add their Fyers symbols to the list, or paste symbols, or drop or load a `.txt` or `.csv` file, then fetch. Enter in the search box adds the best match. The page shows how many Fyers symbols the service has and when they were downloaded; **Refresh symbols** downloads them again. Progress and results update live as the batches complete; invalid symbols are listed. Filter the results and export them to CSV.
- **History:** every past request, filtered by status; select one to see, filter and export its quotes.
- **Mutual funds:** your own list of funds with their latest NAVs. Search by any part of the name (word order, typos, "midcap" or "mid cap", "absl", "dividend" for IDCW all work) or by ISIN or scheme code, add or remove funds, and drag a row to move it (the order is saved; sorting by Est. % is just a view, and **Original order** returns to yours). The list is stored by the API (`/api/my-funds`), so every app pointed at the same service shares it, and it can be exported to CSV. The page also shows when AMFI's NAV file was last downloaded and can download it again. The red **Get live prices** button fetches prices for every holding's stock, and the Est. % and Est. NAV columns show each fund's estimated move today (see [Today's NAV estimate](#todays-nav-estimate)); hover them to see what the estimate is based on.
- **Holdings:** what the funds on your list own. Fetch holdings downloads every listed fund's portfolio and shows live progress: a progress bar, the fund being fetched and a status per fund (waiting, fetching, updated, not available, failed). A fetch keeps running in the service if you leave the page or close the app, and the page picks it up again when reopened. Select a fund to see its holdings with weight and sector; step back through the 12 stored months, filter, and export to CSV. The red **Get live prices** button collects every distinct stock (with a Fyers symbol) held by the listed funds in their latest month, sends them as one quote request, and shows the request ID, its progress and, once it finishes, each holding's LTP and day change in the table. The last request's prices are shown again when the page is reopened. Stocks on the live feed (the top 95) then update in place as they tick; the rest keep the request's prices.
- **Stocks:** every listed stock your funds hold (lines with a Fyers symbol), ranked by average weight like the dashboard's top holdings, with how many funds hold each, the last price and day change. Filter by name, symbol or sector; prices update in place as they stream, and the list reloads when holdings, funds or prices change. The dashboard's **See all stocks** button opens it.
- **Fyers login:** the stored Fyers session (account, days left, when the token renews) and the login itself, without the login tool. Enter the App ID, Secret ID and PIN (prefilled from `Fyers__AppId`, `Fyers__SecretId` and `Fyers__Pin` in `.env` if set) and press **Log in with Fyers**. The Fyers page opens in your browser; after you log in, Fyers redirects to `http://127.0.0.1:8085/callback`, where the app receives it and hands the login to the service. If that port can't be used, **Paste the redirect URL instead** has you copy the address from the browser. Turn on **Remember these details on this PC** to keep them for next time in `%LOCALAPPDATA%\QuoteSnap\fyers-login.json`, with the Secret ID and PIN encrypted for your Windows account. **Log out** on the session card (after a confirmation) revokes the token at Fyers and makes the service forget the login, as `quotesnap-login logout` does; the details remembered on this PC stay, ready for the next login. Logging in and out needs the admin API key (`Auth__AdminApiKey`), which the app reads from `.env` or Settings. The dashboard's "Fyers login needed" alert links here.
- **Settings:** pick the Dark, Light or Purple theme (it applies at once and is saved in `%LOCALAPPDATA%\QuoteSnap\appearance.json`), and point the app at another API address, API key or admin API key. Saved settings go to `%LOCALAPPDATA%\QuoteSnap\desktop-settings.json`, with the keys encrypted for your Windows account; until you save, the app keeps reading `.env`.

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
| `GET /api/my-funds` | Your fund list, in your order (the order added until rearranged), each with its latest NAV |
| `GET /api/my-funds/estimates` | Today's estimated NAV move for each listed fund, from its holdings' weights and the last holdings live prices |
| `PUT /api/my-funds/{schemeCode}` | Add a Direct Plan scheme to your list (`201`, or `200` if it's already there) |
| `DELETE /api/my-funds/{schemeCode}` | Remove a scheme from your list (`204`, also when it wasn't there) |
| `PUT /api/my-funds/{schemeCode}/units` | Set the units you hold: `{ "units": 1234.567 }`, or `null` to clear (`200`; `400` unless above 0 with at most 4 decimals; `404` if the fund isn't listed) |
| `PUT /api/my-funds/order` | Rearrange your list: `{ "schemeCodes": [...] }` naming every listed fund once (`204`; `409` if it doesn't match the list). New funds go last |
| `POST /api/holdings/fetches` | Start fetching the holdings of every listed fund (`202`; `200` with the running fetch if one is already going) |
| `GET /api/holdings/fetches/{id}` | A fetch's status and per-fund progress; `GET /api/holdings/fetches/latest` for the most recent one |
| `GET /api/my-funds/{schemeCode}/holdings?month=yyyy-MM` | A listed fund's stored portfolio for a month (default: the latest) |
| `POST /api/holdings/quotes` | Request quotes for the distinct stock symbols in every listed fund's latest holdings (`202`; `200` with the running request if one is already going; `400` with no symbols) |
| `GET /api/holdings/quotes/latest` | The most recent holdings quote request with its results |
| `GET /api/holdings/exclusions` | The symbols excluded from holdings prices today |
| `PUT /api/holdings/exclusions` | Replace today's excluded symbols, e.g. `{"symbols":["NSE:OMNI-EQ"]}` (`[]` includes them all again) |
| `GET /api/holdings/top?limit=10` | The stocks your funds hold most, by weight averaged over the funds with holdings (by the value of your units once every fund has units; `byValue` says which), with the last live prices (see below); `?all=true` for every stock |
| `GET /api/live/quotes` | Streamed LTP and day change of the top holdings (see below) |
| `GET /api/live/status` | State of the live price feed |
| `POST /api/nav/refresh` | Download AMFI's latest NAV file into the service |
| `GET /api/nav/status` | How many NAVs are stored, the latest NAV date, and when they were downloaded (`loadedAt` is null before the first refresh) |
| `POST /api/symbols/refresh` | Download Fyers' NSE and BSE symbol masters into the service; symbol search and holdings fetches use them |
| `GET /api/symbols/search?q=tata motors` | Listed stocks whose name, ticker or ISIN contains every word, best matches first, with their Fyers symbols (`limit` up to 50, default 20; `503` before the first refresh) |
| `GET /api/symbols/status` | How many Fyers symbols are stored and when they were downloaded (`loadedAt` is null before the first refresh) |
| `GET /api/status` | Service version and Fyers session state |
| `GET /health` | Liveness check, no key needed |
| `GET /openapi/v1.json` | OpenAPI description |

Symbols use the Fyers format `EXCHANGE:SYMBOL-SERIES`, for example `NSE:SBIN-EQ`, `BSE:SENSEX-INDEX` or `NSE:NIFTY50-INDEX`. Results come back in the order you sent them. Requests and their results are kept for 3 days (`QuoteSnap__RetentionDays`); a cleanup runs at startup and then hourly.

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

`POST /api/holdings/fetches` downloads the last 12 months of portfolio data for every fund on your list and stores it in the `FundHoldings` table. No browser or login is needed. Download the Fyers symbols once before the first fetch; until then it returns `503`.

```powershell
Invoke-RestMethod http://127.0.0.1:8080/api/symbols/refresh -Method Post -Headers $h
$f = Invoke-RestMethod http://127.0.0.1:8080/api/holdings/fetches -Method Post -Headers $h
Invoke-RestMethod "http://127.0.0.1:8080/api/holdings/fetches/$($f.id)" -Headers $h
Invoke-RestMethod "http://127.0.0.1:8080/api/my-funds/152712/holdings?month=2026-09" -Headers $h
```

- A background worker goes through the funds one at a time, pausing between calls (`Holdings__DelayBetweenRequestsMs`, default 750). A fund takes one or two seconds, so the fetch returns `202` straight away. Poll it for progress; while it runs, the response carries `Retry-After: 1`. Only one fetch runs at a time, and one cut short by a restart resumes when the service starts.
- Each fund ends `Ok` with its holding count and month, `NotFound`, or `Error` with the reason. The fetch ends `Completed`, `CompletedWithErrors`, or `Failed` when no fund succeeded.
- Funds are matched to the source's schemes by name and fund house. Plan, option, punctuation and words like "Fund" are ignored, and small spelling differences are tolerated within the same fund house. A fund house AMFI has renamed by extending its name is still found under the old one (AMFI's "Zerodha CASE Mutual Fund" is the source's "Zerodha Mutual Fund"). Equity, hybrid, index and most other funds are covered, but few debt, liquid or fixed-maturity ones; those come back `NotFound`.
- A successful fetch replaces all 12 months stored for the fund. A failed one leaves them as they were. Removing a fund from the list deletes its holdings.
- `POST /api/holdings/quotes` collects the distinct Fyers symbols from the latest stored month of every listed fund and submits them as one ordinary quote request (same validation, Fyers login check and worker as `POST /api/quote-requests`). Poll `/api/quote-requests/{id}`, or `GET /api/holdings/quotes/latest` to read back the last one. These requests are flagged in the `QuoteRequests` table (`ForHoldings`) and are deleted by the usual retention cleanup.
- Symbols that keep failing (e.g. a suspended stock with no last traded price) can be excluded for today with `PUT /api/holdings/exclusions`; the desktop offers this on a request's errors, after a confirmation. Excluded symbols are left out of `POST /api/holdings/quotes` and the live feed until the local date changes.
- Each holding has its name, Fyers symbol (for listed shares, e.g. `NSE:HDFCBANK-EQ`, ready for `POST /api/quote-requests`), ISIN, asset type (Equity, Debt, TREPS, Net Payables, ...), sector, weight in percent of the fund's assets and share count. The holdings endpoint also returns the stored months and each holding's weight in the month before (`previousPercent`), ordered by weight. `GET /api/my-funds` includes a `holdings` summary (month, count, fetch time) for funds that have them.
- The source's address is `Holdings__BaseUrl`; `Holdings__PageSize` (default 500) sets how many holdings each call returns.
- Symbols come from Fyers' public symbol masters (`NSE_CM.csv` and `BSE_CM.csv`), matched by ISIN. The NSE symbol is used when there is one, otherwise the BSE one. Like the NAV file, they are downloaded only when you ask: `POST /api/symbols/refresh` (or **Refresh symbols** on the New request page) fetches both from `Holdings__SymbolMasterBaseUrl` (default `https://public.fyers.in/sym_details/`) and replaces the `FyersSymbols` table. Fetches then read symbols from that table. Listings change rarely, so refresh after a new listing or symbol change, then fetch holdings again to apply it. A failed download returns `502` and leaves the stored symbols untouched.

### Today's NAV estimate

`GET /api/my-funds/estimates` estimates how much each listed fund has moved today from its latest stored holdings and the latest prices: the last finished `POST /api/holdings/quotes` request, with any newer streamed price (see below) taking over:

- Estimated change % = Σ (holding weight % × stock's day change %) / 100. The day change is the quote's change percent, or its change divided by the previous price (LTP − change).
- Lines without a live price (TREPS, repo, net payables, cash, debt, unlisted shares or failed quotes) count as unchanged at their weight. `pricedPercent` and `pricedHoldings` show how much of the fund the estimate covers.
- Estimated NAV = latest AMFI NAV × (1 + change % / 100). The NAV is usually the previous trading day's, so the estimate is today's move on top of it; `navDate` and `pricesAt` say what it's based on.
- Nothing is stored. `changePercent` and `estimatedNav` are null for funds without holdings or before any live prices have been fetched. It's an estimate: the portfolio is a month old and fund expenses and intraday trades aren't known.
- Each fund also has the `units` you hold (null until entered with `PUT /api/my-funds/{schemeCode}/units`). Once every listed fund has units, the desktop dashboard weights the overall move by each fund's value (units × latest NAV) and can show today's estimated gain or loss in rupees, hidden until you press the eye icon, then shown for 5 seconds. Until then it's the plain average of the funds' estimated changes.

### Top holdings

`GET /api/holdings/top?limit=10` lists the stocks your funds hold most, as if the same amount were invested in each listed fund that has holdings. A stock's weight is the sum of its weights in those funds divided by their number, so a fund that doesn't hold it counts as 0%. Lines without a Fyers symbol are left out. Each stock also has how many funds hold it and, from the last finished holdings live-prices request, its LTP and day change % (a newer streamed price wins). `limit` is 1 to 100 (default 10); `all=true` returns every stock instead. The desktop dashboard shows the top 10 and the Stocks page all of them.

### Live updates (SignalR)

Instead of polling, clients can connect to the SignalR hub at `/hubs/events` (same `X-Api-Key` header; WebSockets, falling back to long polling). The `QuoteSnapHubClient` in `QuoteSnap.Client` wraps it, reconnects forever, and has `WaitForCompletionAsync` and `WaitForHoldingFetchAsync` that wait on pushes instead of polling. The hub pushes:

| Event | When |
|---|---|
| `QuoteRequestUpdated(summary)` | A quote request is queued, saves a batch, completes or fails |
| `HoldingFetchUpdated(fetch)` | A holdings fetch is queued, starts, finishes a fund, completes or fails |
| `LiveFeedStatusChanged(status)` | The live feed's state, symbols or error change (checked every second) |
| `LiveQuotesUpdated(list)` | Once a second, only the streamed quotes that ticked since the last push |
| `EstimatesUpdated(list)` | Once a second when prices, funds, holdings or NAVs changed: the same list as `GET /api/my-funds/estimates` |
| `Changed(what)` | `FyersSession`, `Navs`, `FyersSymbols`, `MyFunds` or `Holdings` changed; read them again |

Nothing is pushed while no client is connected. A client should read the state once after every (re)connection, since pushes in between are missed. The desktop app uses the hub and no longer polls. In Development the API logs every push and each second's ticked quotes at Debug (`QuoteSnap.Api.Events`, `QuoteSnap.Api.Live` in `appsettings.Development.json`); set `QuoteSnap.Api.Live` to `Verbose` to also log each tick received from Fyers.

### Live prices of the top holdings

The service streams the LTP and day change of the top 95 holdings (ranked like `GET /api/holdings/top`) over the Fyers Data Socket, plus the NIFTY 50, SENSEX, NIFTY BANK, NIFTY MIDCAP 100 and INDIA VIX indices. It stays connected whenever there is a Fyers login, so special sessions such as Muhurat trading or a weekend budget-day session are covered too (outside trading the connection is idle and the quotes keep the last prices). Symbols excluded for today are skipped, so the next holdings by weight take their places. It re-ranks after every holdings fetch, every My funds add or remove or units change, every exclusion change and at the start of a new day, subscribing only the symbols that changed. Prices are kept in memory only.

- `GET /api/live/quotes` returns the feed state and the latest quote of each streamed stock in rank order, followed by the indices.
- `GET /api/live/status` returns the state (`Disabled`, `NoSession`, `NoHoldings`, `Connecting`, `Connected`, `Reconnecting`, `Error`), the streamed symbols, the connection and last tick times, and the last error.

Settings (`LiveQuotes__...`): `Enabled` (default `true`), `SymbolCount` (stocks, 1 to 95, default 95; the 5 indices come on top) and `CheckIntervalSeconds` (15). Fyers allows 100 symbols per user across all its sockets, so the defaults use all of them; lower `SymbolCount` to stream anything else.

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

## Deploying to Kubernetes

`k8s/` runs the API and its own PostgreSQL in the `quotesnap` namespace, for example on Docker Desktop's Kubernetes (Settings → Kubernetes → Enable). Stop the local API first: the cluster's API takes port 8080, and two APIs would compete for Fyers' 100 streamed symbols. The cluster's database starts empty, so log in to Fyers and add your funds again.

```powershell
docker build -t quotesnap:local .

# The secret, from the values in .env (expected-fyers-id is optional).
kubectl create namespace quotesnap
kubectl create secret generic quotesnap -n quotesnap `
    --from-literal=db-password=<QUOTESNAP_DB_PASSWORD> `
    --from-literal=api-key=<Auth__ApiKey> `
    --from-literal=admin-api-key=<Auth__AdminApiKey>

kubectl apply -k k8s
kubectl get pods -n quotesnap
```

The API is then on `http://localhost:8080`, so the desktop app and login tool work unchanged. It runs as exactly one pod (its queues, live feed and event hub are in memory), applies migrations when it starts, and uses `/health` for its probes. PostgreSQL is reachable only inside the cluster; browse it with Adminer at [http://localhost:8081/?pgsql=postgres&username=quotesnap&db=quotesnap](http://localhost:8081/?pgsql=postgres&username=quotesnap&db=quotesnap) (the password is `QUOTESNAP_DB_PASSWORD`; stop Docker Compose first, as its Adminer also uses 8081), or with `kubectl exec -it -n quotesnap postgres-0 -- psql -U quotesnap -d quotesnap`. Its data and the API's encryption keys are on persistent volumes that survive restarts and redeploys, but `kubectl delete namespace quotesnap` deletes them.

Docker Desktop's load balancer listens on every network interface, so other machines on your network can reach the API and Adminer too (if the Windows firewall allows it). The API still needs its keys and Adminer the database password.

After changing the code, run `./k8s/deploy.ps1`. It builds the image with a unique tag, applies `k8s/`, waits for the rollout, checks `/health` and removes all but the newest 3 builds. It also creates the namespace and the secret from `.env` when they're missing, so on a fresh cluster it's the only command you need. `-SkipBuild` applies just manifest changes. Logs: `kubectl logs -n quotesnap deploy/quotesnap -f`.

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
