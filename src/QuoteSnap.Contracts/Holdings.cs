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
public sealed record HoldingFetch(Guid Id, HoldingFetchStatus Status, DateTimeOffset CreatedAt, DateTimeOffset? StartedAt, DateTimeOffset? CompletedAt, int TotalFunds, int FinishedFunds, string? Error, IReadOnlyList<HoldingFetchFund> Funds);

/// <param name="ScanxScheme">The ScanX scheme the fund was matched to, e.g. "DSP Banking &amp; Financial Services Fund Direct-Growth".</param>
/// <param name="Month">The latest month of the holdings that were stored.</param>
public sealed record HoldingFetchFund(int SchemeCode, string SchemeName, HoldingFetchFundStatus Status, string? ScanxScheme, int? HoldingCount, DateOnly? Month, string? Error);

/// <summary>What's stored for a fund on the list; <see cref="MyFund.Holdings"/>.</summary>
/// <param name="Month">The latest month with holdings, as the first day of that month.</param>
/// <param name="Count">Holdings in <paramref name="Month"/>.</param>
public sealed record HoldingsSummary(string ScanxScheme, DateOnly Month, int Count, DateTimeOffset FetchedAt);

/// <summary>A fund's portfolio for one month.</summary>
/// <param name="Month">The first day of the month shown.</param>
/// <param name="Months">Every month with stored holdings, newest first.</param>
/// <param name="Holdings">Largest weight first.</param>
public sealed record FundHoldings(int SchemeCode, string SchemeName, string ScanxScheme, DateOnly Month, IReadOnlyList<DateOnly> Months, DateTimeOffset FetchedAt, IReadOnlyList<FundHolding> Holdings);

/// <param name="Symbol">The Fyers symbol for <paramref name="Isin"/>, e.g. "NSE:HDFCBANK-EQ", or "BSE:…" when it isn't on NSE; ready for a quote request. Null for unlisted lines such as TREPS.</param>
/// <param name="AssetType">ScanX's grouping, e.g. "Equity", "Futures", "TREPS", "Certificate of Deposit".</param>
/// <param name="Percent">Share of the fund's assets, in percent; negative for short positions such as futures.</param>
/// <param name="PreviousPercent">The weight a month earlier; null when it wasn't held then or that month isn't stored.</param>
public sealed record FundHolding(string Name, string? Symbol, string? Isin, string AssetType, string? Sector, decimal Percent, decimal? Shares, decimal? PreviousPercent);

/// <summary>The stocks the listed funds hold most, as if the same amount were invested in each fund that has holdings.</summary>
/// <param name="FundCount">Listed funds with stored holdings; each stock's weight is averaged over these.</param>
/// <param name="RequestId">The finished holdings quote request the prices come from; null when none has finished.</param>
/// <param name="PricesAt">When that request finished.</param>
/// <param name="Holdings">Largest weight first.</param>
public sealed record TopHoldingList(int FundCount, Guid? RequestId, DateTimeOffset? PricesAt, IReadOnlyList<TopHolding> Holdings);

/// <summary>The holdings symbols left out of live prices today: they aren't fetched or streamed, so the next holdings by weight are streamed instead.</summary>
/// <param name="Day">The server's local date they're excluded on; tomorrow they're back.</param>
/// <param name="ExcludedAt">When a symbol was last added; null when none is excluded.</param>
public sealed record ExcludedSymbolList(DateOnly Day, DateTimeOffset? ExcludedAt, IReadOnlyList<string> Symbols);

/// <summary>The body of PUT /api/holdings/exclusions: today's whole list of excluded symbols.</summary>
public sealed record SetExcludedSymbols(IReadOnlyList<string> Symbols);

/// <param name="Percent">The stock's weight averaged over every fund with holdings, counting funds that don't hold it as 0%.</param>
/// <param name="FundCount">How many of the funds hold it.</param>
/// <param name="ChangePercent">The day's change in percent from the last live prices; null when it has no price.</param>
public sealed record TopHolding(string Symbol, string Name, string? Sector, decimal Percent, int FundCount, decimal? Ltp, decimal? ChangePercent);
