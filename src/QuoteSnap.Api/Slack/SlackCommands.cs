using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using Polly;
using QuoteSnap.Api.Data;

namespace QuoteSnap.Api.Slack;

/// <summary>
/// Answers the app's slash command (/latest) over Slack's Socket Mode, so the API needs no public URL:
/// it posts today's saved NAV estimates in the channel, replacing the previous post, whatever the switch or market status.
/// </summary>
public sealed class SlackCommands(IOptions<SlackOptions> options, IServiceScopeFactory scopes, TimeProvider time, ILogger<SlackCommands> logger) : BackgroundService
{
    public const string NoEstimatesText = "No NAV estimates have been saved today yet.";

    private static readonly TimeSpan MinRetryDelay = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.CommandsConfigured)
        {
            logger.LogInformation("Slack /latest is off: Slack:AppToken (xapp-...) isn't set.");
            return;
        }

        TimeSpan delay = MinRetryDelay;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (await RunConnectionAsync(stoppingToken))
                    delay = MinRetryDelay;
                continue;
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception ex)
            {
                logger.LogWarning("Slack Socket Mode connection failed, retrying in {Delay}: {Error}", delay, ex.Message);
            }

            try
            {
                await Task.Delay(delay, time, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                return;
            }

            delay = TimeSpan.FromTicks(Math.Min(delay.Ticks * 2, MaxRetryDelay.Ticks));
        }
    }

    /// <summary>One Socket Mode connection, until Slack asks to reconnect or it drops.</summary>
    /// <returns>Whether Slack said hello, i.e. the connection worked.</returns>
    private async Task<bool> RunConnectionAsync(CancellationToken stoppingToken)
    {
        Uri url;
        await using (AsyncServiceScope scope = scopes.CreateAsyncScope())
            url = await scope.ServiceProvider.GetRequiredService<SlackClient>().OpenSocketAsync(stoppingToken);

        using ClientWebSocket socket = new();
        socket.Options.KeepAliveInterval = TimeSpan.FromSeconds(30);
        await socket.ConnectAsync(url, stoppingToken);
        bool connected = false;
        while (socket.State == WebSocketState.Open)
        {
            JsonNode? envelope = await ReceiveAsync(socket, stoppingToken);
            if (envelope is null)
                break;

            string? type = envelope["type"]?.GetValue<string>();
            if (type == "hello")
            {
                connected = true;
                logger.LogInformation("Slack /latest is listening.");
                continue;
            }
            if (type == "disconnect")
                break;
            if (envelope["envelope_id"]?.GetValue<string>() is not { } envelopeId)
                continue;

            JsonNode? payload = envelope["payload"];
            string? channelId = payload?["channel_id"]?.GetValue<string>();
            string? rejection = type == "slash_commands" ? Reject(channelId) : null;
            JsonObject ack = new() { ["envelope_id"] = envelopeId };
            if (rejection is not null)
                ack["payload"] = new JsonObject { ["text"] = rejection };
            await socket.SendAsync(Encoding.UTF8.GetBytes(ack.ToJsonString()), WebSocketMessageType.Text, true, stoppingToken);

            if (type == "slash_commands" && rejection is null)
                _ = AnswerAsync(payload?["response_url"]?.GetValue<string>(), stoppingToken);
        }

        if (socket.State == WebSocketState.Open)
            await socket.CloseOutputAsync(WebSocketCloseStatus.NormalClosure, null, stoppingToken);
        return connected;
    }

    private static async Task<JsonNode?> ReceiveAsync(ClientWebSocket socket, CancellationToken cancellationToken)
    {
        using MemoryStream message = new();
        byte[] buffer = new byte[8192];
        while (true)
        {
            WebSocketReceiveResult result = await socket.ReceiveAsync(buffer, cancellationToken);
            if (result.MessageType == WebSocketMessageType.Close)
                return null;
            message.Write(buffer, 0, result.Count);
            if (result.EndOfMessage)
                return JsonNode.Parse(message.ToArray());
        }
    }

    private async Task AnswerAsync(string? responseUrl, CancellationToken cancellationToken)
    {
        try
        {
            string? note = await PostLatestAsync(cancellationToken);
            if (note is not null && Uri.TryCreate(responseUrl, UriKind.Absolute, out Uri? url))
            {
                await using AsyncServiceScope scope = scopes.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<SlackClient>().RespondAsync(url, note, cancellationToken);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Couldn't answer Slack /latest: {Error}", ex.Message);
        }
    }

    /// <summary>The command only works in the reports' channel, where the bot keeps its one post.</summary>
    /// <returns>Why it's turned down, for whoever typed it; null to answer it.</returns>
    public string? Reject(string? channelId) => channelId == options.Value.ChannelId ? null : $"QuoteSnap posts in <#{options.Value.ChannelId}>; use the command there.";

    /// <summary>Posts today's saved estimates in the channel, replacing the previous post.</summary>
    /// <returns>A note for whoever typed the command when nothing was posted; null when it was.</returns>
    public async Task<string?> PostLatestAsync(CancellationToken cancellationToken)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        IServiceProvider services = scope.ServiceProvider;
        SlackMessage? message = await SlackReport.BuildAsync(services.GetRequiredService<MyFundRepository>(), services.GetRequiredService<NavEstimateRepository>(), time, cancellationToken);
        if (message is null)
            return NoEstimatesText;

        try
        {
            await services.GetRequiredService<SlackPoster>().PostAsync(message, cancellationToken);
            return null;
        }
        catch (Exception ex) when (ex is SlackException or HttpRequestException or ExecutionRejectedException || ex is TaskCanceledException && !cancellationToken.IsCancellationRequested)
        {
            logger.LogWarning("Couldn't post the NAV estimates to Slack for /latest: {Error}", ex.Message);
            return $"Couldn't post the NAV estimates: {ex.Message}";
        }
    }
}
