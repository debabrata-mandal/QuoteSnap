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

    /// <summary>A service with NAVs loaded, the given funds on the list, and ScanX holdings for the DSP and Motilal funds.</summary>
    private static async Task<QuoteSnapApiFactory> ListedAsync(bool startWorker, params int[] schemeCodes)
    {
        QuoteSnapApiFactory factory = new(startWorker: startWorker) { NavFile = NavFile, ScanxSchemes = Schemes };
        factory.ScanxHoldings[DspPlan] = DspPages;
        factory.ScanxHoldings[MotilalPlan] = MotilalPages;
        QuoteSnapClient client = factory.CreateQuoteSnapClient();
        await client.RefreshNavsAsync();
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
                new FundHolding("HDFC Bank", "HDFCBANK", "INE040A01034", "Equity", "Banks", 9.5m, 1200m, 9.0m),
                new FundHolding("ICICI Bank", "ICICIBANK", "INE090A01021", "Equity", "Banks", 8.0m, 700m, 8.5m),
                new FundHolding("Reverse Repo", null, null, "TREPS", null, 3.2m, null, null),
            ],
            dsp.Holdings);

        FundHoldings motilalAugust = await client.GetFundHoldingsAsync(Motilal, August);
        Assert.Equal(["Hindustan Aeronautics", "Bharat Electronics"], motilalAugust.Holdings.Select(h => h.Name));
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
}
