# Gadfly.Sources

Instructor source material for the Gadfly Socratic engine (depends on **Gadfly.Core**).

- `SourceIngestor` reads `.txt`/`.md` (and PDF, DOCX, EPUB and HTML through Tutor.Ingestion) into
  passages with locations.
- `ConceptMapBuilder` and `RubricBuilder` have the panel extract concepts and write a rubric per
  concept; `GroundingRetriever` finds the passages an answer touches.
- `SourceMaterialSubject` is the ready-made `ISubjectMatter` over all of it.
- `SourceExamEngine` is the resumable, step-driven exam (`NextQuestionAsync` / `SubmitAnswerAsync` /
  `Report`); `SourceExamRunner` drives it in a loop against an `IExamConversation` (your UI).

Pass `new MockVotingClient()` (from Gadfly.Core, `Gadfly.Socrates.Mock`) as the panel to run the whole
thing offline with no keys and no cost.

Runnable examples: <https://github.com/mindattic/Gadfly/tree/main/samples>.
