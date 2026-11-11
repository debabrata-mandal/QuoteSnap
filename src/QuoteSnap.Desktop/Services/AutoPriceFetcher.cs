using QuoteSnap.Contracts;

namespace QuoteSnap.Desktop.Services;

/// <summary>
/// While on, asks the API every 15 minutes whether NSE is trading (Fyers' market status) and, when it is, requests live
/// prices for every stock the listed funds hold. It only runs while the app is open.
/// </summary>
public sealed class AutoPriceFetcher(QuoteSnapConnection connection)
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(15);

    private CancellationTokenSource? _running;

    /// <summary>After every check, with what it did.</summary>
    public event Action<string>? Checked;

    /// <summary>After a live-prices request for the holdings was started or found running.</summary>
    public event Action? PricesRequested;

    public bool IsEnabled => _running is not null;

    /// <summary>Turning it on checks straight away, then every <see cref="Interval"/>.</summary>
    public void SetEnabled(bool enabled)
    {
        if (enabled == IsEnabled)
            return;

        if (enabled)
        {
            _running = new CancellationTokenSource();
            _ = RunAsync(_running.Token);
        }
        else
        {
            _running!.Cancel();
            _running.Dispose();
            _running = null;
        }
    }

    private async Task RunAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (true)
            {
                await CheckAsync(cancellationToken);
                await Task.Delay(Interval, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task CheckAsync(CancellationToken cancellationToken)
    {
        DateTimeOffset now = DateTimeOffset.Now;
        string next = $"next check at {now + Interval:HH:mm}";
        try
        {
            MarketStatus market = await connection.Client.GetMarketStatusAsync(cancellationToken);
            if (!market.IsOpen)
            {
                Checked?.Invoke($"NSE wasn't trading at {now:HH:mm} ({market.Status ?? "no status from Fyers"}); {next}.");
                return;
            }

            await connection.Client.StartHoldingQuotesAsync(cancellationToken);
            Checked?.Invoke($"Requested live prices at {now:HH:mm}; {next}.");
            PricesRequested?.Invoke();
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            Checked?.Invoke($"Couldn't fetch live prices at {now:HH:mm}. {ErrorText.Describe(ex, connection.Settings.BaseAddress)} Trying again at {now + Interval:HH:mm}.");
        }
    }
}
