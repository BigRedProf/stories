# Checkpoint

Resume point for the library-semantics investigation. Nothing here is accepted. Accepted means Russell said so. Team Grok has not reviewed it.

—CJ

## Status words

| Word | Meaning here |
| --- | --- |
| Implemented | On this branch and compiled. |
| Tested | A named test passed in this workspace. The log is `evidence/dotnet-test.txt`. |
| Independently reviewed | Team Grok has written a disagreement or a confirmation. Not happened. |
| Accepted | Russell adopted it. Not happened. |

## Where things stand

| Item | State |
| --- | --- |
| Inventory, requirements, alternatives | Implemented. `REQUIREMENTS.md`. |
| Lab: three batch framings, edition pointer, preserve/restore, stone/sand, Digihouse, field notes, tape comparison | Implemented and tested. 29 tests, 0 failures. |
| Failure injection: torn frame, crash before marker, crash before pointer, crash after publish, lost preserve ack, compaction during an unpinned copy | Tested. See the test names in `DECISION.md` and the seed log in the evidence file. |
| Decision memo | Implemented as a recommendation. Not reviewed, not accepted. `DECISION.md`. |
| Russell's open calls | `DECISIONS_NEEDED.md`. Defaults are in the lab so work did not stop. |
| Shipped `FileStoryLog`, tape CLI, package versions | Unchanged. |

## Git

- Branch: `cursor/library-semantics-lab-e4a6`. The suggested name `cj/library-semantics-lab` was not used; this agent has to use the `cursor/` prefix and the `-e4a6` suffix.
- Base: `origin/main` at `1a0647f7c648d3e08f5a4afca041c49b68752a0e`.
- Requirements commit: `ac053029504cd7a0c494f60d7c55cc482191f9db`.
- Lab, tests, evidence, and this memo: the commit that adds `lab/library-semantics/src`. Run `git log -1 --oneline` on this branch after pulling. The follow-up line below is filled once that commit exists.

Lab commit: `PENDING`.

## Commands

From `lab/library-semantics`:

```bash
dotnet test src/SemanticsLab.Tests/BigRedProf.Stories.LibrarySemanticsLab.Tests.csproj --nologo
```

That is the whole lab. It does not run `task verify` and it does not change shipped formats. The .NET 8 SDK is required. This workspace did not have it installed; `dotnet-install.sh` channel 8.0 provided SDK 8.0.428, which satisfies `global.json` (`8.0.403` with `latestFeature` roll-forward).

Randomized seed, printed by the test: `20261110`.

## What the tests are allowed to say

They show the lab's behavior. They do not show that Digihouse, Content's multihash, or the shipped story file implement any of it.

## Next useful action

Russell, or Team Grok if they pick up the challenges in `DECISION.md`: read the six defaults in `DECISIONS_NEEDED.md`, especially exact-restore resurrection and whether the length-prefixed frame is worth a new story file. Do not merge this branch and do not start a SQL publishing house from it. The following implementation step, after a decision, would be expected-offset on the shipped scribe (#22), still without changing the story frame.
