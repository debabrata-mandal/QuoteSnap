using QuoteSnap.Client;
using QuoteSnap.Contracts;

namespace QuoteSnap.Desktop.Services;

/// <summary>
/// While on, asks the API every 5 minutes whether NSE is trading (Fyers' market status) and, when it is, requests live
/// prices for every stock the listed funds hold. It only runs while the app is open.
/// </summary>
public sealed class AutoPriceFetcher
{
    public static readonly TimeSpan Interval = TimeSpan.FromMinutes(5);
    internal static readonly TimeSpan AttemptTimeout = TimeSpan.FromMinutes(2);

    /// <summary>Reuses a holdings fetch another client started this recently, so several open apps share one Fyers fetch.</summary>
    internal static readonly TimeSpan ReuseWithin = Interval - TimeSpan.FromMinutes(1);

    private readonly Func<QuoteSnapClient> _client;
    private readonly Func<Uri> _baseAddress;
    private readonly TimeProvider _time;
    private CancellationTokenSource? _running;

    public AutoPriceFetcher(QuoteSnapConnection connection) : this(() => connection.Client, () => connection.Settings.BaseAddress, TimeProvider.System)
    {
    }

    internal AutoPriceFetcher(Func<QuoteSnapClient> client, Func<Uri> baseAddress, TimeProvider time)
    {
        _client = client;
        _baseAddress = baseAddress;
        _time = time;
    }

    /// <summary>The last check's result and next-check countdown, updated every second while enabled.</summary>
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
        Task<string>? check = null;
        DateTimeOffset next = _time.GetUtcNow();
        string result = "Checking live prices.";
        try
        {
            while (true)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (check is { IsCompleted: true })
                {
                    result = await check;
                    check = null;
                }

                DateTimeOffset now = _time.GetUtcNow();
                if (check is null && now >= next)
                {
                    next = now + Interval;
                    result = "Checking live prices.";
                    check = CheckAsync(cancellationToken);
                    if (check.IsCompleted)
                    {
                        result = await check;
                        check = null;
                    }
                }

                cancellationToken.ThrowIfCancellationRequested();
                TimeSpan remaining = next > now ? next - now : TimeSpan.Zero;
                Checked?.Invoke($"{result} Next check in {remaining:mm\\:ss} (at {TimeZoneInfo.ConvertTime(next, _time.LocalTimeZone):HH:mm:ss}).");
                await Task.Delay(TimeSpan.FromSeconds(1), _time, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }

    private async Task<string> CheckAsync(CancellationToken cancellationToken)
    {
        using CancellationTokenSource attempt = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        try
        {
            return await RequestAsync(attempt.Token).WaitAsync(AttemptTimeout, _time, cancellationToken);
        }
        catch (TimeoutException)
        {
            return "Couldn't fetch live prices: the check timed out after 2 minutes.";
        }
        catch (Exception ex) when (!cancellationToken.IsCancellationRequested)
        {
            return $"Couldn't fetch live prices. {ErrorText.Describe(ex, _baseAddress())}";
        }
        finally
        {
            await attempt.CancelAsync();
        }
    }

    private async Task<string> RequestAsync(CancellationToken cancellationToken)
    {
        QuoteSnapClient client = _client();
        MarketStatus market = await client.GetMarketStatusAsync(cancellationToken);
        if (!market.IsOpen)
            return $"NSE isn't trading ({market.Status ?? "no status from Fyers"}).";

        cancellationToken.ThrowIfCancellationRequested();
        await client.StartHoldingQuotesAsync(ReuseWithin, cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();
        PricesRequested?.Invoke();
        return $"Requested live prices at {_time.GetLocalNow():HH:mm}.";
    }
}
