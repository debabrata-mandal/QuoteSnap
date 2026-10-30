using QuoteSnap.Api.Holdings;

namespace QuoteSnap.Tests;

public sealed class ScanxJsonTests
{
    [Fact]
    public void Reads_every_scheme_of_every_amc()
    {
        IReadOnlyList<ScanxScheme> schemes = ScanxJson.ParseSchemes("""
            {"code":0,"data":[
              {"amc":"PPFAS Mutual Fund","amc_id":43539,"scheme":[
                {"plan_id":"19701","name":"Parag Parikh Flexi Cap Fund Direct-Growth","seo":"parag-parikh-flexi-cap-fund-direct-growth"},
                {"plan_id":"40105","name":"Parag Parikh ELSS Tax Saver Fund Direct-Growth","seo":"parag-parikh-tax-saver-fund-direct-growth"}]},
              {"amc":"DSP Mutual Fund","amc_id":18585,"scheme":[
                {"plan_id":"43892","name":"DSP Banking & Financial Services Fund Direct-Growth"},
                {"plan_id":"not-a-number","name":"Broken"}]}
            ],"remarks":""}
            """);

        Assert.Equal(
            [
                new ScanxScheme("PPFAS Mutual Fund", 19701, "Parag Parikh Flexi Cap Fund Direct-Growth"),
                new ScanxScheme("PPFAS Mutual Fund", 40105, "Parag Parikh ELSS Tax Saver Fund Direct-Growth"),
                new ScanxScheme("DSP Mutual Fund", 43892, "DSP Banking & Financial Services Fund Direct-Growth"),
            ],
            schemes);
    }

    [Theory]
    [InlineData("<html>Bad gateway</html>")]
    [InlineData("""{"code":0,"data":[]}""")]
    [InlineData("""{"code":-1,"data":"","remarks":"server busy"}""")]
    public void Anything_but_an_amc_list_is_rejected(string json)
    {
        Assert.Throws<FormatException>(() => ScanxJson.ParseSchemes(json));
    }

    [Fact]
    public void Reads_holdings_by_asset_type_with_their_monthly_weights()
    {
        ScanxHoldingsPage page = ScanxJson.ParseHoldingsPage(HoldingsJson);

        Assert.Equal(2, page.TotalPages);
        Assert.Equal(3, page.Holdings.Count);

        ScanxHolding bank = page.Holdings[0];
        Assert.Equal(("Equity", "HDFC Bank", (string?)"HDFCBANK", (string?)"INE040A01034", (string?)"Banks"), (bank.AssetType, bank.Name, bank.Symbol, bank.Isin, bank.Sector));
        Assert.Equal(
            [
                new ScanxHoldingMonth(new DateOnly(2026, 9, 1), 7.7543m, 158729224m),
                new ScanxHoldingMonth(new DateOnly(2026, 8, 1), 7.6347m, 158729224m),
                new ScanxHoldingMonth(new DateOnly(2026, 7, 1), 0m, 0m),
            ],
            bank.Months);

        ScanxHolding future = page.Holdings[1];
        Assert.Equal(("Futures", (string?)null, (string?)null, -0.002m), (future.AssetType, future.Symbol, future.Isin, future.Months[1].Percent));

        ScanxHolding repo = page.Holdings[2];
        Assert.Equal(("TREPS", "Reverse Repo", (string?)null, (string?)null), (repo.AssetType, repo.Name, repo.Symbol, repo.Sector));
    }

    [Fact]
    public void A_page_past_the_end_has_no_holdings()
    {
        ScanxHoldingsPage page = ScanxJson.ParseHoldingsPage(
            """{"code":0,"data":{},"headers":["sym","disp","assetPerc","date"],"meta":{"Equity":44,"totalcount":44,"totalpages":1}}""");

        Assert.Empty(page.Holdings);
        Assert.Equal(1, page.TotalPages);
    }

    [Fact]
    public void An_unknown_plan_has_no_holdings()
    {
        ScanxHoldingsPage page = ScanxJson.ParseHoldingsPage(
            """{"code":-1,"data":"","remarks":"unable to fetch data, error :no record fetched"}""");

        Assert.Empty(page.Holdings);
        Assert.Equal(0, page.TotalPages);
    }

    [Theory]
    [InlineData("""{"code":-1,"data":"","remarks":"rate limited"}""")]
    [InlineData("""{"code":0,"data":{}}""")]
    [InlineData("""{"code":0,"data":{},"headers":["sym","disp"]}""")]
    [InlineData("not json")]
    public void Errors_and_unknown_layouts_are_rejected(string json)
    {
        Assert.Throws<FormatException>(() => ScanxJson.ParseHoldingsPage(json));
    }

    private const string HoldingsJson = """
        {"code":0,
         "data":{
           "Equity":[["HDFCBANK","HDFC Bank",500180,"E","BSE",1,0.05,1,"685.00","hdfc-bank-ltd","-0.90","-0.13","EQUITY","ES","INE040A01034",
                      "158729224.0|158729224.0|0.0","7.7543|7.6347|0.0000","2026-09|2026-08|2026-07","Banks","Banks - Private Sector",""]],
           "Futures":[["Polycab India Limited February 2026 Future","Polycab India Limited February 2026 Future",0,"","",0,0,0,0,"",0,0,"","Futures","",
                       "0.0|-3750.0|0.0","0.0000|-0.0020|0.0000","2026-09|2026-08|2026-07","","",""]],
           "TREPS":[["Reverse Repo","Reverse Repo",0,"","",0,0,0,0,"",0,0,"","Reverse Repo","","0.0|0.0|0.0","4.0878|4.8524|0.0000","2026-09|2026-08|2026-07","","",""]]
         },
         "headers":["sym","disp","sid","seg","exch","ls","ts","mtp","ltp","seo","ch","pch","inst","exchInstType","isin","shares","assetPerc","date","sector","sub_sector","act"],
         "meta":{"Equity":1,"Futures":1,"TREPS":1,"totalcount":3,"totalpages":2},
         "remarks":""}
        """;
}
