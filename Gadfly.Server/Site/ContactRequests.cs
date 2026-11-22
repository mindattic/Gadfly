using System.Collections.Concurrent;
using Gadfly.Server.Data;
using Gadfly.Server.Tenancy;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using MindAttic.Authentication.Services;

namespace Gadfly.Server.Site;

/// <summary>
/// The public contact form's spam protection (GAD-T308), with no third party: a hidden field people never fill in
/// (the honeypot), a signed render time that must be at least a few seconds and at most a day old, and a per-address
/// and site-wide rate limit kept in memory. Nothing about the visitor's address is stored.
/// </summary>
public sealed class ContactSpamGuard(IDataProtectionProvider dp, TimeProvider clock)
{
    public static readonly TimeSpan MinimumFillTime = TimeSpan.FromSeconds(3);
    public static readonly TimeSpan MaximumFormAge = TimeSpan.FromDays(1);
    public const int PerAddressPerHour = 5;
    public const int SiteWidePerDay = 300;

    private readonly IDataProtector _protector = dp.CreateProtector("Gadfly.Site.ContactForm.v1");
    private readonly ConcurrentDictionary<string, Queue<DateTimeOffset>> _byAddress = new();
    private readonly Queue<DateTimeOffset> _siteWide = new();
    private readonly object _gate = new();

    /// <summary>A stamp for a freshly rendered form.</summary>
    public string Stamp() => _protector.Protect(clock.GetUtcNow().ToUnixTimeMilliseconds().ToString(System.Globalization.CultureInfo.InvariantCulture));

    /// <summary>What to do with a submission.</summary>
    public enum Verdict { Accept, LooksAutomated, Expired, TooMany }

    public Verdict Check(string? honeypot, string? stamp, string? address)
    {
        if (!string.IsNullOrEmpty(honeypot)) return Verdict.LooksAutomated;
        DateTimeOffset rendered;
        try
        {
            rendered = DateTimeOffset.FromUnixTimeMilliseconds(long.Parse(_protector.Unprotect(stamp ?? ""), System.Globalization.CultureInfo.InvariantCulture));
        }
        catch (Exception ex) when (ex is System.Security.Cryptography.CryptographicException or FormatException or OverflowException or ArgumentException)
        {
            return Verdict.LooksAutomated;
        }
        var now = clock.GetUtcNow();
        var age = now - rendered;
        if (age < MinimumFillTime) return Verdict.LooksAutomated;
        if (age > MaximumFormAge) return Verdict.Expired;
        return Take(address ?? "unknown", now) ? Verdict.Accept : Verdict.TooMany;
    }

    private bool Take(string address, DateTimeOffset now)
    {
        lock (_gate)
        {
            Trim(_siteWide, now - TimeSpan.FromDays(1));
            var mine = _byAddress.GetOrAdd(address, _ => new Queue<DateTimeOffset>());
            Trim(mine, now - TimeSpan.FromHours(1));
            if (mine.Count >= PerAddressPerHour || _siteWide.Count >= SiteWidePerDay) return false;
            mine.Enqueue(now);
            _siteWide.Enqueue(now);
            // Keep the table small: drop addresses with nothing recent.
            if (_byAddress.Count > 10_000)
                foreach (var (key, q) in _byAddress)
                    if (q.Count == 0 || q.Peek() < now - TimeSpan.FromHours(1)) _byAddress.TryRemove(key, out _);
            return true;
        }
    }

    private static void Trim(Queue<DateTimeOffset> q, DateTimeOffset before)
    {
        while (q.Count > 0 && q.Peek() < before) q.Dequeue();
    }
}

/// <summary>
/// Stores contact-form requests and lets the platform admin read and mark them (GAD-T308). A new request is emailed to
/// <c>Gadfly:Site:ContactNotifyEmail</c> only when that address is set and the sign-in library's SMTP sender is
/// configured; otherwise nothing is sent and the request waits on the platform page.
/// </summary>
public sealed class ContactRequestService(GadflyDbContext db, ITenantContext tenant, IServiceProvider services,
    IConfiguration config, TimeProvider clock, ILogger<ContactRequestService> log)
{
    public const string NotifySetting = "Gadfly:Site:ContactNotifyEmail";
    public static readonly IReadOnlyList<string> Topics = ["Demo", "Pricing", "Trial", "Other"];
    public static readonly IReadOnlyList<string> StudentBands = ["Fewer than 100", "100 to 1,000", "1,000 to 10,000", "More than 10,000"];

    /// <summary>Saves a request from the public form (already validated and spam-checked).</summary>
    public async Task<ContactRequest> SubmitAsync(ContactRequest request, CancellationToken ct = default)
    {
        request.Id = Guid.NewGuid();
        request.ReceivedAt = clock.GetUtcNow();
        request.Name = Clip(request.Name, 200)!;
        request.Email = Clip(request.Email, 320)!;
        request.Institution = Clip(request.Institution, 200)!;
        request.Role = Clip(request.Role, 120);
        request.Topic = Topics.Contains(request.Topic) ? request.Topic : "Other";
        request.Plan = Plans.Contains(request.Plan ?? "") ? request.Plan : null;
        request.Students = StudentBands.Contains(request.Students ?? "") ? request.Students : null;
        request.Message = Clip(request.Message, 4000);
        request.HandledAt = null; request.HandledByUserId = null; request.Note = null;
        db.ContactRequests.Add(request);
        await db.SaveChangesAsync(ct);
        await NotifyAsync(request, ct);
        return request;
    }

    /// <summary>Requests newest first; open ones only unless <paramref name="includeHandled"/>.</summary>
    public async Task<IReadOnlyList<ContactRequest>> ListAsync(bool includeHandled, CancellationToken ct = default)
    {
        RequireSuperAdmin();
        var q = db.ContactRequests.AsNoTracking();
        if (!includeHandled) q = q.Where(r => r.HandledAt == null);
        return await q.OrderByDescending(r => r.ReceivedAt).Take(500).ToListAsync(ct);
    }

    public async Task<int> OpenCountAsync(CancellationToken ct = default)
    {
        RequireSuperAdmin();
        return await db.ContactRequests.CountAsync(r => r.HandledAt == null, ct);
    }

    /// <summary>Marks a request handled (or open again), with an optional note; audited.</summary>
    public async Task<bool> MarkAsync(Guid id, bool handled, string? note, CancellationToken ct = default)
    {
        RequireSuperAdmin();
        var r = await db.ContactRequests.FirstOrDefaultAsync(x => x.Id == id, ct);
        if (r is null) return false;
        var before = new { r.HandledAt, r.Note };
        r.HandledAt = handled ? clock.GetUtcNow() : null;
        r.HandledByUserId = handled ? tenant.UserId : null;
        r.Note = Clip(note, 1000);
        db.AuditEvents.Add(new AuditEvent
        {
            OrganizationId = null,
            ActorUserId = tenant.UserId,
            ActorRole = "SuperAdmin",
            Action = handled ? "site.contact.handled" : "site.contact.reopened",
            EntityType = nameof(ContactRequest),
            EntityId = r.Id.ToString(),
            BeforeJson = System.Text.Json.JsonSerializer.Serialize(before),
            AfterJson = System.Text.Json.JsonSerializer.Serialize(new { r.HandledAt, r.Note }),
            At = clock.GetUtcNow(),
        });
        await db.SaveChangesAsync(ct);
        return true;
    }

    private static readonly IReadOnlyList<string> Plans = SitePlans.All.Select(p => p.Name).ToList();

    private void RequireSuperAdmin()
    {
        if (!tenant.IsSuperAdmin && !tenant.IsSystem)
            throw new UnauthorizedAccessException("Only the platform admin can read contact requests.");
    }

    private async Task NotifyAsync(ContactRequest r, CancellationToken ct)
    {
        var to = config[NotifySetting];
        if (string.IsNullOrWhiteSpace(to)) return;
        // Only the real SMTP path: the library's logging fallback would write the visitor's details into the log.
        if (services.GetService<IAuthEmailSender>() is not SmtpAuthEmailSender sender) return;
        try
        {
            var body = $"A new {r.Topic.ToLowerInvariant()} request arrived from the Gadfly Exams website.\n\n" +
                       $"Name: {r.Name}\nEmail: {r.Email}\nInstitution: {r.Institution}\nRole: {r.Role ?? "-"}\n" +
                       $"Plan: {r.Plan ?? "-"}\nStudents: {r.Students ?? "-"}\n\n{r.Message}\n\n" +
                       "Read and mark it under Organizations > Contact requests.";
            await sender.SendSecurityAlertAsync(to, $"Gadfly Exams: {r.Topic} request from {r.Institution}", body, ct);
        }
        catch (Exception ex)
        {
            // The request is saved; a failed notification must not fail the visitor's submission.
            log.LogWarning(ex, "Contact request {Id} saved, but the notification email failed.", r.Id);
        }
    }

    private static string? Clip(string? s, int max)
    {
        if (s is null) return null;
        s = s.Trim();
        return s.Length <= max ? s : s[..max];
    }
}
