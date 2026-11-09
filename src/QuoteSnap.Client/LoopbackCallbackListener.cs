using System.Net;
using System.Net.Sockets;
using System.Text;

namespace QuoteSnap.Client;

/// <summary>
/// A minimal HTTP listener on 127.0.0.1 that waits for the browser's redirect. Uses a raw socket so it needs
/// no URL reservation or admin rights on Windows.
/// </summary>
internal sealed class LoopbackCallbackListener : IDisposable
{
    private readonly TcpListener _listener;
    private readonly string _path;
    private readonly string _successPage;

    private LoopbackCallbackListener(TcpListener listener, string path, string broker)
    {
        _listener = listener;
        _path = path;
        _successPage = "<!doctype html><html><head><meta charset=\"utf-8\"><title>QuoteSnap</title></head>"
            + $"<body style=\"font-family:sans-serif\"><p>{broker} login received. You can close this tab and return to QuoteSnap.</p></body></html>";
    }

    public int Port => ((IPEndPoint)_listener.LocalEndpoint).Port;

    /// <param name="port">0 picks a free port (used by tests).</param>
    /// <param name="broker">Named on the page the browser shows after the redirect.</param>
    /// <param name="listenError">Makes the exception thrown when the port can't be opened, from its message and cause.</param>
    public static LoopbackCallbackListener Start(int port, string path, string broker = "Fyers", Func<string, Exception, Exception>? listenError = null)
    {
        TcpListener listener = new(IPAddress.Loopback, port);
        try
        {
            listener.Start();
        }
        catch (SocketException ex)
        {
            listener.Dispose();
            string message = $"Could not listen on 127.0.0.1:{port} ({ex.SocketErrorCode}). Close whatever is using that port, or use manual mode.";
            throw listenError?.Invoke(message, ex) ?? new FyersLoginException(message, ex);
        }

        return new LoopbackCallbackListener(listener, path, broker);
    }

    /// <summary>Returns the full callback URL, including its query string.</summary>
    public async Task<Uri> WaitForCallbackAsync(CancellationToken cancellationToken)
    {
        TaskCompletionSource<Uri> callback = new(TaskCreationOptions.RunContinuationsAsynchronously);
        await using CancellationTokenRegistration registration = cancellationToken.Register(() => callback.TrySetCanceled(cancellationToken));
        _ = AcceptLoopAsync(callback, cancellationToken);
        return await callback.Task;
    }

    public void Dispose() => _listener.Dispose();

    // Browsers open speculative connections that never send a request, so each connection is handled on its own.
    private async Task AcceptLoopAsync(TaskCompletionSource<Uri> callback, CancellationToken cancellationToken)
    {
        while (!callback.Task.IsCompleted)
        {
            TcpClient client;
            try
            {
                client = await _listener.AcceptTcpClientAsync(cancellationToken);
            }
            catch (Exception ex) when (ex is OperationCanceledException or SocketException or ObjectDisposedException)
            {
                return;
            }

            _ = HandleConnectionAsync(client, callback, cancellationToken);
        }
    }

    private async Task HandleConnectionAsync(TcpClient client, TaskCompletionSource<Uri> callback, CancellationToken cancellationToken)
    {
        using (client)
        {
            try
            {
                using CancellationTokenSource timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(TimeSpan.FromSeconds(10));
                NetworkStream stream = client.GetStream();

                string? target = await ReadRequestTargetAsync(stream, timeout.Token);
                if (target is not null && IsCallbackPath(target))
                {
                    await RespondAsync(stream, "200 OK", _successPage, timeout.Token);
                    callback.TrySetResult(new Uri($"http://127.0.0.1:{Port}{target}"));
                }
                else
                {
                    await RespondAsync(stream, "404 Not Found", "Not found", timeout.Token);
                }
            }
            catch (Exception ex) when (ex is IOException or SocketException or OperationCanceledException)
            {
            }
        }
    }

    private bool IsCallbackPath(string target)
    {
        int query = target.IndexOf('?');
        string path = query < 0 ? target : target[..query];
        return string.Equals(path, _path, StringComparison.OrdinalIgnoreCase);
    }

    private static async Task<string?> ReadRequestTargetAsync(NetworkStream stream, CancellationToken cancellationToken)
    {
        using StreamReader reader = new(stream, Encoding.ASCII, false, 4096, leaveOpen: true);
        string? requestLine = await reader.ReadLineAsync(cancellationToken);
        string[]? parts = requestLine?.Split(' ');
        return parts is { Length: >= 2 } && parts[0] == "GET" ? parts[1] : null;
    }

    private static async Task RespondAsync(NetworkStream stream, string status, string body, CancellationToken cancellationToken)
    {
        byte[] content = Encoding.UTF8.GetBytes(body);
        string header = $"HTTP/1.1 {status}\r\nContent-Type: text/html; charset=utf-8\r\n"
            + $"Content-Length: {content.Length}\r\nConnection: close\r\n\r\n";
        await stream.WriteAsync(Encoding.ASCII.GetBytes(header), cancellationToken);
        await stream.WriteAsync(content, cancellationToken);
    }
}
