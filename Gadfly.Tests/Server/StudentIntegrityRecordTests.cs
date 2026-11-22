using Gadfly.Server.Data;
using Gadfly.Server.Integrity;
using Gadfly.Server.Review;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>The student's own view of the integrity notes on their attempt, and their response in an appeal (GAD-T450). Pure: no database.</summary>
[TestFixture]
public class StudentIntegrityRecordTests
{
    private static readonly DateTimeOffset Start = DateTimeOffset.Parse("2026-11-20T10:00:00Z");

    private static IntegrityEvent Note(string type, int atSecond, int? forSeconds, IntegritySeverity severity, Guid? turn = null, string source = "webcam") => new()
    {
        Source = source, Type = type, StartedAt = Start.AddSeconds(atSecond),
        EndedAt = forSeconds is { } f ? Start.AddSeconds(atSecond + f) : null,
        Severity = severity, Summary = "server words", Confidence = 0.3, TurnId = turn,
    };

    [Test]
    public void The_student_reads_the_same_explanation_the_reviewer_reads()
    {
        var view = StudentIntegrityRecord.Build([Note("examiner-look-away", 65, 12, IntegritySeverity.Low)], new Dictionary<Guid, int>(), Start);
        var note = view.Notes.Single();
        var (label, explanation, _) = IntegrityExplanations.For("examiner-look-away", "webcam");
        Assert.That(note.Label, Is.EqualTo(label));
        Assert.That(note.Explanation, Is.EqualTo(explanation));
        Assert.That(note.When, Is.EqualTo("1 min 05 s into the exam, for 12 s"));
    }

    [Test]
    public void Ordinary_uncounted_notes_are_only_counted_and_the_rest_keep_their_question()
    {
        var turn = Guid.NewGuid();
        var view = StudentIntegrityRecord.Build(
            [Note("thinking-glance", 10, 2, IntegritySeverity.Info), Note("thinking-glance", 20, 2, IntegritySeverity.Info),
             Note("multiple-faces", 30, 5, IntegritySeverity.Low, turn)],
            new Dictionary<Guid, int> { [turn] = 3 }, Start);
        Assert.That(view.Ordinary.Single().Count, Is.EqualTo(2));
        Assert.That(view.Notes.Single().Question, Is.EqualTo(3));
        Assert.That(view.IsEmpty, Is.False);
    }

    [Test]
    public void Nothing_recorded_is_an_empty_view() =>
        Assert.That(StudentIntegrityRecord.Build([], new Dictionary<Guid, int>(), Start).IsEmpty, Is.True);

    [Test]
    public void A_note_before_the_recorded_start_never_shows_a_negative_time() =>
        Assert.That(StudentIntegrityRecord.When(Note("tab-hidden", -30, null, IntegritySeverity.Low, source: "browser"), Start),
            Is.EqualTo("0 min 00 s into the exam"));

    [TestCase("The grade is wrong.", "", "The grade is wrong.")]
    [TestCase("", "My cat jumped up.", StudentIntegrityRecord.ResponseHeading + "\nMy cat jumped up.")]
    [TestCase("The grade is wrong.", "My cat jumped up.", "The grade is wrong.\n\n" + StudentIntegrityRecord.ResponseHeading + "\nMy cat jumped up.")]
    public void The_response_rides_in_the_appeal_under_a_plain_heading(string reason, string response, string expected) =>
        Assert.That(StudentIntegrityRecord.AppealText(reason, response), Is.EqualTo(expected));
}
