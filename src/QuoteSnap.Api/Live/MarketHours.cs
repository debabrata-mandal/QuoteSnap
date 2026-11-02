namespace QuoteSnap.Api.Live;

/// <summary>Whether the exchange is trading. Exchange holidays aren't known, so they count as open.</summary>
public static class MarketHours
{
    public static bool IsOpen(DateTimeOffset now, LiveQuotesOptions options)
    {
        DateTime local = TimeZoneInfo.ConvertTime(now, TimeZoneInfo.FindSystemTimeZoneById(options.TimeZone)).DateTime;
        if (local.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday)
            return false;
        TimeOnly time = TimeOnly.FromDateTime(local);
        return time >= options.MarketOpen && time < options.MarketClose;
    }
}
