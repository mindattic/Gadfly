using Gadfly.Server.Data;
using Gadfly.Server.Tenancy;
using Gadfly.Server.Voice;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>
/// The exam's own vocabulary, computed server-side from learning objectives and their linked concepts (GAD-T358).
/// Needs the shared test database (<see cref="ServerFixture"/>); ignored when it is unavailable (no LocalDB).
/// </summary>
[TestFixture]
public class VoiceVocabularyTests
{
    [SetUp]
    public void SkipIfServerUnavailable()
    {
        if (ServerFixture.Unavailable is { } why) Assert.Ignore($"Server tests unavailable: {why}");
    }

    [Test]
    public async Task Vocabulary_is_objective_titles_and_their_concepts_titles_deduplicated_and_capped()
    {
        var world = await Seed.ExamAsync();
        await using (var db = ServerFixture.NewContext(TenantSnapshot.ForOrganization(world.Org)))
        {
            db.Concepts.Add(new Concept
            {
                OrganizationId = world.Org, CollectionId = world.CollectionId, Key = "mitosis", Title = "Mitosis", Definition = "Cell division.",
            });
            db.LearningObjectives.Add(new LearningObjective
            {
                OrganizationId = world.Org, ExamId = world.ExamId, Key = "obj-1", Title = "Explain mitosis", ConceptKey = "mitosis", Ordinal = 0,
            });
            // A second objective naming the same concept title again: the result is deduplicated, not doubled.
            db.LearningObjectives.Add(new LearningObjective
            {
                OrganizationId = world.Org, ExamId = world.ExamId, Key = "obj-2", Title = "Mitosis", ConceptKey = "mitosis", Ordinal = 1,
            });
            await db.SaveChangesAsync();
        }

        await using var read = ServerFixture.NewContext(TenantSnapshot.ForOrganization(world.Org));
        var vocabulary = await VoiceVocabulary.ForSessionAsync(read, world.Org, await SessionIdAsync(world));

        Assert.That(vocabulary, Is.EquivalentTo(new[] { "Explain mitosis", "Mitosis" }));
    }

    [Test]
    public async Task An_exam_without_learning_objectives_has_no_vocabulary()
    {
        // Seed.ExamAsync's default world is concept-only (the older exam builder): no LearningObjective rows.
        var world = await Seed.ExamAsync();
        await using var db = ServerFixture.NewContext(TenantSnapshot.ForOrganization(world.Org));
        var vocabulary = await VoiceVocabulary.ForSessionAsync(db, world.Org, await SessionIdAsync(world));
        Assert.That(vocabulary, Is.Empty);
    }

    [Test]
    public async Task A_session_from_another_organization_gets_nothing()
    {
        var world = await Seed.ExamAsync();
        await using var db = ServerFixture.NewContext(TenantSnapshot.ForOrganization(world.Org));
        var vocabulary = await VoiceVocabulary.ForSessionAsync(db, Guid.NewGuid(), await SessionIdAsync(world));
        Assert.That(vocabulary, Is.Empty);
    }

    /// <summary>A started session for the world's assignment, since <see cref="VoiceVocabulary"/> reads from ExamSessions.</summary>
    private static async Task<Guid> SessionIdAsync(Seed.ExamWorld world)
    {
        await using var db = ServerFixture.NewContext(TenantSnapshot.ForOrganization(world.Org));
        var session = new ExamSession
        {
            OrganizationId = world.Org, AssignmentId = world.AssignmentId, ExamId = world.ExamId, StudentUserId = world.StudentId,
            State = SessionState.Running, CreatedAt = DateTimeOffset.UtcNow, TranscriptJson = "{}", LedgerJson = "{}",
        };
        db.ExamSessions.Add(session);
        await db.SaveChangesAsync();
        return session.Id;
    }
}
