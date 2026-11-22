using System.Globalization;

namespace Gadfly.Server.Transcripts;

/// <summary>What a block of the printed transcript is, so each format can style it its own way.</summary>
public enum BlockKind
{
    Title,
    /// <summary>A line under the title (exam, student, dates).</summary>
    Meta,
    Heading,
    Subheading,
    /// <summary>Who speaks and when: "Examiner · 10:02:14 (+3:21)".</summary>
    Speaker,
    /// <summary>The words spoken or typed, indented under their speaker.</summary>
    Quote,
    Body,
    /// <summary>Secondary detail (delay, pauses, record status) in smaller type.</summary>
    Small,
    /// <summary>A boxed note: what the copy leaves out, reviewer-only context, removed words.</summary>
    Notice,
    /// <summary>Fixed-width text (the hash).</summary>
    Mono,
}

/// <summary>A piece of text with its emphasis. <see cref="Mark"/> is for words the speech recognizer was unsure of.</summary>
public sealed record Run(string Text, bool Bold = false, bool Italic = false, bool Mark = false, bool Mono = false);

public sealed record Block(BlockKind Kind, IReadOnlyList<Run> Runs, int Indent = 0)
{
    public Block(BlockKind kind, string text, int indent = 0) : this(kind, [new Run(text)], indent) { }
    public string PlainText => string.Concat(Runs.Select(r => r.Text));
}

/// <summary>
/// The printed transcript as an ordered list of blocks (GAD-T295), shared by the web page, PDF, Word and plain-text
/// renderers so every format says the same thing in the same order. Built only from the <see cref="TranscriptDocument"/>
/// (already filtered for its audience) and its <see cref="TranscriptStamp"/>.
/// </summary>
public static class TranscriptLayout
{
    private static readonly CultureInfo Ci = CultureInfo.InvariantCulture;

    public const string UnsureLegend = "Words marked [like this?] were hard for the speech recognizer to hear.";

    public static IReadOnlyList<Block> Blocks(TranscriptDocument d, TranscriptStamp stamp)
    {
        var b = new List<Block>
        {
            new(BlockKind.Title, "Official exam transcript"),
            new(BlockKind.Meta, [new Run(d.ExamTitle, Bold: true), new Run(Join(" · ", d.Subject, d.Topic) is { Length: > 0 } st ? " · " + st : "")]),
            new(BlockKind.Meta, $"Student: {d.StudentName} · Attempt {d.Attempt} · {d.Purpose} · {d.Mode}"),
        };
        if (d.Organization is { Length: > 0 } org) b.Add(new(BlockKind.Meta, $"Organization: {org}"));
        b.Add(new(BlockKind.Meta, $"Started: {Stamp(d.StartedAt)} · Ended: {Stamp(d.EndedAt)} · Status: {d.Status}" + (d.EndReason is { } er ? $" ({er})" : "")));
        b.Add(new(BlockKind.Meta, $"Copy: {CopyLabel(d.Audience)} · Session {d.SessionId:D}"));
        b.Add(new(BlockKind.Meta, [new Run("Verification code: "), new Run(stamp.Code, Bold: true, Mono: true), new Run($" · check it at {stamp.VerifyUrl}")]));
        b.Add(new(BlockKind.Mono, "SHA-256 " + stamp.Sha256));
        b.Add(new(BlockKind.Small, $"File made {Stamp(stamp.GeneratedAt)}. Times are UTC; +m:ss is the time since the exam started."));

        if (d.LeftOut.Count > 0)
            b.Add(new(BlockKind.Notice, "This copy leaves out " + JoinList(d.LeftOut) + "."));
        if (d.WordsRemovedAt is { } removed)
            b.Add(new(BlockKind.Notice, $"What the student said was removed on {Stamp(removed)} under the organization's retention rules. The questions, times and outcome remain."));
        if (d.Audience == TranscriptAudiences.Key(TranscriptAudience.Reviewer))
            b.Add(new(BlockKind.Notice, "Reviewer notes (camera, sound, browser and writing-style observations, voice measurements) are low-confidence observations for a person to weigh. "
                + "They are never a finding of misconduct and never part of a grade."));
        if (d.Turns.Any(t => t.Responses.Any(r => r.UnsureWords.Count > 0)))
            b.Add(new(BlockKind.Small, UnsureLegend));

        if (d.Turns.Count == 0) b.Add(new(BlockKind.Body, "No questions were asked in this session."));

        foreach (var t in d.Turns)
        {
            b.Add(new(BlockKind.Heading, $"Question {t.Number} · {t.Concept}"));
            b.Add(new(BlockKind.Speaker, [new Run("Examiner", Bold: true), new Run($" · {Time(t.AskedAt)} ({Offset(t.OffsetSeconds)}) · {t.Move}")]));
            b.Add(new(BlockKind.Quote, t.Question, 1));
            if (t.ExaminerReason is { } why) b.Add(new(BlockKind.Small, "Why the examiner asked this: " + why, 1));

            if (t.Responses.Count == 0) b.Add(new(BlockKind.Small, "No answer recorded.", 1));
            foreach (var r in t.Responses) AddResponse(b, r);

            if (t.Ai is { } ai)
            {
                var parts = new List<string> { $"panel score {Pct(ai.Score)}", $"agreement {Pct(ai.Agreement)}" };
                if (ai.JudgesDisagreed) parts.Add("the judges disagreed: a person should look");
                if (ai.MasteryAfter is { } m) parts.Add($"mastery after this answer {Pct(m)}");
                b.Add(new(BlockKind.Subheading, "AI panel at exam time (reviewer copy)", 1));
                b.Add(new(BlockKind.Small, Capital(string.Join("; ", parts)) + ".", 1));
                if (!string.IsNullOrWhiteSpace(ai.Summary)) b.Add(new(BlockKind.Small, ai.Summary, 1));
                if (ai.TextStyle is { } style) b.Add(new(BlockKind.Small, "Writing-style estimate: " + style, 1));
            }
            if (t.VoiceMeasurements is { } voice) b.Add(new(BlockKind.Small, "Voice measurements: " + voice, 1));
            foreach (var n in t.Notes) b.Add(NoteBlock(n));
        }

        if (d.SessionNotes.Count > 0)
        {
            b.Add(new(BlockKind.Heading, "Reviewer notes between questions"));
            foreach (var n in d.SessionNotes) b.Add(NoteBlock(n));
        }

        b.Add(new(BlockKind.Small, $"End of transcript. Verification code {stamp.Code}. Anyone holding this copy can check it at {stamp.VerifyUrl} "
            + "(or upload the JSON download there); the page says only whether it matches Gadfly's record."));
        return b;
    }

    private static void AddResponse(List<Block> b, TranscriptResponse r)
    {
        var when = r.StartedAt is { } s ? $"{Time(s)} ({Offset(r.StartOffsetSeconds ?? 0)})" : $"{Time(r.CompletedAt)} ({Offset(r.EndOffsetSeconds)})";
        var kind = r.Kind == "Answer" ? "" : $" · {r.Kind}";
        b.Add(new(BlockKind.Speaker, [new Run("Student", Bold: true), new Run($" · {when} · {r.Modality.ToLowerInvariant()}{kind}")]));
        b.Add(new(BlockKind.Quote, Marked(r.AsRecorded, r.UnsureWords), 1));

        var details = new List<string>();
        if (r.DelaySeconds is { } delay) details.Add($"started {Seconds(delay)} after the question");
        if (r.SpokenSeconds is { } spoken) details.Add($"spoke for {Seconds(spoken)}");
        if (r.Pauses is { } pauses) details.Add(pauses == 1 ? "1 pause" : $"{pauses} pauses");
        if (r.LongPauses is > 0 and var lp) details.Add(lp == 1 ? "1 long pause" : $"{lp} long pauses");
        if (r.LongestPauseSeconds is > 0 and var longest) details.Add($"longest pause {Seconds(longest)}");
        details.Add($"finished {Time(r.CompletedAt)} ({Offset(r.EndOffsetSeconds)})");
        b.Add(new(BlockKind.Small, Capital(string.Join(" · ", details)) + ".", 1));
        b.Add(new(BlockKind.Small, "Record: " + r.RecordStatus + (r.CheckedByStudent switch
        {
            true => ". The student checked the transcript.",
            false when r.UnsureWords.Count > 0 => ". The student did not check the transcript; the marked words may be mis-heard.",
            _ => ".",
        }), 1));
        if (r.UnsureWords.Count > 0 && r.UnsureWords.Any(u => u.LowestConfidence is not null))
            b.Add(new(BlockKind.Small, "Recognizer confidence on the marked words: "
                + string.Join("; ", r.UnsureWords.Select(u => $"\"{u.Text}\" {Pct(u.LowestConfidence ?? 0)}")) + ".", 1));

        if (r.ReadBack is { } rb)
        {
            b.Add(new(BlockKind.Speaker, [new Run("Examiner", Bold: true), new Run($" · {Time(rb.AskedAt)} · read-back")], 1));
            b.Add(new(BlockKind.Quote, rb.Prompt, 2));
            b.Add(new(BlockKind.Small, rb.Outcome + (rb.AnsweredAt is { } at ? $" ({Time(at)})" : ""), 2));
        }
        foreach (var c in r.Corrections)
        {
            b.Add(new(BlockKind.Subheading, $"Transcription correction (version {c.Version}) by {c.By}, {Time(c.At)}", 1));
            b.Add(new(BlockKind.Quote, [new Run("Original: ", Italic: true), new Run(c.Original)], 2));
            b.Add(new(BlockKind.Quote, [new Run("Corrected: ", Italic: true), new Run(c.Corrected)], 2));
        }
        if (r.Verified is { } verified && r.Corrections.Count == 0)
            b.Add(new(BlockKind.Quote, [new Run("Text used for grading: ", Italic: true), new Run(verified)], 1));
        foreach (var c in r.Clarifications)
        {
            b.Add(new(BlockKind.Speaker, [new Run("Examiner", Bold: true), new Run($" · {Time(c.AskedAt)} · asks what the student meant")], 1));
            b.Add(new(BlockKind.Quote, c.Question, 2));
            b.Add(new(BlockKind.Speaker, [new Run("Student", Bold: true), new Run(c.AnsweredAt is { } at ? $" · {Time(at)} · clarification" : " · clarification")], 1));
            b.Add(new(BlockKind.Quote, c.Reply ?? "(no reply)", 2));
        }
    }

    private static Block NoteBlock(TranscriptNote n) => new(BlockKind.Notice,
    [
        new Run($"Reviewer note · {Time(n.At)} ({Offset(n.OffsetSeconds)}) · ", Italic: true),
        new Run(n.Label, Bold: true),
        new Run($" · {n.Severity}, confidence {Pct(n.Confidence)}" + (n.NormalBehavior ? " · usually normal behaviour" : "") + ". "),
        new Run((n.Summary is { Length: > 0 } sum ? sum + " " : "") + n.Explanation),
    ], 1);

    /// <summary>The answer as runs, with each unsure word run marked; unsure runs not found in the text are listed after it.</summary>
    public static IReadOnlyList<Run> Marked(string text, IReadOnlyList<TranscriptUnsure> unsure)
    {
        var runs = new List<Run>();
        var at = 0;
        var missing = new List<string>();
        foreach (var u in unsure.OrderBy(u => u.StartSeconds))
        {
            if (string.IsNullOrWhiteSpace(u.Text)) continue;
            var i = text.IndexOf(u.Text, at, StringComparison.OrdinalIgnoreCase);
            if (i < 0) { missing.Add(u.Text); continue; }
            if (i > at) runs.Add(new Run(text[at..i]));
            runs.Add(new Run(text.Substring(i, u.Text.Length), Mark: true));
            at = i + u.Text.Length;
        }
        if (at < text.Length) runs.Add(new Run(text[at..]));
        if (runs.Count == 0) runs.Add(new Run(text.Length == 0 ? "(empty answer)" : text));
        if (missing.Count > 0) runs.Add(new Run(" (unsure words: " + string.Join(", ", missing) + ")", Italic: true));
        return runs;
    }

    // ------------------------------------------------------------------ formatting

    public static string CopyLabel(string audienceKey) => TranscriptAudiences.Parse(audienceKey) is { } a ? TranscriptAudiences.Label(a) : audienceKey;
    public static string Stamp(DateTimeOffset? at) => at is { } x ? x.UtcDateTime.ToString("yyyy-MM-dd HH:mm:ss", Ci) + " UTC" : "—";
    public static string Time(DateTimeOffset at) => at.UtcDateTime.ToString("HH:mm:ss", Ci);

    public static string Offset(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, Math.Round(seconds)));
        return t.TotalHours >= 1 ? $"+{(int)t.TotalHours}:{t.Minutes:00}:{t.Seconds:00}" : $"+{(int)t.TotalMinutes}:{t.Seconds:00}";
    }

    private static string Seconds(double s) => s.ToString("0.#", Ci) + " s";
    private static string Pct(double fraction) => (fraction * 100).ToString("0", Ci) + "%";
    private static string Capital(string s) => s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s[1..];
    private static string Join(string sep, params string?[] parts) => string.Join(sep, parts.Where(p => !string.IsNullOrWhiteSpace(p)));

    private static string JoinList(IReadOnlyList<string> items) => items.Count switch
    {
        0 => "",
        1 => items[0],
        _ => string.Join(", ", items.Take(items.Count - 1)) + " and " + items[^1],
    };
}
