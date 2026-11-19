using System.Globalization;
using System.Text;
using System.Text.Json.Nodes;
using QuoteSnap.Api.Data;

namespace QuoteSnap.Api.Slack;

/// <summary>
/// The Slack message: each listed fund's saved estimated change today, from the biggest loser to the biggest gainer, with
/// funds without an estimate last. Prices aren't fetched for it.
/// </summary>
public static class SlackReport
{
    public const string Title = "📊 QuoteSnap NAV estimates";

    // Slack rejects section text over 3,000 characters, so long lists are split over several sections.
    private const int MaxSectionLength = 2900;

    /// <returns>Null when no estimates have been saved today.</returns>
    public static async Task<SlackMessage?> BuildAsync(MyFundRepository funds, NavEstimateRepository estimates, TimeProvider time, CancellationToken cancellationToken)
    {
        List<NavEstimateRecord> today = await estimates.GetTodayAsync(cancellationToken);
        if (today.Count == 0)
            return null;

        Dictionary<int, NavEstimateRecord> bySchemeCode = today.ToDictionary(e => e.SchemeCode);
        List<(string Name, decimal? Change)> rows = (await funds.GetAllAsync(cancellationToken))
            .Select(f => (Name: f.SchemeName, Change: bySchemeCode.GetValueOrDefault(f.SchemeCode)?.ChangePercent))
            .OrderBy(r => r.Change is null)
            .ThenBy(r => r.Change)
            .ToList();

        DateTimeOffset? pricesAt = today[0].PricesAt is { } p ? TimeZoneInfo.ConvertTime(p, time.LocalTimeZone) : null;
        string at = pricesAt is { } local ? $"Prices at *{local.ToString("HH:mm", CultureInfo.InvariantCulture)}* · {local.ToString("ddd, d MMM", CultureInfo.InvariantCulture)}" : "Prices at an unknown time";

        StringBuilder text = new($"{Title}{(pricesAt is { } t ? $" (prices at {t.ToString("HH:mm", CultureInfo.InvariantCulture)})" : "")}");
        foreach ((string name, decimal? change) in rows)
            text.Append('\n').Append(SlackClient.Escape(name)).Append(": ").Append(Format(change));

        JsonArray blocks =
        [
            new JsonObject { ["type"] = "header", ["text"] = new JsonObject { ["type"] = "plain_text", ["text"] = Title, ["emoji"] = true } },
            Context(at),
            new JsonObject { ["type"] = "divider" },
        ];
        StringBuilder section = new();
        foreach ((string name, decimal? change) in rows)
        {
            string line = $"{Marker(change)}  `{Format(change).PadLeft(7)}`  {SlackClient.Escape(name)}";
            if (section.Length > 0 && section.Length + 1 + line.Length > MaxSectionLength)
            {
                blocks.Add(Section(section.ToString()));
                section.Clear();
            }

            section.Append(section.Length > 0 ? "\n" : "").Append(line);
        }

        if (section.Length > 0)
            blocks.Add(Section(section.ToString()));
        blocks.Add(new JsonObject { ["type"] = "divider" });
        blocks.Add(Context("Estimated from each fund's latest holdings · posted by QuoteSnap"));
        return new SlackMessage(text.ToString(), blocks);
    }

    public static string Format(decimal? changePercent) => changePercent is { } c ? c.ToString("+0.00'%';-0.00'%';0.00'%'", CultureInfo.InvariantCulture) : "no estimate";

    private static string Marker(decimal? changePercent) => changePercent switch
    {
        null => "⚪",
        > 0 => "🟢",
        < 0 => "🔴",
        _ => "⚪",
    };

    private static JsonObject Section(string markdown) => new() { ["type"] = "section", ["text"] = new JsonObject { ["type"] = "mrkdwn", ["text"] = markdown } };

    private static JsonObject Context(string markdown) => new() { ["type"] = "context", ["elements"] = new JsonArray(new JsonObject { ["type"] = "mrkdwn", ["text"] = markdown }) };
}
