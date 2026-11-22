using System.Globalization;
using System.Text;
using Gadfly.Server.Tenancy;

namespace Gadfly.Server.Admin;

/// <summary>
/// A small RFC 4180 reader for files an admin pastes or uploads (GAD-T296): commas, quoted cells with doubled quotes, line breaks
/// inside quotes, CRLF or LF endings, a leading byte-order mark. It never throws on bad input: an unclosed quote ends the cell at
/// the end of the text.
/// </summary>
public static class CsvReader
{
    /// <summary>The rows of <paramref name="text"/>, each with the line it started on (1-based). Blank lines are skipped.</summary>
    public static IReadOnlyList<(int Line, IReadOnlyList<string> Cells)> Read(string? text)
    {
        var rows = new List<(int, IReadOnlyList<string>)>();
        if (string.IsNullOrEmpty(text)) return rows;
        var s = text.Length > 0 && text[0] == '﻿' ? text[1..] : text;
        var cells = new List<string>();
        var cell = new StringBuilder();
        var quoted = false;
        var line = 1;
        var rowLine = 1;
        var cellStarted = false;

        void EndCell()
        {
            cells.Add(cell.ToString());
            cell.Clear();
            cellStarted = false;
        }

        void EndRow()
        {
            EndCell();
            if (cells.Count > 1 || cells[0].Trim().Length > 0) rows.Add((rowLine, cells.ToArray()));
            cells.Clear();
        }

        for (var i = 0; i < s.Length; i++)
        {
            var c = s[i];
            if (quoted)
            {
                if (c == '"')
                {
                    if (i + 1 < s.Length && s[i + 1] == '"') { cell.Append('"'); i++; }
                    else quoted = false;
                }
                else
                {
                    if (c == '\n') line++;
                    cell.Append(c);
                }
                continue;
            }
            switch (c)
            {
                case '"' when !cellStarted || cell.ToString().Trim().Length == 0:
                    cell.Clear();
                    quoted = true;
                    cellStarted = true;
                    break;
                case ',':
                    EndCell();
                    break;
                case '\r':
                    break;
                case '\n':
                    EndRow();
                    line++;
                    rowLine = line;
                    break;
                default:
                    cell.Append(c);
                    cellStarted = true;
                    break;
            }
        }
        if (cellStarted || cell.Length > 0 || cells.Count > 0) EndRow();
        return rows;
    }

    /// <summary>
    /// A cell as typed, trimmed, with the apostrophe a CSV export puts before a formula-like value (=, +, -, @) taken off again, so
    /// a file exported from Gadfly imports back unchanged.
    /// </summary>
    public static string Clean(string? cell)
    {
        var v = (cell ?? "").Trim();
        return v.Length > 1 && v[0] == '\'' && v[1] is '=' or '+' or '-' or '@' ? v[1..] : v;
    }
}

/// <summary>One student row of an import file, checked on its own (the database checks come later, in the plan).</summary>
/// <param name="Line">The file line the row starts on, for the error table.</param>
/// <param name="Error">Why the row can't be used, or null.</param>
public sealed record StudentCsvRow(int Line, string Email, string? Name, string? UserName, string? Password, string? Error);

/// <summary>A parsed import file: its rows, plus problems with the file as a whole (no header, no email column, too many rows).</summary>
public sealed record StudentCsvFile(IReadOnlyList<StudentCsvRow> Rows, IReadOnlyList<string> FileErrors, IReadOnlyList<string> IgnoredColumns)
{
    public bool Usable => FileErrors.Count == 0;
}

/// <summary>
/// The students import file (GAD-T296). The first row names the columns, in any order and any letter case: <c>email</c> (required),
/// <c>name</c>, <c>username</c> and <c>password</c>. Other columns are ignored and listed. A row with a password becomes an account
/// the admin hands over (the student must choose their own password at first sign-in); a row without one becomes an email
/// invitation the student accepts. The email is the key: importing the same file twice changes nothing the second time.
/// </summary>
public static class StudentCsv
{
    public const int MaxRows = 5000;
    public const int MaxNameLength = 200;

    /// <summary>The header a template download starts with.</summary>
    public const string TemplateHeader = "email,name,username,password";

    private static readonly Dictionary<string, string> Aliases = new(StringComparer.Ordinal)
    {
        ["email"] = "email", ["emailaddress"] = "email", ["mail"] = "email",
        ["name"] = "name", ["fullname"] = "name", ["displayname"] = "name", ["studentname"] = "name",
        ["username"] = "username", ["user"] = "username", ["login"] = "username", ["userid"] = "username",
        ["password"] = "password", ["temporarypassword"] = "password", ["temppassword"] = "password",
    };

    /// <summary>A header cell reduced to letters and digits, lower case ("E-mail address" is "emailaddress").</summary>
    internal static string Key(string header) =>
        new(header.Trim().ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());

    public static StudentCsvFile Parse(string? text)
    {
        var rows = CsvReader.Read(text);
        if (rows.Count == 0) return new([], ["The file is empty. Its first row must name the columns, for example: " + TemplateHeader], []);

        var header = rows[0].Cells;
        var columns = new Dictionary<string, int>(StringComparer.Ordinal);
        var ignored = new List<string>();
        for (var i = 0; i < header.Count; i++)
        {
            var raw = header[i].Trim();
            if (raw.Length == 0) continue;
            if (Aliases.TryGetValue(Key(raw), out var name))
            {
                if (!columns.TryAdd(name, i)) return new([], [$"The column \"{name}\" appears twice in the first row."], []);
            }
            else ignored.Add(raw);
        }
        if (!columns.ContainsKey("email"))
            return new([], ["The first row must name the columns and include an \"email\" column, for example: " + TemplateHeader], ignored);
        if (rows.Count - 1 > MaxRows)
            return new([], [$"The file has {rows.Count - 1:N0} rows; import at most {MaxRows:N0} at a time."], ignored);

        string? Cell(IReadOnlyList<string> cells, string column) =>
            columns.TryGetValue(column, out var i) && i < cells.Count && CsvReader.Clean(cells[i]) is { Length: > 0 } v ? v : null;

        var seen = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var result = new List<StudentCsvRow>(rows.Count - 1);
        foreach (var (line, cells) in rows.Skip(1))
        {
            var email = Cell(cells, "email") ?? "";
            var name = Cell(cells, "name");
            var userName = Cell(cells, "username");
            // A password is taken exactly as typed inside the cell (no trimming of its own spaces beyond the cell's edges).
            var password = columns.TryGetValue("password", out var pi) && pi < cells.Count && cells[pi].Length > 0 ? cells[pi].Trim() : null;
            if (password is { Length: 0 }) password = null;

            string? error = null;
            if (email.Length == 0) error = "No email.";
            else if (!OrganizationDirectory.IsValidEmail(email)) error = $"\"{Shorten(email)}\" is not an email address.";
            else if (seen.TryGetValue(email, out var first)) error = $"The same email is on line {first}.";
            else if (name is { Length: > MaxNameLength }) error = $"The name is longer than {MaxNameLength} characters.";
            else if (userName is { Length: > OrganizationDirectory.MaxAccountTextLength })
                error = $"The user name is longer than {OrganizationDirectory.MaxAccountTextLength} characters.";
            else if (userName is not null && password is null)
                error = "A user name needs a password in the same row (without a password the student is invited by email instead).";
            if (email.Length > 0 && !seen.ContainsKey(email)) seen[email] = line;
            result.Add(new StudentCsvRow(line, email, name, userName, password, error));
        }
        if (result.Count == 0) return new([], ["The file has a header row but no students under it."], ignored);
        return new(result, [], ignored);
    }

    private static string Shorten(string s) => s.Length <= 60 ? s : s[..57] + "...";

    /// <summary>One CSV line from cells, with the same quoting and formula guard as the other exports.</summary>
    public static string Line(params object?[] cells) =>
        string.Join(',', cells.Select(c => Analytics.AnalyticsEndpoints.Csv(c switch
        {
            null => "",
            IFormattable f => f.ToString(null, CultureInfo.InvariantCulture),
            _ => c.ToString(),
        }))) + "\r\n";
}
