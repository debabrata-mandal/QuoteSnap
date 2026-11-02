using QuoteSnap.Contracts;

namespace QuoteSnap.Api.Live;

/// <summary>The latest tick of each streamed symbol and the socket's state, kept in memory.</summary>
public sealed class LiveQuoteStore(TimeProvider time)
{
    private readonly Lock _lock = new();
    private readonly Dictionary<string, LiveQuote> _quotes = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<string, long> _versions = new(StringComparer.OrdinalIgnoreCase);
    private long _version;
    private List<string> _symbols = [];
    private HashSet<string> _symbolSet = new(StringComparer.OrdinalIgnoreCase);
    private LiveFeedState _state = LiveFeedState.Starting;
    private DateTimeOffset? _connectedAt;
    private DateTimeOffset? _lastTickAt;
    private string? _lastError;
    private DateTimeOffset? _lastErrorAt;

    /// <returns>Whether the state changed.</returns>
    public bool SetState(LiveFeedState state)
    {
        lock (_lock)
        {
            if (state != LiveFeedState.Connected)
                _connectedAt = null;
            bool changed = _state != state;
            _state = state;
            return changed;
        }
    }

    public void SetConnected()
    {
        lock (_lock)
        {
            _state = LiveFeedState.Connected;
            _connectedAt = time.GetUtcNow();
        }
    }

    public void SetError(string message)
    {
        lock (_lock)
        {
            _lastError = message;
            _lastErrorAt = time.GetUtcNow();
        }
    }

    /// <summary>The symbols being streamed, largest weight first. Quotes of any others are dropped, and their late ticks ignored.</summary>
    public void SetSymbols(IReadOnlyList<string> symbols)
    {
        lock (_lock)
        {
            _symbols = [.. symbols];
            _symbolSet = new HashSet<string>(symbols, StringComparer.OrdinalIgnoreCase);
            foreach (string symbol in _quotes.Keys.Where(s => !_symbolSet.Contains(s)).ToList())
            {
                _quotes.Remove(symbol);
                _versions.Remove(symbol);
            }
        }
    }

    /// <summary>Keeps the previous values of whatever the tick left out.</summary>
    public void Record(LiveTick tick)
    {
        lock (_lock)
        {
            if (!_symbolSet.Contains(tick.Symbol))
                return;
            LiveQuote? previous = _quotes.GetValueOrDefault(tick.Symbol);
            if ((tick.Ltp ?? previous?.Ltp) is not { } ltp)
                return;
            DateTimeOffset now = time.GetUtcNow();
            _quotes[tick.Symbol] = new LiveQuote(tick.Symbol, ltp, tick.Change ?? previous?.Change, tick.ChangePercent ?? previous?.ChangePercent, now);
            _versions[tick.Symbol] = ++_version;
            _lastTickAt = now;
        }
    }

    /// <summary>The quotes recorded since <paramref name="version"/>, largest weight first, and the version to pass next time.</summary>
    public (IReadOnlyList<LiveQuote> Quotes, long Version) GetChangedSince(long version)
    {
        lock (_lock)
            return ([.. _symbols.Where(s => _versions.GetValueOrDefault(s) > version).Select(s => _quotes[s])], _version);
    }

    public LiveQuoteList GetQuotes()
    {
        lock (_lock)
            return new LiveQuoteList(_state, _lastTickAt, [.. _symbols.Select(s => _quotes.GetValueOrDefault(s)).OfType<LiveQuote>()]);
    }

    public LiveFeedStatus GetStatus()
    {
        lock (_lock)
            return new LiveFeedStatus(_state, [.. _symbols], _quotes.Count, _connectedAt, _lastTickAt, _lastError, _lastErrorAt);
    }
}
