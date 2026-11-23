using Gadfly.Server.Data;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Inbox;

/// <summary>The kinds of inbox message (<see cref="InboxMessage.Kind"/>).</summary>
public static class InboxKinds
{
    /// <summary>The AI panel's result, labelled not final (GAD-T702).</summary>
    public const string GradePreliminary = "grade.preliminary";
    /// <summary>The human-approved grade, once published (GAD-T702).</summary>
    public const string GradeFinal = "grade.final";
}

/// <summary>
/// A user's in-app inbox (GAD-T701). Every call names the organization and the user, so it works the same in a page's
/// tenant scope and in a job's system scope (the grading worker, GAD-T700/T702).
/// </summary>
public sealed class InboxService(GadflyDbContext db, TimeProvider clock)
{
    /// <summary>
    /// Posts a message to <paramref name="userId"/>'s inbox. With a <paramref name="sessionId"/> it is idempotent per
    /// (kind, session, recipient): posting again returns the message already there, unchanged.
    /// </summary>
    public async Task<InboxMessage> PostAsync(Guid organizationId, Guid userId, string kind, string title, string body,
        string? linkUrl = null, Guid? sessionId = null, CancellationToken ct = default)
    {
        if (sessionId is not null && await FindAsync(organizationId, userId, kind, sessionId, ct) is { } existing) return existing;
        var message = new InboxMessage
        {
            OrganizationId = organizationId,
            RecipientUserId = userId,
            Kind = kind,
            Title = title,
            Body = body,
            LinkUrl = linkUrl,
            SessionId = sessionId,
            CreatedAt = clock.GetUtcNow(),
        };
        db.InboxMessages.Add(message);
        try
        {
            await db.SaveChangesAsync(ct);
        }
        catch (DbUpdateException) when (sessionId is not null)
        {
            // A concurrent retry posted it first; the unique index kept one.
            db.Entry(message).State = EntityState.Detached;
            if (await FindAsync(organizationId, userId, kind, sessionId, ct) is { } raced) return raced;
            throw;
        }
        return message;
    }

    public Task<int> UnreadCountAsync(Guid organizationId, Guid userId, CancellationToken ct = default) =>
        Mine(organizationId, userId).CountAsync(m => m.ReadAt == null, ct);

    /// <summary>The user's messages, newest first.</summary>
    public Task<List<InboxMessage>> ListAsync(Guid organizationId, Guid userId, int take = 200, CancellationToken ct = default) =>
        Mine(organizationId, userId).AsNoTracking().OrderByDescending(m => m.CreatedAt).Take(take).ToListAsync(ct);

    /// <summary>Marks one of the user's messages read; false when it isn't theirs or doesn't exist.</summary>
    public async Task<bool> MarkReadAsync(Guid organizationId, Guid userId, Guid messageId, CancellationToken ct = default)
    {
        var message = await Mine(organizationId, userId).FirstOrDefaultAsync(m => m.Id == messageId, ct);
        if (message is null) return false;
        if (message.ReadAt is null)
        {
            message.ReadAt = clock.GetUtcNow();
            await db.SaveChangesAsync(ct);
        }
        return true;
    }

    /// <summary>Marks every unread message of the user's read; returns how many changed.</summary>
    public Task<int> MarkAllReadAsync(Guid organizationId, Guid userId, CancellationToken ct = default)
    {
        var now = clock.GetUtcNow();
        return Mine(organizationId, userId).Where(m => m.ReadAt == null)
            .ExecuteUpdateAsync(s => s.SetProperty(m => m.ReadAt, now), ct);
    }

    private IQueryable<InboxMessage> Mine(Guid organizationId, Guid userId) =>
        db.InboxMessages.Where(m => m.OrganizationId == organizationId && m.RecipientUserId == userId);

    private Task<InboxMessage?> FindAsync(Guid organizationId, Guid userId, string kind, Guid? sessionId, CancellationToken ct) =>
        Mine(organizationId, userId).FirstOrDefaultAsync(m => m.Kind == kind && m.SessionId == sessionId, ct);
}

/// <summary>Per circuit: a page that marks messages read tells the top bar's unread badge to recount (GAD-T701).</summary>
public sealed class InboxBadgeSignal
{
    public event Action? Changed;
    public void Raise() => Changed?.Invoke();
}
