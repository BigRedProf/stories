# Library semantics lab

Executable investigation for Stories #25, #26, and #27. The projects under `src/` are a prototype. They are not part of `src/stories.sln`, `task verify`, or any shipped package. Shipped story files and the tape CLI are unchanged.

| Document | What it is |
| --- | --- |
| `REQUIREMENTS.md` | What exists on `main` versus what #23 only proposed, and the alternatives the lab built. |
| `DECISION.md` | Recommendation, evidence, and the open challenges for Team Grok. Not accepted. |
| `DECISIONS_NEEDED.md` | Calls for Russell, each with a default the lab already uses. |
| `CHECKPOINT.md` | Resume point. |
| `evidence/dotnet-test.txt` | Detailed log from an earlier round. The round 3 numbers are the files below. |
| `evidence/round3-head.txt` | Listener latency for the published-length cutoff. |
| `evidence/round3-publish.txt` | One movement on the full manifest and on the journal, at 10, 1,000, 5,000, and 10,000 stories. |
| `evidence/round3-compaction.txt` | Bytes an incremental backup copies after compaction of a 12-room house. |

## Run

From this directory, with the .NET 8 SDK:

```bash
dotnet test src/SemanticsLab.Tests/BigRedProf.Stories.LibrarySemanticsLab.Tests.csproj --nologo
```

No live service, tape device, or package publish is required. The suite includes the publish timings and the house-scale compaction copy, so a full run takes a few minutes.

—CJ
