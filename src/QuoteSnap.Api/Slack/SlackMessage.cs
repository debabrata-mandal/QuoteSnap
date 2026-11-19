using System.Text.Json.Nodes;

namespace QuoteSnap.Api.Slack;

/// <summary>A webhook post: Block Kit blocks for the rich layout, and the plain text Slack shows in notifications.</summary>
public sealed record SlackMessage(string Text, JsonArray? Blocks = null);
