using System.Text;
using Gadfly.Server.Data;
using Gadfly.Server.Platform;
using Microsoft.EntityFrameworkCore;

namespace Gadfly.Server.Tenancy;

/// <summary>One CSV row that would be sent, and whether the plan has a free seat left for it when the rows are taken in order.</summary>
public sealed record BulkInvitePreviewRow(int Line, string Email, string Role, bool OverSeats);

/// <summary>
/// What a CSV of invitations would do to the plan's seats, shown to the admin before anything is sent (GAD-T487). Seats are
/// spent only when an invitation is accepted (and acceptance is refused at the limit, GAD-T481), so the rows past the free
/// seats can still be sent; they just can't be accepted until seats free up. Free seat counts are null when unlimited.
/// </summary>
public sealed record BulkInvitePreview(IReadOnlyList<BulkInvitePreviewRow> Rows, int? FreeStaffSeats, int? FreeStudentSeats, int AlreadyMembers)
{
    public IReadOnlyList<BulkInvitePreviewRow> Over => Rows.Where(r => r.OverSeats).ToList();
    public int OverStaff => Rows.Count(r => r.OverSeats && r.Role != OrgRoles.Student);
    public int OverStudent => Rows.Count(r => r.OverSeats && r.Role == OrgRoles.Student);

    /// <summary>
    /// The rows of <paramref name="csv"/> that would become invitations, parsed the way MindAttic.Authentication's bulk invite
    /// parses them (<c>email[,role]</c>, comma or semicolon, optional <c>email</c> header, blank and '#' lines skipped, a repeated
    /// address only once). Rows that would be refused anyway (not an email, not a role, Owner by a non-owner) are left out.
    /// </summary>
    public static IReadOnlyList<(int Line, string Email, string Role)> Parse(string? csv, string defaultRole, string? actingRole)
    {
        var lines = (csv ?? "").Split('\n').Select((text, i) => (Line: i + 1, Text: text.Trim().TrimEnd('\r')))
            .Where(l => l.Text.Length > 0 && !l.Text.StartsWith('#')).ToList();
        if (lines.Count > 0 && Cells(lines[0].Text)[0] is var head && !head.Contains('@') && head.Equals("email", StringComparison.OrdinalIgnoreCase))
            lines.RemoveAt(0);

        var rows = new List<(int, string, string)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var (line, text) in lines)
        {
            var cells = Cells(text);
            var email = cells[0];
            var role = cells.Length > 1 && cells[1].Length > 0 ? cells[1] : defaultRole;
            if (!seen.Add(Key(email))) continue;
            if (!OrganizationDirectory.IsValidEmail(email) || !OrgRoles.All.Contains(role, StringComparer.Ordinal)) continue;
            if (role == OrgRoles.Owner && actingRole is not null && actingRole != OrgRoles.Owner) continue;
            rows.Add((line, email, role));
        }
        return rows;
    }

    /// <summary>
    /// Takes the parsed rows in order against the free seats of each kind; a row whose address already belongs to an active member
    /// of the organization is skipped (the invite would be refused as "already a member" and spends no seat).
    /// </summary>
    public static BulkInvitePreview Build(IReadOnlyList<(int Line, string Email, string Role)> rows, ISet<string> memberEmailKeys,
        int staffLimit, int staffUsed, int studentLimit, int studentUsed)
    {
        int? freeStaff = staffLimit > 0 ? Math.Max(0, staffLimit - staffUsed) : null;
        int? freeStudent = studentLimit > 0 ? Math.Max(0, studentLimit - studentUsed) : null;
        int staffLeft = freeStaff ?? int.MaxValue, studentLeft = freeStudent ?? int.MaxValue, already = 0;
        var result = new List<BulkInvitePreviewRow>();
        foreach (var (line, email, role) in rows)
        {
            if (memberEmailKeys.Contains(Key(email))) { already++; continue; }
            bool over;
            if (role == OrgRoles.Student) { over = studentLeft <= 0; if (!over) studentLeft--; }
            else { over = staffLeft <= 0; if (!over) staffLeft--; }
            result.Add(new(line, email, role, over));
        }
        return new(result, freeStaff, freeStudent, already);
    }

    public static async Task<BulkInvitePreview> ForAsync(GadflyDbContext db, PlanEnforcement plans, Guid organizationId, string csv,
        string defaultRole, string? actingRole, CancellationToken ct = default)
    {
        var rows = Parse(csv, defaultRole, actingRole);
        var status = await plans.StatusAsync(db, organizationId, ct);
        var emails = await (from m in db.OrganizationMembers.AsNoTracking()
                            join u in db.AuthUsers.AsNoTracking() on m.UserId equals u.Id
                            where m.OrganizationId == organizationId && m.IsActive && u.Email != null
                            select u.Email!).ToListAsync(ct);
        var keys = emails.Select(Key).ToHashSet(StringComparer.Ordinal);
        return Build(rows, keys, status.StaffSeatLimit, status.StaffSeatsUsed, status.StudentSeatLimit, status.StudentSeatsUsed);
    }

    private static string[] Cells(string line) => line.Split(',', ';').Select(c => c.Trim().Trim('"').Trim()).ToArray();

    private static string Key(string email) => (email ?? "").Normalize(NormalizationForm.FormKC).Trim().ToUpperInvariant();
}
