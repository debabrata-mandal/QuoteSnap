using System.Collections;

namespace QuoteSnap.Desktop.ViewModels;

/// <summary>How the My funds table is ordered.</summary>
public enum FundOrder
{
    /// <summary>The order the funds were added in, as the service returns them.</summary>
    Original,
    HighestEstimateFirst,
    LowestEstimateFirst,
}

/// <summary>Orders fund rows by today's estimated change; funds without an estimate go last, and ties keep the original order.</summary>
public sealed class FundEstimateComparer(IList<FundRow> original, bool highestFirst) : IComparer
{
    public int Compare(object? x, object? y)
    {
        FundRow a = (FundRow)x!;
        FundRow b = (FundRow)y!;
        int byEstimate = (a.EstimateChangePercent, b.EstimateChangePercent) switch
        {
            (null, null) => 0,
            (null, _) => 1,
            (_, null) => -1,
            ({ } p, { } q) => highestFirst ? q.CompareTo(p) : p.CompareTo(q),
        };
        return byEstimate != 0 ? byEstimate : original.IndexOf(a).CompareTo(original.IndexOf(b));
    }
}
