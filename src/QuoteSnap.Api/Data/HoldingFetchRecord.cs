using QuoteSnap.Contracts;

namespace QuoteSnap.Api.Data;

/// <summary>A run that fetches the holdings of every fund that was on the list when it was started.</summary>
public sealed class HoldingFetchRecord
{
    public Guid Id { get; set; }

    public HoldingFetchStatus Status { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    public string? Error { get; set; }

    public List<HoldingFetchFundRecord> Funds { get; } = [];
}

/// <summary>One fund's progress in a <see cref="HoldingFetchRecord"/>.</summary>
public sealed class HoldingFetchFundRecord
{
    public Guid FetchId { get; set; }

    public int SchemeCode { get; set; }

    public int Position { get; set; }

    public required string SchemeName { get; set; }

    public HoldingFetchFundStatus Status { get; set; }

    public string? ScanxScheme { get; set; }

    public int? HoldingCount { get; set; }

    public DateOnly? Month { get; set; }

    public string? Error { get; set; }
}
