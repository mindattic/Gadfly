using Gadfly.Server.Data;
using Gadfly.Server.Sources;
using Gadfly.Sources;
using Microsoft.EntityFrameworkCore;
using NUnit.Framework;

namespace Gadfly.Tests.Server;

/// <summary>The SQL source store (GAD-T187): replacing a document while concepts cite its passages.</summary>
[TestFixture]
public class SqlSourceStoreTests : ServerTest
{
    [Test]
    public async Task ReIngestingAChangedDocument_KeepsCitationsToPassagesThatStillExist_AndDropsTheRest()
    {
        var w = await Seed.ExamAsync();
        Guid keepChunk, dropChunk, conceptId;
        await using (var db = OrgDb(w.Org))
        {
            var chunks = await db.SourceChunks.AsNoTracking().Where(c => c.CollectionId == w.CollectionId).OrderBy(c => c.Ordinal).ToListAsync();
            keepChunk = chunks[0].Id;
            dropChunk = chunks[2].Id;
            conceptId = await db.Concepts.Where(c => c.CollectionId == w.CollectionId && c.Key == "photosynthesis").Select(c => c.Id).SingleAsync();
            db.ConceptCitations.Add(new ConceptCitation { OrganizationId = w.Org, ConceptId = conceptId, ChunkId = keepChunk, Quote = "light energy" });
            db.ConceptCitations.Add(new ConceptCitation { OrganizationId = w.Org, ConceptId = conceptId, ChunkId = dropChunk });
            await db.SaveChangesAsync();
        }

        await using (var db = OrgDb(w.Org))
        {
            var store = new SqlSourceStore(db, w.Org, Guid.Empty);
            Assert.DoesNotThrowAsync(() => store.SaveDocumentAsync(w.CollectionId.ToString(), new SourceDocumentInfo
            {
                Id = "doc", FileName = "biology.md", Sha256 = new string('b', 64), PassageCount = 2, IngestedAt = DateTimeOffset.UtcNow,
            }, [Seed.Passages[0] with { Text = "Photosynthesis, revised." }, Seed.Passages[1]]),
                "the chunks of a replaced document are cited by concepts through a foreign key");
        }

        await using var check = SystemDb();
        var kept = await check.ConceptCitations.AsNoTracking().Where(c => c.ConceptId == conceptId).ToListAsync();
        Assert.That(kept.Select(c => c.ChunkId), Is.EqualTo(new[] { keepChunk }));
        Assert.That(kept[0].Quote, Is.EqualTo("light energy"));
        Assert.That(await check.SourceChunks.CountAsync(c => c.CollectionId == w.CollectionId), Is.EqualTo(2));
    }

    /// <summary>GAD-T197: saving the map again (an instructor's edit) rewrites each concept's citations; the new rows must be inserts.</summary>
    [Test]
    public async Task SavingAConceptMap_ThatCitesPassages_ReplacesTheCitationsAsNewRows_EvenTwice()
    {
        var w = await Seed.ExamAsync();
        string chunk;
        await using (var db = OrgDb(w.Org))
            chunk = (await db.SourceChunks.AsNoTracking().Where(c => c.CollectionId == w.CollectionId).OrderBy(c => c.Ordinal).Select(c => c.Id).FirstAsync()).ToString("N");

        for (var round = 1; round <= 2; round++)
        {
            await using var db = OrgDb(w.Org);
            var store = new SqlConceptMapStore(db, w.Org);
            var map = (await store.LoadMapAsync(w.CollectionId.ToString()))!;
            var edited = map with { Concepts = map.Concepts.Select(c => c with { PassageIds = [chunk], EditedByInstructor = true, Definition = $"{c.Definition} (edit {round})" }).ToList() };
            Assert.DoesNotThrowAsync(() => store.SaveMapAsync(edited), $"round {round}: the citations are replaced, not updated");
        }

        await using var check = SystemDb();
        var concepts = await check.Concepts.AsNoTracking().CountAsync(c => c.CollectionId == w.CollectionId && !c.IsArchived);
        var citations = await check.ConceptCitations.AsNoTracking().Where(c => check.Concepts.Any(k => k.Id == c.ConceptId && k.CollectionId == w.CollectionId)).ToListAsync();
        Assert.That(citations, Has.Count.EqualTo(concepts), "one citation per concept, none left over from the first save");
    }
}
