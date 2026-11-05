using System.Collections.Concurrent;
using System.Net;
using QuoteSnap.Client;
using QuoteSnap.Contracts;

namespace QuoteSnap.Api.IntegrationTests;

public sealed class HoldingTests
{
    private const int Dsp = 152045;
    private const int Motilal = 152712;
    private const int Parag = 122639;
    private const int DspPlan = 43892;
    private const int MotilalPlan = 44397;
    private const int ParagPlan = 19701;

    private static readonly DateOnly September = new(2026, 9, 1);
    private static readonly DateOnly August = new(2026, 8, 1);

    private const string NavFile = """
        Scheme Code;ISIN Div Payout/ ISIN Growth;ISIN Div Reinvestment;Scheme Name;Plan;Option;Net Asset Value;Date
        Open Ended Schemes(Equity Scheme - Sectoral/ Thematic)
        DSP Mutual Fund
        152045;INF740KA1TB3;-;DSP Banking & Financial Services Fund;Direct Plan;Growth;14.285;29-Oct-2026
        Open Ended Schemes(Other Scheme - Index Funds)
        Motilal Oswal Mutual Fund
        152712;INF247L01DH2;-;Motilal Oswal Nifty India Defence Index Fund;Direct Plan;Growth;11.6296;29-Oct-2026
        Open Ended Schemes(Equity Scheme - Flexi Cap Fund)
        PPFAS Mutual Fund
        122639;INF879O01027;-;Parag Parikh Flexi Cap Fund;Direct Plan;Growth;92.1;29-Oct-2026
        """;

    private const string Schemes = """
        {"code":0,"data":[
          {"amc":"DSP Mutual Fund","amc_id":18585,"scheme":[
            {"plan_id":"16438","name":"DSP Midcap Direct Plan-Growth"},
            {"plan_id":"43892","name":"DSP Banking & Financial Services Fund Direct-Growth"}]},
          {"amc":"Motilal Oswal Mutual Fund","amc_id":1,"scheme":[
            {"plan_id":"44397","name":"Motilal Oswal Nifty India Defence Index Fund Direct-Growth"}]},
          {"amc":"PPFAS Mutual Fund","amc_id":43539,"scheme":[
            {"plan_id":"19701","name":"Parag Parikh Flexi Cap Fund Direct-Growth"}]}
        ],"remarks":""}
        """;

    private const string Headers = """["sym","disp","exch","isin","shares","assetPerc","date","sector"]""";

    // ICICI Bank is left out of the NSE file so its BSE symbol is used.
    private const string NseMaster = """
        10100000001333,HDFC BANK LTD,0,1,0.1,INE040A01034,0915-1530|1815-1915:,2026-10-30,,NSE:HDFCBANK-EQ,10,10,1333,HDFCBANK,1333,-1.0,XX,10100000001333,None,1,3.7
        10100000002303,HINDUSTAN AERONAUTICS LTD,0,1,0.1,INE066F01020,0915-1530|1815-1915:,2026-10-30,,NSE:HAL-EQ,10,10,2303,HAL,2303,-1.0,XX,10100000002303,None,1,3.7
        10100000000383,BHARAT ELECTRONICS LTD,0,1,0.05,INE263A01024,0915-1530|1815-1915:,2026-10-30,,NSE:BEL-EQ,10,10,383,BEL,383,-1.0,XX,10100000000383,None,1,3.7
        """;

    private const string BseMaster = """
        1210000000500180,HDFC BANK LTD.,0,1,0.05,INE040A01034,0915-1530|1815-1915:,2026-10-30,,BSE:HDFCBANK-A,12,10,500180,HDFCBANK,500180,-1.0,XX,1210000000500180,None,0,0.0
        1210000000532174,ICICI BANK LTD.,0,1,0.05,INE090A01021,0915-1530|1815-1915:,2026-10-30,,BSE:ICICIBANK-A,12,10,532174,ICICIBANK,532174,-1.0,XX,1210000000532174,None,0,0.0
        """;

    /// <param name="percents">September, then August.</param>
    /// <param name="shares">September, then August.</param>
    private static string Row(string symbol, string name, string isin, string percents, string shares, string sector = "") =>
        $"""["{symbol}","{name}","{(isin.Length > 0 ? "NSE" : "")}","{isin}","{shares}","{percents}","2026-09|2026-08","{sector}"]""";

    private static string Page(int totalPages, string groups) =>
        $$"""{"code":0,"data":{{{groups}}},"headers":{{Headers}},"meta":{"totalcount":9,"totalpages":{{totalPages}}},"remarks":""}""";

    private static readonly string[] DspPages =
    [
        Page(2, $"""
            "Equity":[{Row("HDFCBANK", "HDFC Bank", "INE040A01034", "9.5|9.0", "1200.0|1100.0", "Banks")},{Row("ICICIBANK", "ICICI Bank", "INE090A01021", "8.0|8.5", "700.0|750.0", "Banks")}]
            """),
        Page(2, $"""
            "TREPS":[{Row("Reverse Repo", "Reverse Repo", "", "3.2|0.0", "0.0|0.0")}]
            """),
    ];

    private static readonly string[] MotilalPages =
    [
        Page(1, $"""
            "Equity":[{Row("HAL", "Hindustan Aeronautics", "INE066F01020", "20.0|19.0", "500.0|480.0", "Aerospace")},{Row("BEL", "Bharat Electronics", "INE263A01024", "0.0|17.5", "0.0|9000.0", "Aerospace")}]
            """),
    ];

    private static Task<QuoteSnapApiFactory> ListedAsync(bool startWorker, params int[] schemeCodes) => ListedAsync(new QuoteSnapApiFactory(startWorker: startWorker), schemeCodes);

    /// <summary>A service with NAVs loaded, the given funds on the list, Fyers' symbol masters, and ScanX holdings for the DSP and Motilal funds.</summary>
    private static async Task<QuoteSnapApiFactory> ListedAsync(QuoteSnapApiFactory factory, params int[] schemeCodes)
    {
        factory.NavFile = NavFile;
        factory.ScanxSchemes = Schemes;
        factory.NseSymbolMaster = NseMaster;
        factory.BseSymbolMaster = BseMaster;
        factory.ScanxHoldings[DspPlan] = DspPages;
        factory.ScanxHoldings[MotilalPlan] = MotilalPages;
        QuoteSnapClient client = factory.CreateQuoteSnapClient();
        await client.RefreshNavsAsync();
        await client.RefreshSymbolsAsync();
        foreach (int code in schemeCodes)
            await client.AddMyFundAsync(code);
        return factory;
    }

    private static async Task<HoldingFetch> FetchAsync(QuoteSnapClient client)
    {
        HoldingFetch started = await client.StartHoldingFetchAsync();
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(20));
        return await client.WaitForHoldingFetchAsync(started.Id, cancellationToken: timeout.Token);
    }

    [Fact]
    public async Task Holdings_of_every_listed_fund_are_fetched_page_by_page_and_stored()
    {
        await using QuoteSnapApiFactory factory = await ListedAsync(true, Dsp, Motilal);
        QuoteSnapClient client = factory.CreateQuoteSnapClient();

        HoldingFetch fetch = await FetchAsync(client);

        Assert.Equal(HoldingFetchStatus.Completed, fetch.Status);
        Assert.Equal((2, 2), (fetch.TotalFunds, fetch.FinishedFunds));
        Assert.Null(fetch.Error);
        Assert.Equal(
            [
                new HoldingFetchFund(Dsp, "DSP Banking & Financial Services Fund", HoldingFetchFundStatus.Ok,
                    "DSP Banking & Financial Services Fund Direct-Growth", 3, September, null),
                new HoldingFetchFund(Motilal, "Motilal Oswal Nifty India Defence Index Fund", HoldingFetchFundStatus.Ok,
                    "Motilal Oswal Nifty India Defence Index Fund Direct-Growth", 1, September, null),
            ],
            fetch.Funds);
        Assert.Equal([(DspPlan, 1), (DspPlan, 2), (MotilalPlan, 1)], factory.ScanxHoldingsCalls);

        MyFundList list = await client.GetMyFundsAsync();
        Assert.Equal(("DSP Banking & Financial Services Fund Direct-Growth", September, 3),
            (list.Funds[0].Holdings?.ScanxScheme, list.Funds[0].Holdings?.Month, list.Funds[0].Holdings?.Count));
        Assert.Equal(1, list.Funds[1].Holdings?.Count);

        FundHoldings dsp = await client.GetFundHoldingsAsync(Dsp);
        Assert.Equal(September, dsp.Month);
        Assert.Equal([September, August], dsp.Months);
        Assert.Equal(
            [
                new FundHolding("HDFC Bank", "NSE:HDFCBANK-EQ", "INE040A01034", "Equity", "Banks", 9.5m, 1200m, 9.0m),
                new FundHolding("ICICI Bank", "BSE:ICICIBANK-A", "INE090A01021", "Equity", "Banks", 8.0m, 700m, 8.5m),
                new FundHolding("Reverse Repo", null, null, "TREPS", null, 3.2m, null, null),
            ],
            dsp.Holdings);

        FundHoldings motilalAugust = await client.GetFundHoldingsAsync(Motilal, August);
        Assert.Equal(["Hindustan Aeronautics", "Bharat Electronics"], motilalAugust.Holdings.Select(h => h.Name));
        Assert.Equal(["NSE:HAL-EQ", "NSE:BEL-EQ"], motilalAugust.Holdings.Select(h => h.Symbol));
        Assert.Equal([19.0m, 17.5m], motilalAugust.Holdings.Select(h => h.Percent));
        Assert.All(motilalAugust.Holdings, h => Assert.Null(h.PreviousPercent));
    }

    [Fact]
    public async Task Funds_scanx_does_not_have_or_fails_on_are_reported_and_the_rest_still_load()
    {
        await using QuoteSnapApiFactory factory = await ListedAsync(true, Dsp, Motilal, Parag);
        QuoteSnapClient client = factory.CreateQuoteSnapClient();
        factory.FailingScanxPlans[MotilalPlan] = true;

        HoldingFetch fetch = await FetchAsync(client);

        Assert.Equal(HoldingFetchStatus.CompletedWithErrors, fetch.Status);
        Assert.Equal(3, fetch.FinishedFunds);
        Assert.Equal(
            [HoldingFetchFundStatus.Ok, HoldingFetchFundStatus.Error, HoldingFetchFundStatus.NotFound],
            fetch.Funds.Select(f => f.Status));
        Assert.Contains("404", fetch.Funds[1].Error);
        // Parag Parikh is on ScanX's list, but ScanX has no holdings for that plan.
        Assert.Equal("ScanX lists the scheme but has no holdings for it yet.", fetch.Funds[2].Error);
        Assert.Contains((ParagPlan, 1), factory.ScanxHoldingsCalls);

        QuoteSnapApiException ex = await Assert.ThrowsAsync<QuoteSnapApiException>(() => client.GetFundHoldingsAsync(Motilal));
        Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
        Assert.Equal(3, (await client.GetFundHoldingsAsync(Dsp)).Holdings.Count);
    }

    [Fact]
    public async Task A_fund_without_a_scanx_scheme_is_not_found()
    {
        await using QuoteSnapApiFactory factory = await ListedAsync(true, Dsp);
        QuoteSnapClient client = factory.CreateQuoteSnapClient();
        factory.ScanxSchemes = Schemes.Replace("DSP Banking & Financial Services Fund", "DSP Healthcare Fund", StringComparison.Ordinal);

        HoldingFetch fetch = await FetchAsync(client);

        Assert.Equal(HoldingFetchStatus.Failed, fetch.Status);
        Assert.Equal(HoldingFetchFundStatus.NotFound, fetch.Funds[0].Status);
        Assert.Equal("ScanX has no scheme named like “DSP Banking & Financial Services Fund”.", fetch.Funds[0].Error);
        Assert.Empty(factory.ScanxHoldingsCalls);
    }

    [Fact]
    public async Task Every_fund_fails_when_scanx_scheme_list_is_unavailable()
    {
        await using QuoteSnapApiFactory factory = await ListedAsync(true, Dsp, Motilal);
        QuoteSnapClient client = factory.CreateQuoteSnapClient();
        factory.ScanxSchemes = null;

        HoldingFetch fetch = await FetchAsync(client);

        Assert.Equal(HoldingFetchStatus.Failed, fetch.Status);
        Assert.StartsWith("Couldn't load ScanX's list of schemes", fetch.Error);
        Assert.All(fetch.Funds, f => Assert.Equal(HoldingFetchFundStatus.Error, f.Status));
        Assert.Null((await client.GetMyFundsAsync()).Funds[0].Holdings);
    }

    [Fact]
    public async Task Symbols_come_from_both_masters_and_are_kept_when_a_refresh_fails()
    {
        await using QuoteSnapApiFactory factory = await ListedAsync(true, Motilal);
        QuoteSnapClient client = factory.CreateQuoteSnapClient();
        FyersSymbolStatus loaded = await client.GetSymbolStatusAsync();
        Assert.Equal(4, loaded.SymbolCount);
        Assert.NotNull(loaded.LoadedAt);

        factory.BseSymbolMaster = null;
        QuoteSnapApiException ex = await Assert.ThrowsAsync<QuoteSnapApiException>(() => client.RefreshSymbolsAsync());
        Assert.Equal(HttpStatusCode.BadGateway, ex.StatusCode);
        Assert.Equal(loaded, await client.GetSymbolStatusAsync());

        // Fetches use the stored symbols, so they work while Fyers' files are unavailable.
        factory.NseSymbolMaster = null;
        HoldingFetch fetch = await FetchAsync(client);
        Assert.Equal(HoldingFetchStatus.Completed, fetch.Status);
        Assert.Equal(["NSE:HAL-EQ"], (await client.GetFundHoldingsAsync(Motilal)).Holdings.Select(h => h.Symbol));
    }

    [Fact]
    public async Task A_symbol_refresh_counts_isins_listed_only_on_bse()
    {
        await using QuoteSnapApiFactory factory = await ListedAsync(true);

        FyersSymbolRefreshResult result = await factory.CreateQuoteSnapClient().RefreshSymbolsAsync();

        Assert.Equal((4, 3, 1), (result.SymbolCount, result.NseCount, result.BseOnlyCount));
    }

    [Fact]
    public async Task Stocks_are_found_by_name_ticker_or_isin()
    {
        await using QuoteSnapApiFactory factory = await ListedAsync(true);
        QuoteSnapClient client = factory.CreateQuoteSnapClient();

        async Task<string[]> SearchAsync(string query) => [.. (await client.SearchSymbolsAsync(query)).Symbols.Select(s => s.Symbol)];

        Assert.Equal(["NSE:HDFCBANK-EQ"], await SearchAsync("hdfc bank"));
        Assert.Equal(["NSE:HDFCBANK-EQ"], await SearchAsync("HDFCBANK"));
        Assert.Equal(["BSE:ICICIBANK-A"], await SearchAsync("INE090A01021"));
        Assert.Equal(["NSE:HAL-EQ"], await SearchAsync("hindustan aero"));
        Assert.Equal(["NSE:HDFCBANK-EQ", "BSE:ICICIBANK-A"], await SearchAsync("bank"));
        Assert.Empty(await SearchAsync("%%"));
        FyersSymbolMatch hdfc = Assert.Single((await client.SearchSymbolsAsync("hdfc")).Symbols);
        Assert.Equal(new FyersSymbolMatch("NSE:HDFCBANK-EQ", "HDFC BANK LTD", "INE040A01034"), hdfc);
        QuoteSnapApiException tooShort = await Assert.ThrowsAsync<QuoteSnapApiException>(() => client.SearchSymbolsAsync("h"));
        Assert.Equal(HttpStatusCode.BadRequest, tooShort.StatusCode);
    }

    [Fact]
    public async Task Exact_tickers_come_before_other_matches()
    {
        await using QuoteSnapApiFactory factory = await ListedAsync(true);

        FyersSymbolSearchResult result = await factory.CreateQuoteSnapClient().SearchSymbolsAsync("bel");

        Assert.Equal("NSE:BEL-EQ", result.Symbols[0].Symbol);
    }

    [Fact]
    public async Task Fetching_needs_the_fyers_symbols_downloaded_first()
    {
        await using QuoteSnapApiFactory factory = new(startWorker: true) { NavFile = NavFile, ScanxSchemes = Schemes };
        QuoteSnapClient client = factory.CreateQuoteSnapClient();
        await client.RefreshNavsAsync();
        await client.AddMyFundAsync(Motilal);
        Assert.Equal(new FyersSymbolStatus(0, null), await client.GetSymbolStatusAsync());

        QuoteSnapApiException ex = await Assert.ThrowsAsync<QuoteSnapApiException>(() => client.StartHoldingFetchAsync());
        QuoteSnapApiException search = await Assert.ThrowsAsync<QuoteSnapApiException>(() => client.SearchSymbolsAsync("hdfc"));

        Assert.Equal(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
        Assert.Equal(HttpStatusCode.ServiceUnavailable, search.StatusCode);
        Assert.Null(await client.GetLatestHoldingFetchAsync());
    }

    [Fact]
    public async Task Starting_while_a_fetch_is_queued_returns_that_fetch()
    {
        await using QuoteSnapApiFactory factory = await ListedAsync(false, Dsp);
        QuoteSnapClient client = factory.CreateQuoteSnapClient();
        HttpClient http = factory.CreateClient();
        http.DefaultRequestHeaders.Add("X-Api-Key", QuoteSnapApiFactory.ApiKey);

        using HttpResponseMessage first = await http.PostAsync("/api/holdings/fetches", null);
        using HttpResponseMessage second = await http.PostAsync("/api/holdings/fetches", null);

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        HoldingFetch? latest = await client.GetLatestHoldingFetchAsync();
        Assert.Equal(HoldingFetchStatus.Queued, latest?.Status);
        Assert.Equal(first.Headers.Location?.OriginalString, $"/api/holdings/fetches/{latest?.Id}");
        Assert.Equal((1, 0), (latest?.TotalFunds, latest?.FinishedFunds));
    }

    [Fact]
    public async Task Fetching_again_replaces_the_holdings_and_removing_the_fund_deletes_them()
    {
        await using QuoteSnapApiFactory factory = await ListedAsync(true, Motilal);
        QuoteSnapClient client = factory.CreateQuoteSnapClient();
        await FetchAsync(client);

        factory.ScanxHoldings[MotilalPlan] =
        [
            Page(1, $"""
                "Equity":[{Row("BEL", "Bharat Electronics", "INE263A01024", "21.0|17.5", "9500.0|9000.0", "Aerospace")},{Row("HAL", "Hindustan Aeronautics", "INE066F01020", "19.5|20.0", "490.0|500.0", "Aerospace")}]
                """),
        ];
        HoldingFetch second = await FetchAsync(client);

        Assert.Equal(2, second.Funds[0].HoldingCount);
        FundHoldings holdings = await client.GetFundHoldingsAsync(Motilal);
        Assert.Equal(["Bharat Electronics", "Hindustan Aeronautics"], holdings.Holdings.Select(h => h.Name));
        Assert.Equal([17.5m, 20.0m], holdings.Holdings.Select(h => h.PreviousPercent));

        await client.RemoveMyFundAsync(Motilal);
        await client.AddMyFundAsync(Motilal);
        QuoteSnapApiException ex = await Assert.ThrowsAsync<QuoteSnapApiException>(() => client.GetFundHoldingsAsync(Motilal));
        Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
        Assert.Null((await client.GetMyFundsAsync()).Funds[0].Holdings);
    }

    [Fact]
    public async Task Fetching_needs_funds_on_the_list()
    {
        await using QuoteSnapApiFactory factory = await ListedAsync(true);
        QuoteSnapClient client = factory.CreateQuoteSnapClient();

        QuoteSnapApiException ex = await Assert.ThrowsAsync<QuoteSnapApiException>(() => client.StartHoldingFetchAsync());

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        Assert.Null(await client.GetLatestHoldingFetchAsync());
    }

    [Fact]
    public async Task An_unknown_month_is_rejected()
    {
        await using QuoteSnapApiFactory factory = await ListedAsync(true, Motilal);
        QuoteSnapClient client = factory.CreateQuoteSnapClient();
        await FetchAsync(client);

        QuoteSnapApiException ex = await Assert.ThrowsAsync<QuoteSnapApiException>(() => client.GetFundHoldingsAsync(Motilal, new DateOnly(2025, 1, 1)));

        Assert.Equal(HttpStatusCode.NotFound, ex.StatusCode);
    }

    [Fact]
    public async Task Live_prices_are_requested_once_for_each_stock_in_the_listed_funds_latest_holdings()
    {
        await using QuoteSnapApiFactory factory = await ListedAsync(true, Dsp, Motilal);
        QuoteSnapClient client = factory.CreateQuoteSnapClient();
        await factory.SeedSessionAsync();
        // HDFC Bank is held by both funds; BEL was only held in August, which is no longer the latest month.
        factory.ScanxHoldings[MotilalPlan] =
        [
            Page(1, $"""
                "Equity":[{Row("HAL", "Hindustan Aeronautics", "INE066F01020", "20.0|19.0", "500.0|480.0", "Aerospace")},{Row("BEL", "Bharat Electronics", "INE263A01024", "0.0|17.5", "0.0|9000.0", "Aerospace")},{Row("HDFCBANK", "HDFC Bank", "INE040A01034", "1.0|0.0", "10.0|0.0", "Banks")}]
                """),
        ];
        await FetchAsync(client);
        Assert.Null(await client.GetLatestHoldingQuotesAsync());

        QuoteRequestAccepted accepted = await client.StartHoldingQuotesAsync();
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(20));
        QuoteRequestDetails finished = await client.WaitForCompletionAsync(accepted.RequestId, cancellationToken: timeout.Token);

        Assert.Equal(3, accepted.TotalSymbols);
        Assert.Equal(QuoteRequestStatus.Completed, finished.Status);
        Assert.Equal(["BSE:ICICIBANK-A", "NSE:HAL-EQ", "NSE:HDFCBANK-EQ"], finished.Results.Select(r => r.Symbol));
        Assert.All(finished.Results, r => Assert.Equal(100.5m, r.Ltp));

        QuoteRequestDetails? latest = await client.GetLatestHoldingQuotesAsync();
        Assert.Equal(accepted.RequestId, latest?.RequestId);
        Assert.Equal(3, latest?.Results.Count);

        // Ordinary quote requests don't count as the holdings' latest prices.
        await client.FetchSnapshotAsync(["NSE:SBIN-EQ"], cancellationToken: timeout.Token);
        Assert.Equal(accepted.RequestId, (await client.GetLatestHoldingQuotesAsync())?.RequestId);
    }

    [Fact]
    public async Task Excluded_symbols_are_left_out_of_live_prices()
    {
        await using QuoteSnapApiFactory factory = await ListedAsync(true, Dsp, Motilal);
        QuoteSnapClient client = factory.CreateQuoteSnapClient();
        await factory.SeedSessionAsync();
        factory.ScanxHoldings[MotilalPlan] =
        [
            Page(1, $"""
                "Equity":[{Row("HAL", "Hindustan Aeronautics", "INE066F01020", "20.0|19.0", "500.0|480.0", "Aerospace")},{Row("HDFCBANK", "HDFC Bank", "INE040A01034", "1.0|0.0", "10.0|0.0", "Banks")}]
                """),
        ];
        await FetchAsync(client);

        ExcludedSymbolList excluded = await client.SetExcludedSymbolsAsync(["nse:hal-eq", "NSE:NOTHELD-EQ"]);
        Assert.Equal(["NSE:HAL-EQ", "NSE:NOTHELD-EQ"], excluded.Symbols);
        Assert.NotNull(excluded.ExcludedAt);
        Assert.Equal(excluded.Symbols, (await client.GetExcludedSymbolsAsync()).Symbols);

        QuoteRequestAccepted accepted = await client.StartHoldingQuotesAsync();
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(20));
        QuoteRequestDetails finished = await client.WaitForCompletionAsync(accepted.RequestId, cancellationToken: timeout.Token);

        Assert.Equal(["BSE:ICICIBANK-A", "NSE:HDFCBANK-EQ"], finished.Results.Select(r => r.Symbol));
        Assert.Equal(QuoteRequestStatus.Completed, finished.Status);

        ExcludedSymbolList cleared = await client.SetExcludedSymbolsAsync([]);
        Assert.Empty(cleared.Symbols);
        Assert.Null(cleared.ExcludedAt);
    }

    [Fact]
    public async Task Excluded_symbols_give_their_live_feed_places_to_the_next_holdings_by_weight()
    {
        await using QuoteSnapApiFactory factory = await ListedAsync(new QuoteSnapApiFactory(liveFeed: true) { LiveSymbolCount = 2 }, Dsp, Motilal);
        QuoteSnapClient client = factory.CreateQuoteSnapClient();
        await factory.SeedSessionAsync();
        await FetchAsync(client);
        await WaitForAsync(async () => (await client.GetLiveFeedStatusAsync()).Symbols.Count == 7);
        Assert.Equal(["NSE:HAL-EQ", "NSE:HDFCBANK-EQ"], (await client.GetLiveFeedStatusAsync()).Symbols.Take(2));

        await client.SetExcludedSymbolsAsync(["NSE:HAL-EQ"]);
        await WaitForAsync(() => Task.FromResult(factory.LiveFeed.Calls.Count == 3));

        Assert.Equal(["NSE:HDFCBANK-EQ", "BSE:ICICIBANK-A"], (await client.GetLiveFeedStatusAsync()).Symbols.Take(2));
        Assert.Equal(["-NSE:HAL-EQ", "+BSE:ICICIBANK-A"], factory.LiveFeed.Calls.Skip(1));
    }

    [Fact]
    public async Task Requesting_live_prices_while_a_request_is_queued_returns_that_request()
    {
        await using QuoteSnapApiFactory factory = await ListedAsync(true, Dsp);
        QuoteSnapClient client = factory.CreateQuoteSnapClient();
        await FetchAsync(client);
        // A second host on the same database without workers, so the request stays queued.
        await using QuoteSnapApiFactory idle = new(factory.DataDirectory, startWorker: false);
        await idle.SeedSessionAsync();
        HttpClient http = idle.CreateClient();
        http.DefaultRequestHeaders.Add("X-Api-Key", QuoteSnapApiFactory.ApiKey);

        using HttpResponseMessage first = await http.PostAsync("/api/holdings/quotes", null);
        using HttpResponseMessage second = await http.PostAsync("/api/holdings/quotes", null);

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.OK, second.StatusCode);
        QuoteRequestDetails? latest = await idle.CreateQuoteSnapClient().GetLatestHoldingQuotesAsync();
        Assert.Equal(QuoteRequestStatus.Queued, latest?.Status);
        Assert.Equal(first.Headers.Location?.OriginalString, $"/api/quote-requests/{latest?.RequestId}");
        Assert.Equal(2, latest?.TotalSymbols);
    }

    [Fact]
    public async Task Live_prices_need_fetched_holdings_with_symbols()
    {
        await using QuoteSnapApiFactory factory = await ListedAsync(true, Dsp);
        QuoteSnapClient client = factory.CreateQuoteSnapClient();
        await factory.SeedSessionAsync();

        QuoteSnapApiException ex = await Assert.ThrowsAsync<QuoteSnapApiException>(() => client.StartHoldingQuotesAsync());

        Assert.Equal(HttpStatusCode.BadRequest, ex.StatusCode);
        Assert.Null(await client.GetLatestHoldingQuotesAsync());
    }

    [Fact]
    public async Task Live_prices_need_a_fyers_session()
    {
        await using QuoteSnapApiFactory factory = await ListedAsync(true, Dsp);
        QuoteSnapClient client = factory.CreateQuoteSnapClient();
        await FetchAsync(client);

        QuoteSnapApiException ex = await Assert.ThrowsAsync<QuoteSnapApiException>(() => client.StartHoldingQuotesAsync());

        Assert.Equal(HttpStatusCode.ServiceUnavailable, ex.StatusCode);
    }

    [Fact]
    public async Task Each_funds_nav_is_estimated_from_its_holdings_weights_and_their_day_changes()
    {
        await using QuoteSnapApiFactory factory = await ListedAsync(true, Dsp, Motilal, Parag);
        QuoteSnapClient client = factory.CreateQuoteSnapClient();
        await factory.SeedSessionAsync();
        await FetchAsync(client);

        NavEstimateList before = await client.GetNavEstimatesAsync();
        Assert.Null(before.RequestId);
        Assert.Null(before.PricesAt);
        Assert.Equal([Dsp, Motilal, Parag], before.Funds.Select(f => f.SchemeCode));
        Assert.All(before.Funds, f => Assert.Null(f.ChangePercent));
        Assert.Equal(3, before.Funds[0].TotalHoldings);
        Assert.Equal(September, before.Funds[0].HoldingsMonth);

        QuoteRequestAccepted accepted = await client.StartHoldingQuotesAsync();
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(20));
        await client.WaitForCompletionAsync(accepted.RequestId, cancellationToken: timeout.Token);

        NavEstimateList after = await client.GetNavEstimatesAsync();
        Assert.Equal(accepted.RequestId, after.RequestId);
        Assert.NotNull(after.PricesAt);

        // Every stock moves 1.25 on 99.25 (+1.2594%); DSP's 3.2% in TREPS counts as unchanged.
        NavEstimate dsp = after.Funds[0];
        Assert.Equal(0.2204m, dsp.ChangePercent);
        Assert.Equal(14.285m, dsp.Nav);
        Assert.Equal(new DateOnly(2026, 10, 29), dsp.NavDate);
        Assert.Equal(14.3165m, dsp.EstimatedNav);
        Assert.Equal(17.5m, dsp.PricedPercent);
        Assert.Equal(2, dsp.PricedHoldings);
        Assert.Equal(3, dsp.TotalHoldings);

        NavEstimate motilal = after.Funds[1];
        Assert.Equal(0.2519m, motilal.ChangePercent);
        Assert.Equal(11.6589m, motilal.EstimatedNav);

        // Parag Parikh has no holdings on ScanX, so it can't be estimated.
        NavEstimate parag = after.Funds[2];
        Assert.Null(parag.HoldingsMonth);
        Assert.Null(parag.ChangePercent);
        Assert.Equal(92.1m, parag.Nav);
    }

    [Fact]
    public async Task Top_holdings_average_each_stocks_weight_over_the_funds_with_holdings_and_carry_the_last_prices()
    {
        await using QuoteSnapApiFactory factory = await ListedAsync(true, Dsp, Motilal, Parag);
        QuoteSnapClient client = factory.CreateQuoteSnapClient();
        Assert.Equal(0, (await client.GetTopHoldingsAsync()).FundCount);

        await factory.SeedSessionAsync();
        await FetchAsync(client);

        // Parag Parikh has no holdings, so weights are averaged over DSP and Motilal; DSP's TREPS has no symbol.
        TopHoldingList before = await client.GetTopHoldingsAsync();
        Assert.Equal(2, before.FundCount);
        Assert.Null(before.RequestId);
        Assert.Equal(["NSE:HAL-EQ", "NSE:HDFCBANK-EQ", "BSE:ICICIBANK-A"], before.Holdings.Select(h => h.Symbol));
        Assert.Equal([10m, 4.75m, 4m], before.Holdings.Select(h => h.Percent));
        Assert.All(before.Holdings, h => Assert.Equal(1, h.FundCount));
        Assert.All(before.Holdings, h => Assert.Null(h.Ltp));
        Assert.Equal(["NSE:HAL-EQ"], (await client.GetTopHoldingsAsync(1)).Holdings.Select(h => h.Symbol));
        Assert.Equal(before.Holdings, (await client.GetTopHoldingsAsync(null)).Holdings);

        QuoteRequestAccepted accepted = await client.StartHoldingQuotesAsync();
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(20));
        await client.WaitForCompletionAsync(accepted.RequestId, cancellationToken: timeout.Token);

        TopHoldingList after = await client.GetTopHoldingsAsync();
        Assert.Equal(accepted.RequestId, after.RequestId);
        Assert.NotNull(after.PricesAt);
        Assert.All(after.Holdings, h => Assert.Equal(100.5m, h.Ltp));
        Assert.All(after.Holdings, h => Assert.Equal(1.2594m, h.ChangePercent));
    }

    [Fact]
    public async Task The_top_holdings_are_streamed_live_and_re_ranked_when_the_holdings_or_the_list_change()
    {
        await using QuoteSnapApiFactory factory = await ListedAsync(new QuoteSnapApiFactory(liveFeed: true), Dsp, Motilal);
        QuoteSnapClient client = factory.CreateQuoteSnapClient();
        await WaitForAsync(async () => (await client.GetLiveFeedStatusAsync()).State == LiveFeedState.NoSession);

        await factory.SeedSessionAsync();
        await WaitForAsync(async () => (await client.GetLiveFeedStatusAsync()).State == LiveFeedState.NoHoldings);

        await FetchAsync(client);
        await WaitForAsync(async () => (await client.GetLiveFeedStatusAsync()).Symbols.Count == 8);
        LiveFeedStatus status = await client.GetLiveFeedStatusAsync();
        Assert.Equal(LiveFeedState.Connected, status.State);
        Assert.Equal(["NSE:HAL-EQ", "NSE:HDFCBANK-EQ", "BSE:ICICIBANK-A", "NSE:NIFTY50-INDEX", "BSE:SENSEX-INDEX", "NSE:NIFTYBANK-INDEX", "NSE:NIFTYMIDCAP100-INDEX", "NSE:INDIAVIX-INDEX"], status.Symbols);
        Assert.NotNull(status.ConnectedAt);
        Assert.Equal(["+NSE:HAL-EQ,NSE:HDFCBANK-EQ,BSE:ICICIBANK-A,NSE:NIFTY50-INDEX,BSE:SENSEX-INDEX,NSE:NIFTYBANK-INDEX,NSE:NIFTYMIDCAP100-INDEX,NSE:INDIAVIX-INDEX"], factory.LiveFeed.Calls);

        // A later tick keeps the values the earlier one sent and this one left out; unstreamed symbols are ignored.
        factory.LiveFeed.Tick("NSE:HDFCBANK-EQ", 1650.25m, 12.5m, 0.7631m);
        factory.LiveFeed.Tick("NSE:HDFCBANK-EQ", 1651m, null, null);
        factory.LiveFeed.Tick("NSE:HAL-EQ", 4500.5m, -20m, -0.4425m);
        factory.LiveFeed.Tick("NSE:SBIN-EQ", 800m, 1m, 0.125m);
        factory.LiveFeed.Tick("NSE:NIFTY50-INDEX", 25600.35m, 102.5m, 0.4021m);
        LiveQuoteList quotes = await client.GetLiveQuotesAsync();
        Assert.Equal(LiveFeedState.Connected, quotes.State);
        Assert.NotNull(quotes.LastTickAt);
        Assert.Equal(["NSE:HAL-EQ", "NSE:HDFCBANK-EQ", "NSE:NIFTY50-INDEX"], quotes.Quotes.Select(q => q.Symbol));
        Assert.Equal((1651m, 12.5m, 0.7631m), (quotes.Quotes[1].Ltp, quotes.Quotes[1].Change, quotes.Quotes[1].ChangePercent));
        Assert.Equal(3, (await client.GetLiveFeedStatusAsync()).QuoteCount);

        // Only Motilal holds HAL, so removing it unsubscribes HAL and drops its quote, on the same socket; the indices stay.
        await client.RemoveMyFundAsync(Motilal);
        await WaitForAsync(async () => (await client.GetLiveFeedStatusAsync()).Symbols.Count == 7);
        Assert.Equal(["+NSE:HAL-EQ,NSE:HDFCBANK-EQ,BSE:ICICIBANK-A,NSE:NIFTY50-INDEX,BSE:SENSEX-INDEX,NSE:NIFTYBANK-INDEX,NSE:NIFTYMIDCAP100-INDEX,NSE:INDIAVIX-INDEX", "-NSE:HAL-EQ"], factory.LiveFeed.Calls);
        Assert.Equal(["NSE:HDFCBANK-EQ", "NSE:NIFTY50-INDEX"], (await client.GetLiveQuotesAsync()).Quotes.Select(q => q.Symbol));
        Assert.Equal(1, factory.LiveFeed.Connections);
    }

    [Fact]
    public async Task The_hub_pushes_fetch_progress_live_status_quotes_and_estimates()
    {
        await using QuoteSnapApiFactory factory = await ListedAsync(new QuoteSnapApiFactory(liveFeed: true), Dsp, Motilal);
        QuoteSnapClient client = factory.CreateQuoteSnapClient();
        await using QuoteSnapHubClient hub = factory.CreateHubClient();
        ConcurrentQueue<HoldingFetch> fetches = new();
        ConcurrentQueue<ServerChange> changes = new();
        ConcurrentQueue<LiveFeedStatus> statuses = new();
        ConcurrentQueue<LiveQuoteList> quotes = new();
        ConcurrentQueue<NavEstimateList> estimates = new();
        hub.HoldingFetchUpdated += fetches.Enqueue;
        hub.Changed += changes.Enqueue;
        hub.LiveFeedStatusChanged += statuses.Enqueue;
        hub.LiveQuotesUpdated += quotes.Enqueue;
        hub.EstimatesUpdated += estimates.Enqueue;
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(20));
        await hub.StartAsync(timeout.Token);

        HoldingFetch started = await client.StartHoldingFetchAsync(timeout.Token);
        HoldingFetch fetch = await hub.WaitForHoldingFetchAsync(client, started.Id, cancellationToken: timeout.Token);
        Assert.Equal(HoldingFetchStatus.Completed, fetch.Status);
        await EventHubTests.WaitForAsync(() => changes.Contains(ServerChange.Holdings), timeout.Token);
        Assert.Equal(HoldingFetchStatus.Queued, fetches.First().Status);
        Assert.Contains(fetches, f => f.Status == HoldingFetchStatus.Running && f.FinishedFunds == 1);

        await factory.SeedSessionAsync();
        await EventHubTests.WaitForAsync(() => statuses.Any(s => s.State == LiveFeedState.Connected && s.Symbols.Count == 8), timeout.Token);

        factory.LiveFeed.Tick("NSE:HDFCBANK-EQ", 1650.25m, 12.5m, 0.7631m);
        await EventHubTests.WaitForAsync(() => quotes.Any(q => q.Quotes.Any(x => x.Symbol == "NSE:HDFCBANK-EQ")), timeout.Token);
        LiveQuoteList batch = quotes.First(q => q.Quotes.Count > 0);
        Assert.Equal([new LiveQuote("NSE:HDFCBANK-EQ", 1650.25m, 12.5m, 0.7631m, batch.Quotes[0].UpdatedAt)], batch.Quotes);
        Assert.Equal(LiveFeedState.Connected, batch.State);

        // DSP holds HDFC Bank, so its estimate moves with the tick, without any quote request.
        await EventHubTests.WaitForAsync(() => estimates.Any(e => e.PricesAt is not null && e.Funds.Any(f => f.SchemeCode == Dsp && f.PricedHoldings > 0)), timeout.Token);
        NavEstimateList list = estimates.Last();
        Assert.Null(list.RequestId);
        Assert.NotNull(list.Funds.Single(f => f.SchemeCode == Dsp).EstimatedNav);
        TopHoldingList top = await client.GetTopHoldingsAsync(10, timeout.Token);
        Assert.Equal(1650.25m, top.Holdings.Single(h => h.Symbol == "NSE:HDFCBANK-EQ").Ltp);
    }

    private static async Task WaitForAsync(Func<Task<bool>> condition)
    {
        using CancellationTokenSource timeout = new(TimeSpan.FromSeconds(20));
        while (!await condition())
            await Task.Delay(50, timeout.Token);
    }
}
