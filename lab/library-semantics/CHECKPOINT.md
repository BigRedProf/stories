# Checkpoint

Resume point for the library-semantics investigation. Nothing here is accepted. Accepted means Russell said so.

—CJ

## Status words

| Word | Meaning here |
| --- | --- |
| Implemented | On this branch and compiled. |
| Tested | A named test passed in this workspace. 46 passed, 0 failed. One movement took 4 ms with 10 stories, 64 ms with 1,000, and 463 ms with 5,000, including fsync. The 5,000 figure is from a one-off run; the checked-in test keeps 10 and 1,000. |
| Independently reviewed | Codex reviewed `c97df01`, `34a8f6d`, and `37c659e`. The findings from those commits were checked and fixed. Team Grok has not reviewed it. |
| Accepted | Russell adopted it. Not happened. |

## Where things stand

| Item | State |
| --- | --- |
| Inventory and requirements | Implemented. `REQUIREMENTS.md`. |
| Lab, including process-kill, cross-process writers, shipped-file adoption, and the 0.11 bit comparison | Implemented and tested. |
| Decision memo | Round 2 recommendation. Codex's findings are recorded in it. Not accepted. |
| Russell's open calls | `DECISIONS_NEEDED.md`. The frame recommendation and the resurrection default changed in round 2. |
| Shipped `FileStoryLog`, tape CLI, package versions | Unchanged. |

## Git

- Branch: `cursor/library-semantics-lab-e4a6`.
- Base: `origin/main` at `1a0647f7c648d3e08f5a4afca041c49b68752a0e`.
- Round 1 lab commit: `c2e6d465f709b645e74036dba5d47e3188114f92`.
- Round 1 head Codex reviewed: `c97df0143a6d7587de36b4b7ec62086cfa290a67`.
- Round 2 head: the commit that contains this paragraph. After pull, `git rev-parse HEAD`.

## Commands

From `lab/library-semantics`, with the .NET 8 SDK on `PATH`:

```bash
dotnet test src/SemanticsLab.Tests/BigRedProf.Stories.LibrarySemanticsLab.Tests.csproj --nologo
```

The suite builds `CrashHost` and, inside one test, Data 0.11.0-rc.1's bit-layout process. It does not run `task verify`. The filesystem used for the process-kill tests was ext4 on `/dev/vdc`. Those tests are process crashes. They are not power-loss tests.

## Recommendation after round 2

Do not add a length-prefixed story frame for v1. Edition readers plus the current per-frame file already hide a partial batch. Require `AcceptResurrection` before an exact restore materializes sand. Keep the writer lock. It is what makes two processes safe. Expected offset decides the race only after a process holds that lock.

## Next useful action

Russell decides the two changed defaults in `DECISIONS_NEEDED.md`: no new story frame, and an explicit accept before sand comes back. Team Grok can still argue that raw-file atomicity is a v1 requirement. If that argument wins, the length-prefixed frame is already implemented and tested in the lab.
