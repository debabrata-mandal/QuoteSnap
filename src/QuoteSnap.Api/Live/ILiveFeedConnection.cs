namespace QuoteSnap.Api.Live;

/// <summary>A price tick; values the socket didn't send are null.</summary>
public readonly record struct LiveTick(string Symbol, decimal? Ltp, decimal? Change, decimal? ChangePercent);

/// <summary>Receives a connection's events, on the connection's own threads.</summary>
public interface ILiveFeedListener
{
    void OnOpened();

    void OnTick(LiveTick tick);

    /// <param name="fatal">The connection is dead and won't reconnect by itself.</param>
    void OnError(string message, bool fatal);

    void OnClosed(string reason);
}

/// <summary>One market data socket. Disposing closes it for good; create another to reconnect.</summary>
public interface ILiveFeedConnection : IDisposable
{
    /// <summary>Opens the socket; <see cref="ILiveFeedListener.OnOpened"/> follows once it's authenticated.</summary>
    void Connect();

    /// <summary>Starts streaming; failures are reported to <see cref="ILiveFeedListener.OnError"/>.</summary>
    void Subscribe(IReadOnlyList<string> symbols);

    void Unsubscribe(IReadOnlyList<string> symbols);
}

public interface ILiveFeedConnectionFactory
{
    ILiveFeedConnection Create(string appId, string accessToken, ILiveFeedListener listener);
}
