# Checkpoint (read this first after any interruption)

Owner: Claude. Assignment: Russell's 2026-11-10 brief on stories #25, #26, #27.
Last updated 2026-11-11.

## Where things are

- Repository `BigRedProf/stories`, branch `claude/library-lab-25-26-27`, based
  on main `1a0647f`. Draft PR: see the branch's pull request. Main is not merged.
- Everything is under `docs/library-lab/`. No file outside it was changed.
- Market Lab was already complete and pushed (`plai` PR #14 at `2b4d47c`); a
  resume note is on that PR. Nothing new was committed there.

## Status

| Item | Implemented | Tested | Independently reviewed | Accepted |
| --- | --- | --- | --- | --- |
| Requirements and alternatives note | yes | n/a | no | no |
| Atomic append designs A, B, C, D, S | yes | yes (18 tests) | no | no |
| Library-to-Library backup, verify, restore | yes | yes (14 tests) | no | no |
| Stone/sand, compaction, races, stale restore | yes | yes (14 tests) | no | no |
| Second application (scoreboard) | yes | yes (1 test) | no | no |
| Timings | yes | recorded once | no | no |
| Decision memo | draft | n/a | no | no |
| Anything in .NET | **no** | no | no | no |

## Commands

```sh
cd docs/library-lab
python3 -m unittest discover -s tests -t .
python3 demo.py
python3 bench.py
```

Expected: `Ran 47 tests ... OK`; `demo.py` output identical to
`results/demo.txt`.

## Results in one paragraph

A backup Library restored the Digihouse scenario bit for bit with the source
gone. Today's per-thing append tears batches and silently drops a damaged
acknowledged tail; a batch frame fixes the first and a recorded edition or
commit log detects the second. Multi-story commits were all-or-nothing across 27
crash cases. Sand removal and compaction left every stone fact, position and
edition identity unchanged. Restoring from a backup older than a deletion
honours the deletion when a newer retention ledger survives and says so plainly
when none does.

## Environment limits hit

- No .NET SDK in the session and package installs are disabled, so nothing was
  built or run against the real Stories code. `task verify` was not run; no
  .NET file changed, so it is unaffected.
- GitHub GraphQL is unavailable; the REST API was used.

## Remaining work, most useful first

1. **Team Grok's challenge** of the contracts and the failure handling. The
   memo lists five places I expect disagreement.
2. **Russell's decisions D1 to D8** in `DECISIONS.md`. D3 gates all .NET work.
3. If D3 is yes: port the story batch frame and header chain to a branch of
   `FileStoryLog` with golden bytes, as #24 did, and re-run the crash matrix
   there.
4. Commit-log checkpointing and a retry-id window.
5. A backup role flag that refuses application writes to a backup Library.
6. Per-edition pins instead of "any pin defers all reclamation".
7. Model directory fsync and non-atomic rename in the crash file system.
8. Exercise `StoryEnded` and catalog retirement.
9. #23's SQL-behind-Library and Library-behind-SQL cases, if a SQL house stays
   in scope.

## Next useful action

Ask Team Grok to attack `tests/test_retention.py::StaleBackup` and
`tests/test_atomic.py::LibraryCommit` first: those carry the two contracts most
likely to be wrong in a way that matters.
