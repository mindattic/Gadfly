using Gadfly.Server.Analytics;
using Gadfly.Sources.Verification;
using NUnit.Framework;

namespace Gadfly.Tests.Analytics;

/// <summary>
/// Human-reference transcripts for word error rate (GAD-T426): the sample spreads across disfluency bands and students and never
/// redraws a referenced answer, the stored reference is read back (withdrawn and redacted ones are not), and word error rate is
/// measured against a human reference when one exists. Pure.
/// </summary>
[TestFixture]
public class ReferenceTranscriptsTests
{
    private static List<ReferenceCandidate> Candidates(string band, int students, int answersEach) =>
        Enumerable.Range(0, students).SelectMany(_ =>
        {
            var id = Guid.NewGuid();
            return Enumerable.Range(0, answersEach).Select(_ => new ReferenceCandidate(Guid.NewGuid(), id, band));
        }).ToList();

    [Test]
    public void The_sample_spreads_across_bands()
    {
        var fluent = Candidates(SpeechAccuracy.FluentBand, 20, 3);
        var frequent = Candidates(SpeechAccuracy.FrequentBand, 2, 3);
        var picked = ReferenceTranscripts.Sample(fluent.Concat(frequent), new HashSet<Guid>(), 10, seed: 7);
        Assert.That(picked, Has.Count.EqualTo(10));
        Assert.That(picked.Count(p => frequent.Any(c => c.ResponseId == p)), Is.EqualTo(5), "the small band gets an equal turn until it runs out");
    }

    [Test]
    public void The_sample_spreads_across_students_before_repeating_one()
    {
        var band = Candidates(SpeechAccuracy.FluentBand, 6, 4);
        var picked = ReferenceTranscripts.Sample(band, new HashSet<Guid>(), 6, seed: 3);
        var students = picked.Select(p => band.Single(c => c.ResponseId == p).Student).Distinct().Count();
        Assert.That(students, Is.EqualTo(6));
    }

    [Test]
    public void Referenced_answers_are_never_drawn_again_and_a_seed_repeats_its_draw()
    {
        var band = Candidates(SpeechAccuracy.FluentBand, 3, 2);
        var done = new HashSet<Guid>(band.Take(4).Select(c => c.ResponseId));
        var picked = ReferenceTranscripts.Sample(band, done, 10, seed: 1);
        Assert.That(picked, Has.Count.EqualTo(2));
        Assert.That(picked, Has.None.Matches<Guid>(done.Contains));
        Assert.That(ReferenceTranscripts.Sample(band, new HashSet<Guid>(), 4, 9), Is.EqualTo(ReferenceTranscripts.Sample(band, new HashSet<Guid>(), 4, 9)));
    }

    [Test]
    public void A_stored_reference_is_read_back_and_a_withdrawn_or_redacted_one_is_not()
    {
        var r = ReferenceTranscripts.Parse("{\"reference\":\"the light reaction splits water\",\"how\":\"present\",\"consentConfirmed\":true}");
        Assert.That(r!.Text, Is.EqualTo("the light reaction splits water"));
        Assert.That(r.How, Is.EqualTo(ReferenceTranscripts.HowPresent));
        Assert.That(ReferenceTranscripts.Parse("{\"withdrawn\":true}"), Is.Null);
        Assert.That(ReferenceTranscripts.Parse(null), Is.Null, "retention clears the JSON");
        Assert.That(ReferenceTranscripts.Parse("not json"), Is.Null);
    }

    [Test]
    public void A_reference_is_cleaned_and_bounded()
    {
        Assert.That(ReferenceTranscripts.Clean("  the\tlight \n reaction  "), Is.EqualTo("the light reaction"));
        Assert.That(ReferenceTranscripts.Clean("   "), Is.Null);
        Assert.That(ReferenceTranscripts.Clean(new string('a', ReferenceTranscripts.MaxLength + 5))!.Length, Is.EqualTo(ReferenceTranscripts.MaxLength));
    }

    [Test]
    public void Word_error_rate_is_measured_against_the_human_reference()
    {
        var samples = Enumerable.Range(0, 5).Select(i => new SpeechSample($"s{i}", "the light reaction splits water", "the light reactions split the water",
            TranscriptChecked: true, Corrected: false, Words: 0, LowConfidenceWords: 0, MeanConfidence: null, HumanReference: true)).ToList();
        var g = SpeechAccuracy.Summarize("A", samples);
        Assert.That(g.HumanReferenced, Is.EqualTo(5));
        Assert.That(g.CheckedResponses, Is.EqualTo(5));
        // reference "the light reactions split the water" (6 words) against "the light reaction splits water": 2 substitutions, 1 deletion
        Assert.That(g.WordErrorRate, Is.EqualTo(3 / 6.0).Within(1e-9));
    }

    [Test]
    public void Answers_without_a_reference_add_no_word_error_rate()
    {
        var samples = Enumerable.Range(0, 5).Select(i => new SpeechSample($"s{i}", "a b c", "a b c", false, false, 3, 1, 0.8)).ToList();
        var g = SpeechAccuracy.Summarize("A", samples);
        Assert.That(g.WordErrorRate, Is.Null);
        Assert.That(g.HumanReferenced, Is.EqualTo(0));
        Assert.That(g.LowConfidenceShare, Is.EqualTo(1 / 3.0).Within(1e-9), "the unsure-word share still covers every spoken answer");
    }
}
