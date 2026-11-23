using System.Reflection;
using Gadfly.Engine;
using Gadfly.Server.Data;
using Gadfly.Server.Sessions;
using Gadfly.Server.Student;
using Gadfly.Server.Tenancy;
using Gadfly.Server.Voice;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>
/// "Meet your examiner" (GAD-T294): the practice walks its fixed steps in memory and never touches the database: no session,
/// attempt, turn, integrity note or audit row, and the student's attempts on their real exams are unchanged.
/// </summary>
[TestFixture]
public class PracticeInterviewTests
{
    private static readonly DateTimeOffset T0 = new(2026, 11, 22, 9, 0, 0, TimeSpan.Zero);

    [Test]
    public void Steps_TeachTheExamAsItIsNow_EndingWithYoureReady()
    {
        Assert.That(PracticeInterview.Steps.Select(s => s.Key), Is.EqualTo(new[]
        {
            PracticeInterview.Welcome, PracticeInterview.AsHeard, PracticeInterview.DontKnow,
            PracticeInterview.EyeContact, PracticeInterview.Pause, PracticeInterview.Ready,
        }));
        Assert.That(PracticeInterview.Steps.Take(5).All(s => s.WantsAnswer), Is.True);
        Assert.That(PracticeInterview.Steps[^1].WantsAnswer, Is.False);
        Assert.That(PracticeInterview.Steps.All(s => s.Points.Count > 0 && s.Title.Length > 0), Is.True);
    }

    [Test]
    public void NoLine_TeachesTheRemovedReadBack()
    {
        // GAD-T405: a spoken answer is taken as heard; nothing is read back, confirmed, corrected or changed.
        var all = PracticeInterview.AllLines().Concat(PracticeInterview.Steps.SelectMany(s => s.Points)).ToList();
        Assert.That(all, Has.None.Contains("Is this what you said"));
        Assert.That(all, Has.None.Contains("I recorded your answer as"));
        Assert.That(all.Any(l => l.Contains("taken as", StringComparison.OrdinalIgnoreCase)), Is.True);
        Assert.That(PracticeInterview.AllLines().All(l => !string.IsNullOrWhiteSpace(l) && l.Length < 1200), Is.True);
    }

    [Test]
    public void NoLine_TeachesTheOldFadeOrPageTurns()
    {
        // GAD-T624: the practice teaches the exam's reminder ladder (GAD-T557/T561) and thinking-gesture wait (GAD-T559), in every language.
        var all = PracticeInterview.AllLines()
            .Concat(PracticeInterview.Steps.Concat(PracticeInterview.KoreanSteps).SelectMany(s => s.Points.Append(s.Title))).ToList();
        string[] old = ["pages turning", "Please look at the examiner", "screen slowly fades", "screen fade", "시험관을 봐 주세요", "흐려", "조용한 소리"];
        foreach (var o in old) Assert.That(all, Has.None.Contains(o), o);
        Assert.That(all.Any(l => l.Contains("glance aside")), Is.True);
        Assert.That(PracticeInterview.ThinkingPause, Is.GreaterThan(Gadfly.Server.Voice.ExaminerFiller.FirstAfter + TimeSpan.FromSeconds(2)));
    }

    [Test]
    public void Answer_MovesOneStep_AndIgnoresAnAnswerToAnEarlierStep()
    {
        var p = new PracticeInterview();
        var first = p.Turn;
        Assert.That(p.Answer(first, "Biology"), Is.True);
        Assert.That(p.Current.Key, Is.EqualTo(PracticeInterview.AsHeard));
        Assert.That(p.Answer(first, "late answer to the first step"), Is.False);
        Assert.That(p.Current.Key, Is.EqualTo(PracticeInterview.AsHeard));
    }

    [Test]
    public void IDontKnow_ChangesOnlyTheExaminersNextWords()
    {
        var p = new PracticeInterview();
        p.Answer(p.Turn, "speech");
        p.Answer(p.Turn, "speech");
        Assert.That(p.Current.Key, Is.EqualTo(PracticeInterview.DontKnow));
        p.Answer(p.Turn, "Honestly, I don't know.");
        Assert.That(p.SaidIDontKnow, Is.True);
        Assert.That(p.Line, Does.StartWith(PracticeInterview.AporiaThanks));

        var q = new PracticeInterview();
        q.Next();
        q.Next();
        q.Answer(q.Turn, "Ouagadougou");
        Assert.That(q.Line, Does.StartWith(PracticeInterview.AnsweredThanks));
    }

    [TestCase("aporia", true)]
    [TestCase("I don’t know", true)]
    [TestCase("no idea, sorry", true)]
    [TestCase("I'm not sure", true)]
    [TestCase("Ouagadougou", false)]
    [TestCase("", false)]
    [TestCase(null, false)]
    public void SoundsLikeIDontKnow(string? heard, bool expected) =>
        Assert.That(PracticeInterview.SoundsLikeIDontKnow(heard), Is.EqualTo(expected));

    [Test]
    public void LastStep_TakesNoAnswer_AndRestartBeginsAgain()
    {
        var p = new PracticeInterview();
        while (p.Next()) { }
        Assert.That(p.IsFinished, Is.True);
        Assert.That(p.WantsAnswer, Is.False);
        Assert.That(p.Answer(p.Turn, "hello"), Is.False);
        var turn = p.Turn;
        p.Restart();
        Assert.That(p.StepIndex, Is.Zero);
        Assert.That(p.Turn, Is.Not.EqualTo(turn), "a restart is a new turn, so the examiner speaks the first step again");
        Assert.That(p.AnswersGiven, Is.Zero);
    }

    [Test]
    public void Clock_StopsWhilePaused_GivesTimeBack_UpToTheAllowance()
    {
        var c = new PracticeClock(TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(2));
        c.Pause(T0);   // before the examiner spoke: nothing to stop
        Assert.That(c.IsPaused, Is.False);
        c.Start(T0);
        c.Pause(T0.AddMinutes(1));
        Assert.That(c.Remaining(T0.AddMinutes(2)), Is.EqualTo(TimeSpan.FromMinutes(9)), "stopped while paused");
        c.Resume(T0.AddMinutes(2));
        Assert.That(c.Credited, Is.EqualTo(TimeSpan.FromMinutes(1)));
        c.Pause(T0.AddMinutes(3));
        c.Resume(T0.AddMinutes(8));   // five minutes, but only one more is left of the allowance
        Assert.That(c.Credited, Is.EqualTo(TimeSpan.FromMinutes(2)));
        Assert.That(c.Remaining(T0.AddMinutes(8)), Is.EqualTo(TimeSpan.FromMinutes(4)));
        Assert.That(c.Remaining(T0.AddHours(1)), Is.EqualTo(TimeSpan.Zero));
    }

    [Test]
    public void ThePractice_HasNoWayToReachTheDatabase()
    {
        static bool IsDb(Type t) => typeof(DbContext).IsAssignableFrom(t) || t == typeof(IServiceProvider) || t == typeof(IServiceScopeFactory);
        foreach (var type in new[] { typeof(PracticeInterview), typeof(PracticeClock) })
        {
            const BindingFlags all = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            Assert.That(type.GetFields(all).Select(f => f.FieldType).Where(IsDb), Is.Empty, type.Name + " fields");
            Assert.That(type.GetProperties(all).Select(p => p.PropertyType).Where(IsDb), Is.Empty, type.Name + " properties");
            Assert.That(type.GetConstructors(all).SelectMany(c => c.GetParameters()).Select(p => p.ParameterType).Where(IsDb), Is.Empty, type.Name + " constructors");
            Assert.That(type.GetMethods(all).SelectMany(m => m.GetParameters()).Select(p => p.ParameterType).Where(IsDb), Is.Empty, type.Name + " methods");
        }
    }

    [Test]
    public void GazeEpisodes_AreDropped_AndTheCheckKeepsRunning()
    {
        var episode = new HubIntegrityEvent("webcam", "examiner-look-away", T0, T0.AddSeconds(6), 0.3, "q:80;m:head");
        Assert.That(PracticeInterview.DropGazeEpisodes([episode]), Is.True);
        Assert.That(PracticeInterview.DropGazeEpisodes(null), Is.True);
    }
}

/// <summary>The practice against the real server and database: a whole run leaves no trace and uses no attempt (GAD-T294).</summary>
[TestFixture]
public class PracticeInterviewServerTests : ServerTest
{
    private sealed record Counts(int Sessions, int Turns, int Integrity, int Audit);

    private static async Task<Counts> CountAsync(Guid org)
    {
        await using var db = OrgDb(org);
        return new Counts(
            await db.ExamSessions.CountAsync(s => s.OrganizationId == org),
            await db.SessionTurns.CountAsync(t => t.OrganizationId == org),
            await db.IntegrityEvents.CountAsync(e => e.OrganizationId == org),
            await db.AuditEvents.CountAsync(e => e.OrganizationId == org));
    }

    [Test]
    public async Task AWholePractice_WritesNothing_AndCountsNoAttempt()
    {
        var w = await Seed.ExamAsync(attempts: 1);
        var before = await CountAsync(w.Org);
        var grants = Services.GetRequiredService<VoiceGrants>();
        var practice = new PracticeInterview();

        // What the practice page and the real VoiceRoom do around a practice: arm the hubs, speak each line, answer every step
        // (the same read-only strain lookup VoiceRoom makes for each spoken answer), drop gaze episodes, say an eye-contact remark.
        grants.Arm(w.StudentId, w.Org, practice.Id);
        practice.Clock.Start(Clock.GetUtcNow());
        while (true)
        {
            Assert.That(grants.Offer(w.StudentId, practice.Id, practice.Line), Is.True);
            if (!practice.WantsAnswer) break;
            await using (var scope = Scope(TenantSnapshot.ForOrganization(w.Org)))
            {
                var db = scope.ServiceProvider.GetRequiredService<GadflyDbContext>();
                var answer = new Answer
                {
                    Text = "speech", Mode = "voice", CompletedAt = Clock.GetUtcNow(),
                    SpeechMetrics = new Dictionary<string, double> { ["wordsPerMinute"] = 120 },
                };
                await VoiceStrain.ApplyAsync(db, practice.Id, answer, CancellationToken.None);
                Assert.That(db.ChangeTracker.HasChanges(), Is.False);
            }
            Assert.That(PracticeInterview.DropGazeEpisodes([new HubIntegrityEvent("webcam", "examiner-fade", Clock.GetUtcNow(), Clock.GetUtcNow(), 0.3, "peak:80")]), Is.True);
            Assert.That(practice.Answer(practice.Turn, "speech"), Is.True);
        }
        Assert.That(practice.Remark(grants, w.StudentId, "look-away"), Is.Not.Null);
        Assert.That(practice.IsFinished, Is.True);
        grants.Disarm(w.StudentId, practice.Id);

        Assert.That(await CountAsync(w.Org), Is.EqualTo(before), "no session, turn, integrity note or audit row");
        await using (var db = SystemDb())
        {
            Assert.That(await db.ExamSessions.AnyAsync(s => s.Id == practice.Id), Is.False);
            Assert.That(await db.ExamSessions.AnyAsync(s => s.StudentUserId == w.StudentId), Is.False);
            Assert.That(await db.IntegrityEvents.AnyAsync(e => e.SessionId == practice.Id), Is.False);
        }
        var mine = await StudentExams.ListAsync(Services.GetRequiredService<IServiceScopeFactory>(), w.Org, w.StudentId, Clock.GetUtcNow());
        var row = mine.Single(r => r.AssignmentId == w.AssignmentId);
        Assert.That(row.AttemptsUsed, Is.Zero);
        Assert.That(row.Standing, Is.EqualTo(AssignmentStanding.Open));
        Assert.That(row.ActiveSessionId, Is.Null);
    }
}
