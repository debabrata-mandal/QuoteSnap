namespace QuoteSnap.Api.Data;

/// <summary>Whether the Slack reports are on, and the latest post; a single row, with <see cref="Id"/> 1, once first set.</summary>
public sealed class SlackSettingsRecord
{
    public const int SingletonId = 1;

    public int Id { get; set; } = SingletonId;

    public bool Enabled { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }

    /// <summary>The channel of the latest post, which is deleted when the next one is posted.</summary>
    public string? LastMessageChannel { get; set; }

    /// <summary>The latest post's Slack timestamp, which is its ID.</summary>
    public string? LastMessageTs { get; set; }

    /// <summary>The channel of the post saying why the estimates couldn't be posted, deleted once they are.</summary>
    public string? ProblemChannel { get; set; }

    /// <summary>The problem post's Slack timestamp.</summary>
    public string? ProblemTs { get; set; }

    /// <summary>The kind of problem it reports (<see cref="Slack.SlackProblem.Key"/>), so the same one isn't posted again.</summary>
    public string? ProblemKey { get; set; }
}
