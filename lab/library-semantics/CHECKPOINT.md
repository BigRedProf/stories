# Checkpoint

Status of the library-semantics investigation. Nothing in this file is accepted. Accepted means Russell said so.

—CJ

## Branch

- Branch: `cursor/library-semantics-lab-e4a6` (the suggested name `cj/library-semantics-lab` was not used; this agent's branch rule requires the `cursor/` prefix and the `-e4a6` suffix).
- Base: `origin/main` at `1a0647f7c648d3e08f5a4afca041c49b68752a0e`.
- Head: see `git rev-parse HEAD` after the latest push. The draft PR is the durable link.

## Status words

| Word | Meaning here |
| --- | --- |
| Implemented | On this branch and compiled. |
| Tested | A named test passed in this workspace. Output is under `evidence/` once captured. |
| Independently reviewed | Team Grok has written a disagreement or a confirmation. Not happened. |
| Accepted | Russell adopted it. Not happened. |

## This checkpoint

| Item | State |
| --- | --- |
| Inventory of Bit, Code, Story, tape, Library, Edition | Written in `REQUIREMENTS.md`. |
| Requirements and the short alternative list | Written in `REQUIREMENTS.md`. |
| Executable lab | Not started at the first commit. |
| Failure-test evidence | Not captured. |
| Decision memo | Not written. `DECISION.md` will be the memo. |
| Decisions for Russell | Not written. `DECISIONS_NEEDED.md` will hold them. |

## Commands

From `lab/library-semantics`, once the projects exist:

```bash
dotnet test src/SemanticsLab.Tests/BigRedProf.Stories.LibrarySemanticsLab.Tests.csproj --nologo
```

That command is the whole lab. It does not run `task verify`, and it does not change shipped formats.

## Next useful action

Implement the lab formats (per-frame, commit-marker, length-prefixed), the edition pointer, preservation, and the stone/sand lifecycle, then run the tests before writing the recommendation.
