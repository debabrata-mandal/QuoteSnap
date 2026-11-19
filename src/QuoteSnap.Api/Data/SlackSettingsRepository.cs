using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace QuoteSnap.Api.Data;

public sealed class SlackSettingsRepository(QuoteSnapDbContext db, TimeProvider time)
{
    /// <summary>Off until turned on.</summary>
    public async Task<bool> GetEnabledAsync(CancellationToken cancellationToken) =>
        await db.SlackSettings.AsNoTracking().Where(s => s.Id == SlackSettingsRecord.SingletonId).Select(s => (bool?)s.Enabled).SingleOrDefaultAsync(cancellationToken) ?? false;

    public Task SetEnabledAsync(bool enabled, CancellationToken cancellationToken) => UpdateAsync(r => r.Enabled = enabled, cancellationToken);

    /// <summary>The (channel, timestamp) of the latest post; null before the first.</summary>
    public async Task<(string Channel, string Ts)?> GetLastMessageAsync(CancellationToken cancellationToken)
    {
        SlackSettingsRecord? row = await db.SlackSettings.AsNoTracking().SingleOrDefaultAsync(s => s.Id == SlackSettingsRecord.SingletonId, cancellationToken);
        return row is { LastMessageChannel: { } channel, LastMessageTs: { } ts } ? (channel, ts) : null;
    }

    public Task SetLastMessageAsync(string channel, string ts, CancellationToken cancellationToken) => UpdateAsync(r => (r.LastMessageChannel, r.LastMessageTs) = (channel, ts), cancellationToken);

    /// <summary>The (channel, timestamp, kind) of the post saying why the estimates couldn't be posted; null when there's none.</summary>
    public async Task<(string Channel, string Ts, string Key)?> GetProblemAsync(CancellationToken cancellationToken)
    {
        SlackSettingsRecord? row = await db.SlackSettings.AsNoTracking().SingleOrDefaultAsync(s => s.Id == SlackSettingsRecord.SingletonId, cancellationToken);
        return row is { ProblemChannel: { } channel, ProblemTs: { } ts, ProblemKey: { } key } ? (channel, ts, key) : null;
    }

    public Task SetProblemAsync((string Channel, string Ts, string Key)? problem, CancellationToken cancellationToken) => UpdateAsync(r => (r.ProblemChannel, r.ProblemTs, r.ProblemKey) = (problem?.Channel, problem?.Ts, problem?.Key), cancellationToken);

    private async Task UpdateAsync(Action<SlackSettingsRecord> update, CancellationToken cancellationToken)
    {
        if (await db.SlackSettings.SingleOrDefaultAsync(s => s.Id == SlackSettingsRecord.SingletonId, cancellationToken) is { } row)
        {
            update(row);
            row.UpdatedAt = time.GetUtcNow();
            await db.SaveChangesAsync(cancellationToken);
            return;
        }

        SlackSettingsRecord created = new() { UpdatedAt = time.GetUtcNow() };
        update(created);
        db.SlackSettings.Add(created);
        try
        {
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException ex) when (ex.InnerException is PostgresException { SqlState: PostgresErrorCodes.UniqueViolation })
        {
            // Created by a concurrent request; apply this change on top of it.
            db.ChangeTracker.Clear();
            SlackSettingsRecord existing = await db.SlackSettings.SingleAsync(s => s.Id == SlackSettingsRecord.SingletonId, cancellationToken);
            update(existing);
            existing.UpdatedAt = time.GetUtcNow();
            await db.SaveChangesAsync(cancellationToken);
        }
    }
}
