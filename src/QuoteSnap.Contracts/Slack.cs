namespace QuoteSnap.Contracts;

/// <param name="Configured">Whether the API has a Slack bot token and channel; the reports can't be turned on without them.</param>
/// <param name="Enabled">Whether the API posts the saved NAV estimates to Slack while NSE is trading.</param>
/// <param name="IntervalMinutes">How often it posts them.</param>
/// <param name="NextReportAt">When the API next checks and posts; null without them.</param>
/// <param name="LastCheckedAt">When it last checked, while the reports were on; null since it started otherwise.</param>
/// <param name="LastResult">What that check did, e.g. posted, or skipped because NSE wasn't trading.</param>
public sealed record SlackStatus(bool Configured, bool Enabled, int IntervalMinutes, DateTimeOffset? NextReportAt = null, DateTimeOffset? LastCheckedAt = null, string? LastResult = null);

public sealed record SetSlackReports(bool Enabled);

/// <param name="Text">The message that was posted, replacing the previous post.</param>
public sealed record SlackTestResult(string Text);
