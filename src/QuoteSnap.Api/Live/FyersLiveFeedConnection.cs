using System.Globalization;
using FyersCSharpSDK;
using HyperSyncLib;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace QuoteSnap.Api.Live;

/// <summary>Opens Fyers Data Sockets with the official SDK, which reads the credentials from its process-wide singleton.</summary>
public sealed class FyersLiveFeedConnectionFactory : ILiveFeedConnectionFactory
{
    private static readonly Lock CredentialsLock = new();

    public ILiveFeedConnection Create(string appId, string accessToken, ILiveFeedListener listener)
    {
        lock (CredentialsLock)
        {
            FyersClass fyers = FyersClass.Instance;
            fyers.ClientId = appId;
            fyers.AccessToken = accessToken;
            return new FyersLiveFeedConnection(new FyersSocket(), listener);
        }
    }
}

/// <summary>
/// A Fyers Data Socket in lite mode, which streams only LTP and the day's change. The SDK reconnects by itself and
/// subscribes again to everything it was streaming; it reports giving up as an error.
/// </summary>
public sealed class FyersLiveFeedConnection : ILiveFeedConnection, FyersSocketDelegate
{
    private const string GaveUp = "Could not re-connect";

    private readonly FyersSocket _socket;
    private readonly ILiveFeedListener _listener;

    public FyersLiveFeedConnection(FyersSocket socket, ILiveFeedListener listener)
    {
        _socket = socket;
        _listener = listener;
        _socket.webSocketDelegate = this;
    }

    public void Connect() => _socket.ConnectHSM(ChannelModes.LITE, UserTypes.Normal);

    public void Subscribe(IReadOnlyList<string> symbols) => _socket.SubscribeData([.. symbols]);

    public void Unsubscribe(IReadOnlyList<string> symbols) => _socket.UnSubscribeData([.. symbols]);

    public void Dispose()
    {
        _socket.webSocketDelegate = null;
        _socket.CloseHSM();
    }

    /// <summary>A scrip update, e.g. {"data":{"ltp":812.4,"ch":5.1,"chp":0.63,"type":"sf","symbol":"NSE:SBIN-EQ"}}.</summary>
    public static LiveTick? ParseTick(JObject message)
    {
        if (message["data"] is not JObject data || data.Value<string>("symbol") is not { Length: > 0 } symbol)
            return null;
        return new LiveTick(symbol, Number(data, "ltp"), Number(data, "ch"), Number(data, "chp"));
    }

    private static decimal? Number(JObject data, string name) => data[name] is JValue { Type: JTokenType.Float or JTokenType.Integer } value ? Math.Round(Convert.ToDecimal(value.Value, CultureInfo.InvariantCulture), 4) : null;

    void FyersSocketDelegate.OnScrips(JObject scrips)
    {
        if (ParseTick(scrips) is { } tick)
            _listener.OnTick(tick);
    }

    void FyersSocketDelegate.OnOpen(string status) => _listener.OnOpened();

    void FyersSocketDelegate.OnError(JObject error)
    {
        string message = error.Value<string>("message") is { Length: > 0 } text ? text : error.ToString(Formatting.None);
        _listener.OnError(message, message.StartsWith(GaveUp, StringComparison.Ordinal));
    }

    void FyersSocketDelegate.OnClose(string status) => _listener.OnClosed(status);

    void FyersSocketDelegate.OnIndex(JObject index)
    {
    }

    void FyersSocketDelegate.OnDepth(JObject depths)
    {
    }

    void FyersSocketDelegate.OnOrder(JObject orders)
    {
    }

    void FyersSocketDelegate.OnTrade(JObject trades)
    {
    }

    void FyersSocketDelegate.OnPosition(JObject positions)
    {
    }

    void FyersSocketDelegate.OnMessage(JObject response)
    {
    }
}
