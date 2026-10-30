using System.Text.Json.Serialization;

namespace QuoteSnap.Contracts;

[JsonConverter(typeof(JsonStringEnumConverter<HoldingFetchStatus>))]
public enum HoldingFetchStatus
{
    Queued,
    Running,
    Completed,
    CompletedWithErrors,
    Failed,
}

[JsonConverter(typeof(JsonStringEnumConverter<HoldingFetchFundStatus>))]
public enum HoldingFetchFundStatus
{
    Pending,
    Running,
    Ok,

    /// <summary>ScanX has no scheme that matches the fund's name.</summary>
    NotFound,
    Error,
}

public static class HoldingFetchStatusExtensions
{
    public static bool IsTerminal(this HoldingFetchStatus status) =>
        status is HoldingFetchStatus.Completed or HoldingFetchStatus.CompletedWithErrors or HoldingFetchStatus.Failed;

    public static bool IsFinished(this HoldingFetchFundStatus status) =>
        status is HoldingFetchFundStatus.Ok or HoldingFetchFundStatus.NotFound or HoldingFetchFundStatus.Error;
}

/// <summary>One run that scrapes the holdings of every fund on the list from ScanX, a fund at a time.</summary>
/// <param name="Funds">In list order, each with its own progress.</param>
public sealed record HoldingFetch(
    Guid Id,
    HoldingFetchStatus Status,
    DateTimeOffset CreatedAt,
    DateTimeOffset? StartedAt,
    DateTimeOffset? CompletedAt,
    int TotalFunds,
    int FinishedFunds,
    string? Error,
    IReadOnlyList<HoldingFetchFund> Funds);

/// <param name="ScanxScheme">The ScanX scheme the fund was matched to, e.g. "DSP Banking &amp; Financial Services Fund Direct-Growth".</param>
/// <param name="Month">The latest month of the holdings that were stored.</param>
public sealed record HoldingFetchFund(
    int SchemeCode,
    string SchemeName,
    HoldingFetchFundStatus Status,
    string? ScanxScheme,
    int? HoldingCount,
    DateOnly? Month,
    string? Error);

/// <summary>What's stored for a fund on the list; <see cref="MyFund.Holdings"/>.</summary>
/// <param name="Month">The latest month with holdings, as the first day of that month.</param>
/// <param name="Count">Holdings in <paramref name="Month"/>.</param>
public sealed record HoldingsSummary(string ScanxScheme, DateOnly Month, int Count, DateTimeOffset FetchedAt);

/// <summary>A fund's portfolio for one month.</summary>
/// <param name="Month">The first day of the month shown.</param>
/// <param name="Months">Every month with stored holdings, newest first.</param>
/// <param name="Holdings">Largest weight first.</param>
public sealed record FundHoldings(
    int SchemeCode,
    string SchemeName,
    string ScanxScheme,
    DateOnly Month,
    IReadOnlyList<DateOnly> Months,
    DateTimeOffset FetchedAt,
    IReadOnlyList<FundHolding> Holdings);

/// <param name="AssetType">ScanX's grouping, e.g. "Equity", "Futures", "TREPS", "Certificate of Deposit".</param>
/// <param name="Percent">Share of the fund's assets, in percent; negative for short positions such as futures.</param>
/// <param name="PreviousPercent">The weight a month earlier; null when it wasn't held then or that month isn't stored.</param>
public sealed record FundHolding(
    string Name,
    string? Symbol,
    string? Isin,
    string AssetType,
    string? Sector,
    decimal Percent,
    decimal? Shares,
    decimal? PreviousPercent);
