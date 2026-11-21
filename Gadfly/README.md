# Gadfly.Core

The subject-agnostic **Socratic engine** behind Gadfly. It plans each question from what the student
just said, judges answers against grounding text with citations, tracks claims and contradictions,
records honest aporia ("I don't know"), and produces a session report. The LLM work goes through a
multi-model panel (MindAttic.Legion) behind one small interface, `ISocraticVotingClient`, so you can
swap in the bundled **mock panel** (`MockVotingClient`) and run everything offline with no API keys
and no cost.

## What you use

| Type | Namespace | Role |
|---|---|---|
| `ISubjectMatter` | `Gadfly.Engine` | What a session examines: concepts, grounding per concept, a prompt set. Implement it for your own domain. |
| `SessionPolicy` | `Gadfly.Engine` | Which concepts, time budget, turn limit, difficulty, mode. |
| `SocraticSession`, `Turn`, `SessionReport` | `Gadfly.Engine` | The transcript and the report (`ToMarkdown()`, `ToJson()`). |
| `ISocraticVotingClient` | `Gadfly.Socrates` | The panel seam. `LegionVotingClient` is the live panel; `MockVotingClient` is the offline one. |

The source-material subject and the step-driven exam engine are in the companion package
**Gadfly.Sources**.

```csharp
ISocraticVotingClient panel = new Gadfly.Socrates.Mock.MockVotingClient();   // no keys, no spend
```

The mock panel is labelled: every rationale starts with `[MOCK]`. It is a deterministic stand-in for
development and CI, never an AI evaluation.

Runnable examples: <https://github.com/mindattic/Gadfly/tree/main/samples>.
