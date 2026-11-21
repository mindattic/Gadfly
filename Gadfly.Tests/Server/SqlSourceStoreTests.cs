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
}
