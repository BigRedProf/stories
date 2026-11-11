# Checkpoint

Resume point for the library-semantics investigation. Nothing here is accepted. Accepted means Russell said so.

—CJ

## Status words

| Word | Meaning here |
| --- | --- |
| Implemented | On this branch and compiled. |
| Tested | A named test passed in this workspace. 55 passed, 0 failed, at `9a1ea6de732bb9d065684d9c488a91adac38a9ca`. The log is `evidence/dotnet-test.txt`. |
| Independently reviewed | Codex reviewed `c97df01`, `34a8f6d`, and `37c659e`. The findings from those commits were checked and fixed. Round 3 has not been reviewed. Team Grok has not reviewed it. |
| Accepted | Russell adopted it. Not happened. |

## Where things stand

| Item | State |
| --- | --- |
| Inventory and requirements | Implemented. `REQUIREMENTS.md`. |
| Lab, including process-kill, cross-process writers, shipped-file adoption, and the 0.11 bit comparison | Implemented and tested. |
| Published-length head reader, journal publish, and the house compaction copy | Implemented and tested in round 3. Not independently reviewed. Not accepted. |
| Decision memo | Round 3 recommendation. Codex's findings through round 2 are recorded in it. Not accepted. |
| Russell's open calls | `DECISIONS_NEEDED.md`. D8's default is a published-length head reader. The publish default is a journal of changed stories. |
| Shipped `FileStoryLog`, tape CLI, package versions | Unchanged. |

## Git

- Branch: `cursor/library-semantics-lab-e4a6`.
- Base: `origin/main` at `1a0647f7c648d3e08f5a4afca041c49b68752a0e`.
- Round 1 lab commit: `c2e6d465f709b645e74036dba5d47e3188114f92`.
- Round 1 head Codex reviewed: `c97df0143a6d7587de36b4b7ec62086cfa290a67`.
- Round 2 head: `e6737b9f3dc132234731565e653fa42a61768609`, the commit the #29 cross-review read.
- Round 3 head: `9a1ea6de732bb9d065684d9c488a91adac38a9ca`. 55 passed, 0 failed. The log is `evidence/dotnet-test.txt`.

## Commands

From `lab/library-semantics`, with the .NET 8 SDK on `PATH`:

```bash
dotnet test src/SemanticsLab.Tests/BigRedProf.Stories.LibrarySemanticsLab.Tests.csproj --nologo
```

The suite builds `CrashHost` and, inside one test, Data 0.11.0-rc.1's bit-layout process. It does not run `task verify`. The filesystem used for the process-kill tests was ext4 on `/dev/vdc`. Those tests are process crashes. They are not power-loss tests.

## Recommendation after round 3

Head readers stay, and they stop at the published length. Do not add a length-prefixed story frame for that. Publish a journal frame for the stories that changed, and checkpoint the full map periodically, so one movement does not rewrite the library. Keep edition identity tied to the bytes: after compaction an incremental copies the new generation, and the old generation stays the meaning of the old edition. Require `AcceptResurrection` before an exact restore materializes sand. Keep the writer lock.

## Next useful action

Russell accepts or reverses the defaults in `DECISIONS_NEEDED.md`, in particular D8 and the journal publish. Team Grok can still argue for a raw head. If that argument wins, the cutoff is the wrong default and the length-prefixed frame is already implemented and tested in the lab. It still does not hide a flushed batch from that raw head.
