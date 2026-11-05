using QuoteSnap.Contracts;

namespace QuoteSnap.Desktop.Services;

/// <summary>
/// The holdings symbols the user left out of today's live prices, e.g. suspended stocks Fyers has no price for. The
/// service keeps the list, so it holds across restarts, and skips them in the holdings' price fetches and live feed,
/// where the next holdings by weight take their places. Tomorrow they're back.
/// </summary>
public sealed class SymbolExclusions(QuoteSnapConnection connection)
{
    private ExcludedSymbolList? _list;

    /// <summary>Raised when the list is loaded or changed.</summary>
    public event EventHandler? Changed;

    /// <summary>Today's excluded symbols, as last loaded.</summary>
    public IReadOnlyList<string> Today => _list?.Symbols ?? [];

    /// <summary>When a symbol was last excluded; errors in requests made before then have been dealt with.</summary>
    public DateTimeOffset? ExcludedAt => _list?.ExcludedAt;

    public bool Contains(string symbol) => Today.Contains(symbol, StringComparer.OrdinalIgnoreCase);

    /// <summary>Reloads the list; when the service can't be reached, the last one is kept.</summary>
    public async Task RefreshAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            Set(await connection.Client.GetExcludedSymbolsAsync(cancellationToken));
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
        }
    }

    public async Task ExcludeAsync(IEnumerable<string> symbols) => Set(await connection.Client.SetExcludedSymbolsAsync([.. Today, .. symbols]));

    public async Task IncludeAsync(IEnumerable<string> symbols)
    {
        HashSet<string> included = new(symbols, StringComparer.OrdinalIgnoreCase);
        Set(await connection.Client.SetExcludedSymbolsAsync(Today.Where(s => !included.Contains(s))));
    }

    private void Set(ExcludedSymbolList list)
    {
        _list = list;
        Changed?.Invoke(this, EventArgs.Empty);
    }
}
