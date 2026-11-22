using System.Text;
using System.Text.Json;
using Gadfly.Assessment;
using Gadfly.Engine;
using Gadfly.Server.Data;
using Gadfly.Server.Transcripts;
using Gadfly.Server.Verification;
using Gadfly.Sources.Verification;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>
/// The transcript maker (GAD-T295): the document built from the response ledger, what each audience's copy leaves out,
/// the canonical hash and verification code, and the renderers' basic shape. Pure: no database, no clock.
/// </summary>
[TestFixture]
public sealed class TranscriptTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 20, 14, 0, 0, TimeSpan.Zero);
    private static readonly Guid Org = Guid.NewGuid();
    // Fixed so two calls to Source() describe the same record: every entity's Id defaults to Guid.NewGuid(), and
    // TranscriptDocument.SessionId carries the session's actual Id into the hashed content (GAD-T333).
    private static readonly Guid SessionId = Guid.NewGuid();
    private static readonly Guid ExamId = Guid.NewGuid();
    private static readonly Guid StudentUserId = Guid.NewGuid();

    private static TranscriptSource Source()
    {
        var session = new ExamSession
        {
            Id = SessionId, OrganizationId = Org, ExamId = ExamId, StudentUserId = StudentUserId, Attempt = 2, State = SessionState.Ended,
            EndReason = nameof(EndReason.TimeUp), StartedAt = T0, EndedAt = T0.AddMinutes(12), CreatedAt = T0.AddMinutes(-1),
        };
        var t1 = new SessionTurn
        {
            OrganizationId = Org, SessionId = session.Id, Index = 0, ConceptKey = "osmosis", Move = nameof(MoveKind.Elicit),
            Question = "What is osmosis, in your own words?", PlannerReason = "Opens the concept.", AskedAt = T0.AddSeconds(5),
            AnswerText = "Water moves across a membrane.", AnswerMode = "voice", AnswerStartedAt = T0.AddSeconds(9), AnswerCompletedAt = T0.AddSeconds(15),
            SpeechMetricsJson = """{"spokenSeconds":5.5,"pauses.count":2,"pauses.longCount":1,"pauses.maxSeconds":1.8,"strain.isBaseline":1,"strain.baselineAnswers":2}""",
            MasteryAfter = 0.6,
        };
        var t2 = new SessionTurn
        {
            OrganizationId = Org, SessionId = session.Id, Index = 1, ConceptKey = "osmosis", Move = nameof(MoveKind.Probe),
            Question = "Why does the water move that way?", AskedAt = T0.AddSeconds(40),
            AnswerText = "Because of concentration.", AnswerMode = "text", AnswerStartedAt = T0.AddSeconds(50), AnswerCompletedAt = T0.AddSeconds(60),
        };
        var r1 = new StudentResponse
        {
            OrganizationId = Org, SessionId = session.Id, TurnId = t1.Id, Sequence = 1, Kind = ResponseKind.Original, Modality = "voice",
            OriginalText = "Water moves a cross a membrane.", SpeechRecognitionText = "Water moves a cross a membrane.",
            VerifiedText = "Water moves across a membrane.", VerificationState = VerificationState.Corrected, SubmittedAt = T0.AddSeconds(15),
        };
        r1.Versions.Add(new TranscriptVersion { OrganizationId = Org, ResponseId = r1.Id, Version = 1, Text = r1.OriginalText, Source = "speech-recognition", CreatedAt = T0.AddSeconds(15) });
        r1.Versions.Add(new TranscriptVersion { OrganizationId = Org, ResponseId = r1.Id, Version = 2, Text = r1.VerifiedText, CorrectionKind = CorrectionKind.Transcription, Source = "student", CreatedAt = T0.AddSeconds(25) });
        var r2 = new StudentResponse
        {
            OrganizationId = Org, SessionId = session.Id, TurnId = t2.Id, Sequence = 2, Kind = ResponseKind.Original, Modality = "text",
            OriginalText = "Because of concentration.", VerifiedText = "Because of concentration.", VerificationState = VerificationState.Clarified, SubmittedAt = T0.AddSeconds(60),
        };
        var readBack = new ClarificationEvent
        {
            OrganizationId = Org, SessionId = session.Id, ResponseId = r1.Id, Type = ClarificationType.Transcription,
            Question = "I recorded your answer as: \"Water moves a cross a membrane.\" Is this what you said?", StudentReply = "Water moves across a membrane.",
            AskedAt = T0.AddSeconds(16), AnsweredAt = T0.AddSeconds(25),
        };
        var meaning = new ClarificationEvent
        {
            OrganizationId = Org, SessionId = session.Id, ResponseId = r2.Id, Type = ClarificationType.Meaning,
            Question = "What do you mean by concentration here?", StudentReply = "How much is dissolved.", AskedAt = T0.AddSeconds(61), AnsweredAt = T0.AddSeconds(70),
        };
        var words = new List<TimedWord>
        {
            new("Water", TimeSpan.FromSeconds(0), TimeSpan.FromSeconds(0.5), 0.95), new("moves", TimeSpan.FromSeconds(0.6), TimeSpan.FromSeconds(1.0), 0.9),
            new("a", TimeSpan.FromSeconds(1.1), TimeSpan.FromSeconds(1.2), 0.4), new("cross", TimeSpan.FromSeconds(1.2), TimeSpan.FromSeconds(1.6), 0.45),
            new("a", TimeSpan.FromSeconds(1.7), TimeSpan.FromSeconds(1.8), 0.9), new("membrane.", TimeSpan.FromSeconds(1.9), TimeSpan.FromSeconds(2.6), 0.9),
        };
        var mark = new SpeechMark([new UncertainSegment("a cross", TimeSpan.FromSeconds(1.1), TimeSpan.FromSeconds(1.6), 0.4)], TranscriptChecked: true,
            Corrected: true, ChangedWords: 2, words);
        var integrity = new IntegrityEvent
        {
            OrganizationId = Org, SessionId = session.Id, Source = "webcam", Type = "offscreen-reading", StartedAt = T0.AddSeconds(10),
            Confidence = 0.42, Severity = IntegritySeverity.Low, Summary = "Eyes moved left to right for 6 seconds.", TurnId = t1.Id,
        };
        var judgment = new Judgment { OrganizationId = Org, TurnId = t1.Id, OverallScore = 0.7, Agreement = 0.9, Summary = "Mostly right.", TextStyleScore = 0.2, CreatedAt = T0.AddSeconds(30) };
        return new TranscriptSource
        {
            Session = session,
            Exam = new Exam { OrganizationId = Org, Title = "Cell biology oral", Subject = "Biology", Topic = "Osmosis", Mode = ExamMode.Voice },
            OrganizationName = "Test College",
            StudentName = "Ada (ada01)",
            ConceptTitles = new Dictionary<string, string> { ["osmosis"] = "Osmosis" },
            Turns = [t2, t1],   // out of order on purpose
            Responses = [r2, r1],
            Clarifications = [meaning, readBack],
            Marks = new Dictionary<Guid, SpeechMark> { [r1.Id] = mark },
            IntegrityEvents = [integrity],
            Judgments = [judgment],
        };
    }

    // ------------------------------------------------------------------ the builder

    [Test]
    public void Builds_turns_in_order_with_moves_times_and_offsets()
    {
        var d = TranscriptComposer.Compose(Source(), TranscriptAudience.Reviewer);
        Assert.Multiple(() =>
        {
            Assert.That(d.Turns.Select(t => t.Number), Is.EqualTo(new[] { 1, 2 }));
            Assert.That(d.Turns[0].Move, Is.EqualTo("Opening question"));
            Assert.That(d.Turns[0].MoveKind, Is.EqualTo("Elicit"));
            Assert.That(d.Turns[0].OffsetSeconds, Is.EqualTo(5));
            Assert.That(d.Turns[0].Concept, Is.EqualTo("Osmosis"));
            Assert.That(d.Mode, Is.EqualTo("Spoken"));
            Assert.That(d.EndReason, Is.EqualTo("Time ran out"));
        });
    }

    [Test]
    public void Keeps_the_answer_as_recorded_and_shows_each_correction_as_original_and_corrected()
    {
        var r = TranscriptComposer.Compose(Source(), TranscriptAudience.Student).Turns[0].Responses.Single();
        Assert.Multiple(() =>
        {
            Assert.That(r.AsRecorded, Is.EqualTo("Water moves a cross a membrane."));
            Assert.That(r.Verified, Is.EqualTo("Water moves across a membrane."));
            Assert.That(r.Corrections, Has.Count.EqualTo(1));
            Assert.That(r.Corrections[0].Original, Is.EqualTo("Water moves a cross a membrane."));
            Assert.That(r.Corrections[0].Corrected, Is.EqualTo("Water moves across a membrane."));
            Assert.That(r.ReadBack?.Outcome, Does.Contain("corrected"));
            Assert.That(r.DelaySeconds, Is.EqualTo(4));
            Assert.That(r.Pauses, Is.EqualTo(2));
            Assert.That(r.LongPauses, Is.EqualTo(1));
            Assert.That(r.UnsureWords.Single().Text, Is.EqualTo("a cross"));
            Assert.That(r.Words, Has.Count.EqualTo(6));
            Assert.That(r.CheckedByStudent, Is.True);
        });
    }

    [Test]
    public void Meaning_clarifications_are_kept_beside_the_answer()
    {
        var r = TranscriptComposer.Compose(Source(), TranscriptAudience.BlindGrader).Turns[1].Responses.Single();
        Assert.That(r.Clarifications.Single().Reply, Is.EqualTo("How much is dissolved."));
        Assert.That(r.Verified, Is.Null, "a clarification never changes the recorded answer");
    }

    [Test]
    public void A_turn_answered_before_the_ledger_existed_still_shows_its_answer()
    {
        var src = Source() with { Responses = [], Clarifications = [], Marks = new Dictionary<Guid, SpeechMark>() };
        var d = TranscriptComposer.Compose(src, TranscriptAudience.Reviewer);
        Assert.That(d.Turns[1].Responses.Single().AsRecorded, Is.EqualTo("Because of concentration."));
    }

    // ------------------------------------------------------------------ audience filtering

    [Test]
    public void Reviewer_copy_carries_integrity_notes_voice_measures_ai_scores_and_reasons()
    {
        var t = TranscriptComposer.Compose(Source(), TranscriptAudience.Reviewer).Turns[0];
        Assert.Multiple(() =>
        {
            Assert.That(t.Notes, Has.Count.EqualTo(1));
            Assert.That(t.Ai, Is.Not.Null);
            Assert.That(t.VoiceMeasurements, Is.Not.Null);
            Assert.That(t.ExaminerReason, Is.EqualTo("Opens the concept."));
            Assert.That(t.Responses[0].UnsureWords[0].LowestConfidence, Is.EqualTo(0.4));
        });
    }

    [TestCase(TranscriptAudience.Student)]
    [TestCase(TranscriptAudience.BlindGrader)]
    public void Student_and_blind_grader_copies_leave_out_reviewer_only_context(TranscriptAudience audience)
    {
        var d = TranscriptComposer.Compose(Source(), audience);
        Assert.Multiple(() =>
        {
            Assert.That(d.SessionNotes, Is.Empty);
            Assert.That(d.Turns.SelectMany(t => t.Notes), Is.Empty);
            Assert.That(d.Turns.Select(t => t.Ai), Has.All.Null);
            Assert.That(d.Turns.Select(t => t.VoiceMeasurements), Has.All.Null);
            Assert.That(d.Turns.Select(t => t.ExaminerReason), Has.All.Null);
            Assert.That(d.Turns.SelectMany(t => t.Responses).SelectMany(r => r.UnsureWords).Select(u => u.LowestConfidence), Has.All.Null);
            Assert.That(d.LeftOut, Is.Not.Empty);
        });
        // Nor anywhere in any rendering of it.
        var stamp = TranscriptHash.Stamp(d, T0, "https://gadfly.example");
        foreach (var f in new[] { TranscriptFormat.Json, TranscriptFormat.Html, TranscriptFormat.Txt })
        {
            var text = Encoding.UTF8.GetString(TranscriptFormats.Render(f, d, stamp));
            Assert.That(text, Does.Not.Contain("Eyes moved left to right"), f.ToString());
            Assert.That(text, Does.Not.Contain("Mostly right."), f.ToString());
            Assert.That(text, Does.Not.Contain("Opens the concept."), f.ToString());
        }
    }

    [Test]
    public void A_caller_can_ask_for_a_lesser_copy_but_never_a_greater_one()
    {
        Assert.Multiple(() =>
        {
            Assert.That(TranscriptAudiences.AtMost(TranscriptAudience.Reviewer, TranscriptAudience.Student), Is.EqualTo(TranscriptAudience.Student));
            Assert.That(TranscriptAudiences.AtMost(TranscriptAudience.Student, TranscriptAudience.Reviewer), Is.EqualTo(TranscriptAudience.Student));
            Assert.That(TranscriptAudiences.AtMost(TranscriptAudience.BlindGrader, TranscriptAudience.Reviewer), Is.EqualTo(TranscriptAudience.BlindGrader));
            Assert.That(TranscriptAudiences.AtMost(TranscriptAudience.BlindGrader, null), Is.EqualTo(TranscriptAudience.BlindGrader));
        });
    }

    // ------------------------------------------------------------------ the hash

    [Test]
    public void The_same_record_always_gives_the_same_hash_and_code()
    {
        var a = TranscriptHash.Stamp(TranscriptComposer.Compose(Source(), TranscriptAudience.Student), T0, "https://x");
        var b = TranscriptHash.Stamp(TranscriptComposer.Compose(Source(), TranscriptAudience.Student), T0.AddHours(5), "https://y");
        Assert.Multiple(() =>
        {
            Assert.That(a.Sha256, Is.EqualTo(b.Sha256), "the time a file is made is not part of the content");
            Assert.That(a.Code, Is.EqualTo(b.Code));
            Assert.That(a.Code, Does.Match("^[0-9A-HJKMNP-TV-Z]{4}(-[0-9A-HJKMNP-TV-Z]{4}){3}$"));
        });
    }

    [Test]
    public void Each_copy_has_its_own_code()
    {
        var src = Source();
        var codes = Enum.GetValues<TranscriptAudience>().Select(a => TranscriptHash.Stamp(TranscriptComposer.Compose(src, a), T0, "https://x").Code).ToList();
        Assert.That(codes.Distinct().Count(), Is.EqualTo(3));
    }

    [Test]
    public void Canonical_form_ignores_key_order_and_whitespace_but_not_a_changed_word()
    {
        var d = TranscriptComposer.Compose(Source(), TranscriptAudience.Student);
        var canonical = TranscriptHash.Canonical(d);
        var indented = JsonSerializer.Serialize(d, new JsonSerializerOptions(TranscriptHash.Json) { WriteIndented = true });
        using var reparsed = JsonDocument.Parse(indented);
        Assert.That(TranscriptHash.Canonical(reparsed.RootElement), Is.EqualTo(canonical));

        using var shuffled = JsonDocument.Parse("""{"b":1,"a":{"y":true,"x":null,"w":[2,1]}}""");
        Assert.That(TranscriptHash.Canonical(shuffled.RootElement), Is.EqualTo("""{"a":{"w":[2,1],"y":true},"b":1}"""));

        var edited = d with { Turns = [d.Turns[0] with { Question = d.Turns[0].Question + "!" }, .. d.Turns.Skip(1)] };
        Assert.That(TranscriptHash.Sha256(TranscriptHash.Canonical(edited)), Is.Not.EqualTo(TranscriptHash.Sha256(canonical)));
    }

    [Test]
    public void The_json_download_verifies_from_its_transcript_property()
    {
        var d = TranscriptComposer.Compose(Source(), TranscriptAudience.Reviewer);
        var stamp = TranscriptHash.Stamp(d, T0, "https://gadfly.example");
        using var file = JsonDocument.Parse(TranscriptJson.Render(d, stamp));
        var sha = TranscriptHash.Sha256(TranscriptHash.Canonical(file.RootElement.GetProperty("transcript")));
        Assert.Multiple(() =>
        {
            Assert.That(sha, Is.EqualTo(stamp.Sha256));
            Assert.That(file.RootElement.GetProperty("verification").GetProperty("code").GetString(), Is.EqualTo(stamp.Code));
        });
    }

    [TestCase("7k3m 9qxa b2cd efgh", "7K3M-9QXA-B2CD-EFGH")]
    [TestCase("7K3M-9QXA-B2CD-EFGH", "7K3M-9QXA-B2CD-EFGH")]
    [TestCase("OIL0-0000-0000-0000", "0110-0000-0000-0000")]
    [TestCase("7K3M-9QXA-B2CD", null)]
    [TestCase("7K3M-9QXA-B2CD-EFGU", null)]
    [TestCase("", null)]
    public void Typed_codes_are_normalized(string typed, string? expected) =>
        Assert.That(TranscriptHash.NormalizeCode(typed), Is.EqualTo(expected));

    [Test]
    public void The_code_is_the_first_80_bits_of_the_hash_in_crockford_base32()
    {
        Assert.Multiple(() =>
        {
            Assert.That(TranscriptHash.Code(new string('0', 64)), Is.EqualTo("0000-0000-0000-0000"));
            Assert.That(TranscriptHash.Code(new string('f', 64)), Is.EqualTo("ZZZZ-ZZZZ-ZZZZ-ZZZZ"));
            Assert.That(TranscriptHash.Code("0842" + new string('0', 60)), Is.EqualTo("1110-0000-0000-0000"));
        });
    }

    // ------------------------------------------------------------------ renderers

    [Test]
    public void Captions_use_word_timings_and_never_overlap()
    {
        var d = TranscriptComposer.Compose(Source() with { Responses = [] }, TranscriptAudience.Student);
        var cues = TranscriptCaptions.Cues(d);
        Assert.Multiple(() =>
        {
            Assert.That(cues, Is.Not.Empty);
            for (var i = 1; i < cues.Count; i++) Assert.That(cues[i].Start, Is.GreaterThanOrEqualTo(cues[i - 1].End - 1e-9));
            Assert.That(TranscriptCaptions.Vtt(d, TranscriptHash.Stamp(d, T0, "https://x")), Does.StartWith("WEBVTT"));
            Assert.That(TranscriptCaptions.Srt(d), Does.Contain(" --> "));
            Assert.That(TranscriptCaptions.Ts(3723.5, ','), Is.EqualTo("01:02:03,500"));
        });
    }

    [Test]
    public void Pdf_and_docx_are_real_files()
    {
        var d = TranscriptComposer.Compose(Source(), TranscriptAudience.Reviewer);
        var stamp = TranscriptHash.Stamp(d, T0, "https://gadfly.example");
        var pdf = TranscriptFormats.Render(TranscriptFormat.Pdf, d, stamp);
        var docx = TranscriptFormats.Render(TranscriptFormat.Docx, d, stamp);
        Assert.Multiple(() =>
        {
            Assert.That(Encoding.ASCII.GetString(pdf, 0, 8), Is.EqualTo("%PDF-1.4"));
            Assert.That(Encoding.ASCII.GetString(pdf[^6..]).Trim(), Is.EqualTo("%%EOF"));
            Assert.That(docx[0..2], Is.EqualTo("PK"u8.ToArray()));
        });
        using var zip = new System.IO.Compression.ZipArchive(new MemoryStream(docx));
        Assert.That(zip.Entries.Select(e => e.FullName), Does.Contain("word/document.xml").And.Contain("[Content_Types].xml"));
        using var reader = new StreamReader(zip.GetEntry("word/document.xml")!.Open());
        System.Xml.Linq.XDocument.Parse(reader.ReadToEnd());   // well-formed
    }

    [Test]
    public void Unsure_words_are_marked_in_place()
    {
        var runs = TranscriptLayout.Marked("Water moves a cross a membrane.", [new TranscriptUnsure("a cross", 1.1, 1.6, null)]);
        Assert.That(runs.Where(r => r.Mark).Select(r => r.Text), Is.EqualTo(new[] { "a cross" }));
        Assert.That(string.Concat(runs.Select(r => r.Text)), Is.EqualTo("Water moves a cross a membrane."));
    }
}
