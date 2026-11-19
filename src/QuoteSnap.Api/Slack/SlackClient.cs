using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Options;
using QuoteSnap.Api.Data;

namespace QuoteSnap.Api.Slack;

/// <summary>Slack turned a call down, e.g. a revoked token ("invalid_auth") or a bot outside the channel ("not_in_channel").</summary>
public sealed class SlackException(string error) : Exception(error)
{
    public string Error { get; } = error;
}

/// <summary>Calls the Slack Web API with the bot and app tokens, which are secrets, so its requests aren't logged.</summary>
public sealed class SlackClient(HttpClient http, IOptions<SlackOptions> options)
{
    /// <returns>The new message's timestamp, which is its ID.</returns>
    public async Task<string> PostAsync(SlackMessage message, CancellationToken cancellationToken)
    {
        JsonObject payload = new() { ["channel"] = options.Value.ChannelId, ["text"] = message.Text, ["unfurl_links"] = false };
        if (message.Blocks is { } blocks)
            payload["blocks"] = blocks.DeepClone();
        JsonElement result = await CallAsync("chat.postMessage", payload, cancellationToken);
        return result.GetProperty("ts").GetString() ?? throw new SlackException("no_ts");
    }

    /// <summary>Deletes one of the bot's messages; one that's already gone counts as deleted.</summary>
    public async Task DeleteAsync(string channel, string ts, CancellationToken cancellationToken)
    {
        try
        {
            await CallAsync("chat.delete", new JsonObject { ["channel"] = channel, ["ts"] = ts }, cancellationToken);
        }
        catch (SlackException ex) when (ex.Error == "message_not_found")
        {
        }
    }

    /// <summary>Gets a Socket Mode WebSocket URL with the app token; it's single-use and expires in 30 seconds.</summary>
    public async Task<Uri> OpenSocketAsync(CancellationToken cancellationToken)
    {
        SlackOptions slack = options.Value;
        if (!slack.CommandsConfigured)
            throw new InvalidOperationException("Slack:AppToken isn't set.");

        JsonElement result = await SendAsync(new Uri(slack.ApiBaseUrl, "apps.connections.open"), slack.AppToken!, new JsonObject(), cancellationToken);
        return new Uri(result.GetProperty("url").GetString() ?? throw new SlackException("no_url"));
    }

    /// <summary>Answers a slash command with a message only the person who typed it sees.</summary>
    public async Task RespondAsync(Uri responseUrl, string text, CancellationToken cancellationToken)
    {
        using HttpResponseMessage response = await http.PostAsJsonAsync(responseUrl, new JsonObject { ["response_type"] = "ephemeral", ["text"] = text }, cancellationToken);
        response.EnsureSuccessStatusCode();
    }

    private Task<JsonElement> CallAsync(string method, JsonObject payload, CancellationToken cancellationToken)
    {
        SlackOptions slack = options.Value;
        if (!slack.IsConfigured)
            throw new InvalidOperationException("Slack:BotToken and Slack:ChannelId aren't set.");
        return SendAsync(new Uri(slack.ApiBaseUrl, method), slack.BotToken!, payload, cancellationToken);
    }

    private async Task<JsonElement> SendAsync(Uri url, string token, JsonObject payload, CancellationToken cancellationToken)
    {
        using HttpRequestMessage request = new(HttpMethod.Post, url) { Content = JsonContent.Create(payload) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using HttpResponseMessage response = await http.SendAsync(request, cancellationToken);
        if (!response.IsSuccessStatusCode)
            throw new SlackException($"HTTP {(int)response.StatusCode} {response.ReasonPhrase}");

        using JsonDocument body = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        if (!body.RootElement.TryGetProperty("ok", out JsonElement ok) || !ok.GetBoolean())
            throw new SlackException(body.RootElement.TryGetProperty("error", out JsonElement error) ? error.GetString() ?? "unknown_error" : "unknown_error");
        return body.RootElement.Clone();
    }

    /// <summary>Escapes the characters Slack treats as markup in message text.</summary>
    public static string Escape(string text) => text.Replace("&", "&amp;", StringComparison.Ordinal).Replace("<", "&lt;", StringComparison.Ordinal).Replace(">", "&gt;", StringComparison.Ordinal);
}

/// <summary>
/// Keeps one QuoteSnap post in the channel: each new post replaces the previous one. When the estimates can't be posted,
/// one more post says why, replaced only when the problem changes and deleted once the estimates are posted again.
/// </summary>
public sealed class SlackPoster(SlackClient slack, SlackSettingsRepository settings, IOptions<SlackOptions> options, ILogger<SlackPoster> logger)
{
    private static readonly SemaphoreSlim Gate = new(1, 1);

    public async Task PostAsync(SlackMessage message, CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            (string Channel, string Ts)? previous = await settings.GetLastMessageAsync(cancellationToken);
            string ts = await slack.PostAsync(message, cancellationToken);
            await settings.SetLastMessageAsync(options.Value.ChannelId!, ts, cancellationToken);
            if (previous is { } p)
                await DeleteAsync(p.Channel, p.Ts, cancellationToken);

            if (await settings.GetProblemAsync(cancellationToken) is { } problem)
            {
                await settings.SetProblemAsync(null, cancellationToken);
                await DeleteAsync(problem.Channel, problem.Ts, cancellationToken);
            }
        }
        finally
        {
            Gate.Release();
        }
    }

    /// <returns>Whether it was posted; false when the channel already shows a problem of the same kind.</returns>
    public async Task<bool> PostProblemAsync(SlackProblem problem, CancellationToken cancellationToken)
    {
        await Gate.WaitAsync(cancellationToken);
        try
        {
            (string Channel, string Ts, string Key)? previous = await settings.GetProblemAsync(cancellationToken);
            if (previous?.Key == problem.Key)
                return false;

            string ts = await slack.PostAsync(new SlackMessage($"*QuoteSnap couldn't post the NAV estimates*\n{problem.ToSlack()}"), cancellationToken);
            await settings.SetProblemAsync((options.Value.ChannelId!, ts, problem.Key), cancellationToken);
            if (previous is { } p)
                await DeleteAsync(p.Channel, p.Ts, cancellationToken);
            return true;
        }
        finally
        {
            Gate.Release();
        }
    }

    private async Task DeleteAsync(string channel, string ts, CancellationToken cancellationToken)
    {
        try
        {
            await slack.DeleteAsync(channel, ts, cancellationToken);
        }
        catch (Exception ex) when (ex is SlackException or HttpRequestException)
        {
            logger.LogWarning("Couldn't delete the previous Slack post {Ts}: {Error}", ts, ex.Message);
        }
    }
}
