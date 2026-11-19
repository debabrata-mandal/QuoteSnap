using System.Net;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using QuoteSnap.Api.Data;
using QuoteSnap.Api.Domain;
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
        await SeedAsync(factory);

        Assert.False(await reporter.ReportAsync(default));
        Assert.Empty(factory.SlackMessages);
        await client.SetSlackAsync(true);
        Assert.True(await reporter.ReportAsync(default));
        Assert.Contains("Parag Parikh Flexi Cap Fund: no estimate", Assert.Single(factory.SlackMessages));
        Assert.StartsWith("Posted at ", (await client.GetSlackAsync()).LastResult);

        factory.SlackError = "not_in_channel";
        Assert.False(await reporter.ReportAsync(default));
        Assert.EndsWith("Fix: type `/invite @QuoteSnap` in <#C0TEST>.", (await client.GetSlackAsync()).LastResult);
        Assert.Null((await client.SetSlackAsync(false)).LastResult);
    }

    [Fact]
    public async Task A_problem_is_posted_once_beside_the_last_estimates_and_deleted_once_they_post_again()
    {
        await using QuoteSnapApiFactory factory = new() { SlackConfigured = true };
        QuoteSnapClient client = factory.CreateQuoteSnapClient();
        SlackReporter reporter = factory.Services.GetRequiredService<SlackReporter>();
        await factory.SeedSessionAsync();
        await SeedAsync(factory, navsLoadedAt: DateTimeOffset.UtcNow.AddDays(-1));
        await client.SetSlackAsync(true);

        Assert.False(await reporter.ReportAsync(default));
        Assert.False(await reporter.ReportAsync(default));
        string problem = Assert.Single(factory.SlackMessages);
        Assert.StartsWith("*QuoteSnap couldn't post the NAV estimates*\n⚠️ NAVs were last downloaded on ", problem);
        Assert.EndsWith("*Fix:* refresh NAVs on the desktop app's Mutual funds page.", problem);
        Assert.Contains("Fix: refresh NAVs", (await client.GetSlackAsync()).LastResult);

        await using (AsyncServiceScope scope = factory.Services.CreateAsyncScope())
            await scope.ServiceProvider.GetRequiredService<QuoteSnapDbContext>().MutualFundNavs.ExecuteUpdateAsync(s => s.SetProperty(n => n.LoadedAt, DateTimeOffset.UtcNow));
        Assert.True(await reporter.ReportAsync(default));
        Assert.Equal(2, factory.SlackMessages.Count);
        Assert.Equal(["C0TEST/1700000000.000001"], factory.SlackDeletes);
    }

    [Fact]
    public async Task Latest_posts_the_last_saved_day_while_the_market_is_closed_and_otherwise_says_how_to_fix_it()
    {
        await using QuoteSnapApiFactory factory = new() { SlackConfigured = true };
        SlackCommands commands = factory.Services.GetRequiredService<SlackCommands>();
        factory.QuoteProvider.MarketStatus = "CLOSED";

        Assert.Equal("QuoteSnap posts in <#C0TEST>; use the command there.", commands.Reject("C0OTHER"));
        Assert.Null(commands.Reject(QuoteSnapApiFactory.SlackChannel));
        Assert.Equal(SlackCommands.Reply(SlackProblem.FyersLogin), await commands.PostLatestAsync(default));
        await factory.SeedSessionAsync();
        Assert.EndsWith("*Fix:* add them on the desktop app's Mutual funds page.", await commands.PostLatestAsync(default));
        Assert.Empty(factory.SlackMessages);

        await SeedAsync(factory, estimatesDaysAgo: 3);
        Assert.Null(await commands.PostLatestAsync(default));
        Assert.Null(await commands.PostLatestAsync(default));
        Assert.Equal(2, factory.SlackMessages.Count);
        Assert.StartsWith("📊 QuoteSnap NAV estimates (NSE closed · prices at ", factory.SlackMessages.First());
        Assert.Equal(["C0TEST/1700000000.000001"], factory.SlackDeletes);

        factory.SlackError = "not_in_channel";
        Assert.EndsWith("*Fix:* type `/invite @QuoteSnap` in <#C0TEST>.", await commands.PostLatestAsync(default));
    }

    [Fact]
    public async Task Latest_turns_down_estimates_over_15_minutes_old_while_the_market_is_open()
    {
        await using QuoteSnapApiFactory factory = new() { SlackConfigured = true };
        await factory.SeedSessionAsync();
        await SeedAsync(factory, savedAt: DateTimeOffset.UtcNow.AddMinutes(-20));

        string? reply = await factory.Services.GetRequiredService<SlackCommands>().PostLatestAsync(default);

        Assert.Matches(@"^Couldn't post the NAV estimates\.\n⚠️ The latest NAV estimates were saved at \d\d:\d\d, over 15 minutes ago\. None of your funds has stock holdings with a Fyers symbol", reply);
        Assert.EndsWith("*Fix:* fetch the holdings on the desktop app's Holdings page.", reply);
        Assert.Empty(factory.SlackMessages);
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

    /// <summary>Three listed funds with NAVs, and estimates for the first two.</summary>
    private static async Task SeedAsync(QuoteSnapApiFactory factory, DateTimeOffset? navsLoadedAt = null, DateTimeOffset? savedAt = null, int estimatesDaysAgo = 0)
    {
        await using AsyncServiceScope scope = factory.Services.CreateAsyncScope();
        QuoteSnapDbContext db = scope.ServiceProvider.GetRequiredService<QuoteSnapDbContext>();
        DateOnly day = scope.ServiceProvider.GetRequiredService<NavEstimateRepository>().Today.AddDays(-estimatesDaysAgo);
        DateTimeOffset now = DateTimeOffset.UtcNow;
        db.MyFunds.AddRange(
            new MyFundRecord { SchemeCode = 152045, SchemeName = "DSP Banking & Financial Services Fund", AddedAt = now, Position = 0 },
            new MyFundRecord { SchemeCode = 152712, SchemeName = "Motilal Oswal Nifty India Defence Index Fund", AddedAt = now, Position = 1 },
            new MyFundRecord { SchemeCode = 122639, SchemeName = "Parag Parikh Flexi Cap Fund", AddedAt = now, Position = 2 });
        db.MutualFundNavs.AddRange(new[] { 152045, 152712, 122639 }.Select(code => new MutualFundNav { SchemeCode = code, SchemeName = $"Fund {code}", Nav = 100, NavDate = DateOnly.FromDateTime(now.Date), LoadedAt = navsLoadedAt ?? now }));
        db.NavEstimates.AddRange(
            new NavEstimateRecord { Day = day, SchemeCode = 152045, PricesAt = now, ChangePercent = 0.4204m, Position = 0, SavedAt = savedAt ?? now },
            new NavEstimateRecord { Day = day, SchemeCode = 152712, PricesAt = now, ChangePercent = -1.5m, Position = 1, SavedAt = savedAt ?? now });
        await db.SaveChangesAsync();
    }
}