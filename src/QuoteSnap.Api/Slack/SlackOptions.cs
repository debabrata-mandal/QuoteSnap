using System.ComponentModel.DataAnnotations;

namespace QuoteSnap.Api.Slack;

/// <summary>Posting the saved NAV estimates to a Slack channel while NSE is trading, keeping only the latest post.</summary>
public sealed class SlackOptions
{
    public const string SectionName = "Slack";

    /// <summary>The Slack app's bot token (xoxb-...), with the chat:write scope; a secret. Empty turns the reports off.</summary>
    public string? BotToken { get; set; }

    /// <summary>The channel's ID (e.g. C0123456789, from the channel's details), which the bot must be a member of.</summary>
    public string? ChannelId { get; set; }

    /// <summary>The Slack app's app-level token (xapp-...), with the connections:write scope; a secret. With it, the API answers /latest over Socket Mode.</summary>
    public string? AppToken { get; set; }

    /// <summary>The Slack Web API.</summary>
    public Uri ApiBaseUrl { get; set; } = new("https://slack.com/api/");

    /// <summary>How often the estimates are posted while the market is open.</summary>
    [Range(1, 1440)]
    public int IntervalMinutes { get; set; } = 10;

    public bool IsConfigured => BotToken?.StartsWith("xoxb-", StringComparison.Ordinal) == true && !string.IsNullOrWhiteSpace(ChannelId);

    public bool CommandsConfigured => IsConfigured && AppToken?.StartsWith("xapp-", StringComparison.Ordinal) == true;
}
