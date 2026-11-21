# Sample: Gadfly in your own process

A console app that uses the Socratic engine as a library, with the **mock panel**: no keys, no network,
no cost.

```powershell
dotnet run
```

What it does, in `Program.cs`:

1. `SourceIngestor` reads `multiplication.md` into passages (a throwaway store in your temp folder).
2. `ConceptMapBuilder` and `RubricBuilder` have the panel find the concepts and write a rubric each.
3. `SourceMaterialSubject` (an `ISubjectMatter`) wraps them as what the session examines.
4. `SourceExamRunner` runs the session. Your `IExamConversation` is the student side: here a script of
   canned answers; in your app, your UI.
5. The final `SessionReport` is printed (`ToMarkdown()` and `ToJson()` give it to you in full).

It references **Gadfly.Core** and **Gadfly.Sources** as `PackageReference` (see `InProcess.csproj`), not as
projects. The mock panel's rationales all start with `[MOCK]`: it scores by keyword overlap and is never an
AI evaluation. For the real panel, replace `new MockVotingClient()` with a `LegionVotingClient`
(`Gadfly.Socrates`) built from `GadflyConfiguration.Build(...)`; provider keys come from MindAttic.Vault.
