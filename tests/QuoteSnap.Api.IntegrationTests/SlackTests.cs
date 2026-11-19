using System.Net;
using Microsoft.Extensions.DependencyInjection;
using QuoteSnap.Api.Data;
using QuoteSnap.Api.Endpoints;
using QuoteSnap.Api.Slack;
using QuoteSnap.Client;
using QuoteSnap.Contracts;

namespace QuoteSnap.Api.IntegrationTests;

public sealed class SlackTests
{


    [Fact]
    public async Task Reports_cannot_be_turned_on_without_a_bot()
    {
        await using QuoteSnapApiFactory factory = new();
        QuoteSnapClient client = factory.CreateQuoteSnapClient();

        Assert.Equal(new SlackStatus(false, false, 10), await client.GetSlackAsync());
        QuoteSnapApiException enable = await Assert.ThrowsAsync<QuoteSnapApiException>(() => client.SetSlackAsync(true));
        Assert.Equal(HttpStatusCode.Conflict, enable.StatusCode);
        QuoteSnapApiException test = await Assert.ThrowsAsync<QuoteSnapApiException>(() => client.SendSlackTestAsync());
        Assert.Equal(HttpStatusCode.Conflict, test.StatusCode);
        Assert.Empty(factory.SlackMessages);
    }

    [Fact]
    public async Task The_switch_is_saved_and_the_test_posts_whatever_is_saved_today()
    {
        await using QuoteSnapApiFactory factory = new() { SlackConfigured = true };
        QuoteSnapClient client = factory.CreateQuoteSnapClient();

        SlackStatus initial = await client.GetSlackAsync();
        Assert.Equal((true, false, 10), (initial.Configured, initial.Enabled, initial.IntervalMinutes));
        Assert.InRange(initial.NextReportAt!.Value, DateTimeOffset.UtcNow.AddMinutes(9), DateTimeOffset.UtcNow.AddMinutes(10));
        Assert.True((await client.SetSlackAsync(true)).Enabled);
        Assert.True((await client.GetSlackAsync()).Enabled);
        Assert.False((await client.SetSlackAsync(false)).Enabled);

        Assert.Equal(SlackEndpoints.NoEstimatesText, (await client.SendSlackTestAsync()).Text);
        await SeedAsync(factory);
        string text = (await client.SendSlackTestAsync()).Text;
        Assert.Equal([SlackEndpoints.NoEstimatesText, text], factory.SlackMessages);
        Assert.Equal(["C0TEST/1700000000.000001"], factory.SlackDeletes);
        Assert.StartsWith("📊 QuoteSnap NAV estimates (prices at ", text);
        Assert.EndsWith("\nMotilal Oswal Nifty India Defence Index Fund: -1.50%\nDSP Banking &amp; Financial Services Fund: +0.42%\nParag Parikh Flexi Cap Fund: no estimate", text);

        using (System.Text.Json.JsonDocument payload = System.Text.Json.JsonDocument.Parse(factory.SlackPayloads.Last()))
        {
            System.Text.Json.JsonElement[] blocks = [.. payload.RootElement.GetProperty("blocks").EnumerateArray()];
            Assert.Equal(["header", "context", "divider", "section", "divider", "context"], blocks.Select(b => b.GetProperty("type").GetString()));
            Assert.Equal("🔴  ` -1.50%`  Motilal Oswal Nifty India Defence Index Fund\n🟢  ` +0.42%`  DSP Banking &amp; Financial Services Fund\n⚪  `no estimate`  Parag Parikh Flexi Cap Fund", blocks[3].GetProperty("text").GetProperty("text").GetString());
        }

        factory.SlackError = "invalid_auth";
        QuoteSnapApiException failed = await Assert.ThrowsAsync<QuoteSnapApiException>(() => client.SendSlackTestAsync());
        Assert.Equal(HttpStatusCode.BadGateway, failed.StatusCode);
    }

    [Fact]
    public async Task Reports_are_posted_only_when_on_while_the_market_is_open_and_estimates_are_saved()
    {
        await using QuoteSnapApiFactory factory = new() { SlackConfigured = true };
        QuoteSnapClient client = factory.CreateQuoteSnapClient();
        SlackReporter reporter = factory.Services.GetRequiredService<SlackReporter>();
        await factory.SeedSessionAsync();

        Assert.False(await reporter.ReportAsync(default));
        await client.SetSlackAsync(true);
        Assert.False(await reporter.ReportAsync(default));
        Assert.Empty(factory.SlackMessages);
        Assert.Matches(@"^Skipped at \d\d:\d\d: no estimate saved today yet\.$", (await client.GetSlackAsync()).LastResult);

        await SeedAsync(factory);
        Assert.True(await reporter.ReportAsync(default));
        Assert.Contains("Parag Parikh Flexi Cap Fund: no estimate", Assert.Single(factory.SlackMessages));
        Assert.StartsWith("Posted at ", (await client.GetSlackAsync()).LastResult);

        factory.SlackError = "not_in_channel";
        Assert.False(await reporter.ReportAsync(default));
        Assert.EndsWith(": not_in_channel", (await client.GetSlackAsync()).LastResult);
        Assert.Null((await client.SetSlackAsync(false)).LastResult);
    }

    [Fact]
    public async Task Latest_posts_the_saved_estimates_in_the_channel_whatever_the_switch_or_market()
    {
        await using QuoteSnapApiFactory factory = new() { SlackConfigured = true };
        SlackCommands commands = factory.Services.GetRequiredService<SlackCommands>();
        factory.QuoteProvider.MarketStatus = "CLOSED";

        Assert.Equal("QuoteSnap posts in <#C0TEST>; use the command there.", commands.Reject("C0OTHER"));
        Assert.Null(commands.Reject(QuoteSnapApiFactory.SlackChannel));
        Assert.Equal(SlackCommands.NoEstimatesText, await commands.PostLatestAsync(default));
        Assert.Empty(factory.SlackMessages);

        await SeedAsync(factory);
        Assert.Null(await commands.PostLatestAsync(default));
        Assert.Null(await commands.PostLatestAsync(default));
        Assert.Equal(2, factory.SlackMessages.Count);
        Assert.Equal(["C0TEST/1700000000.000001"], factory.SlackDeletes);

        factory.SlackError = "not_in_channel";
        Assert.Equal("Couldn't post the NAV estimates: not_in_channel", await commands.PostLatestAsync(default));
    }

    [Fact]
    public async Task Nothing_is_posted_while_the_market_is_closed()
    {
        await using QuoteSnapApiFactory factory = new() { SlackConfigured = true };
        QuoteSnapClient client = factory.CreateQuoteSnapClient();
        await factory.SeedSessionAsync();
        await SeedAsync(factory);
        await client.SetSlackAsync(true);
        factory.QuoteProvider.MarketStatus = "CLOSED";

        Assert.False(await factory.Services.GetRequiredService<SlackReporter>().ReportAsync(default));
        Assert.Empty(factory.SlackMessages);
        Assert.EndsWith("NSE isn't trading (CLOSED).", (await client.GetSlackAsync()).LastResult);
    }

    /// <summary>Three listed funds, with estimates saved today for the first two.</summary>
    private static async Task SeedAsync(QuoteSnapApiFactory factory)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        QuoteSnapDbContext db = scope.ServiceProvider.GetRequiredService<QuoteSnapDbContext>();
        DateOnly today = scope.ServiceProvider.GetRequiredService<NavEstimateRepository>().Today;
        DateTimeOffset now = DateTimeOffset.UtcNow;
        db.MyFunds.AddRange(
            new MyFundRecord { SchemeCode = 152045, SchemeName = "DSP Banking & Financial Services Fund", AddedAt = now, Position = 0 },
            new MyFundRecord { SchemeCode = 152712, SchemeName = "Motilal Oswal Nifty India Defence Index Fund", AddedAt = now, Position = 1 },
            new MyFundRecord { SchemeCode = 122639, SchemeName = "Parag Parikh Flexi Cap Fund", AddedAt = now, Position = 2 });
        db.NavEstimates.AddRange(
            new NavEstimateRecord { Day = today, SchemeCode = 152045, PricesAt = now, ChangePercent = 0.4204m, Position = 0, SavedAt = now },
            new NavEstimateRecord { Day = today, SchemeCode = 152712, PricesAt = now, ChangePercent = -1.5m, Position = 1, SavedAt = now });
        await db.SaveChangesAsync();
    }
}
