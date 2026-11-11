# Library Lab

An executable investigation for stories #25, #26 and #27: can a Library back up
a Library, should appends be all-or-nothing, and how do stone and sand behave
across deletion, compaction, backup and restore.

**This is a prototype in Python. It is not the Stories implementation and
changes nothing in it.** It exists because the questions are about semantics,
and semantics can be settled with a small model before anything is frozen.

Start with [MEMO.md](MEMO.md). Decisions needed are in [DECISIONS.md](DECISIONS.md).
The state of the work is in [CHECKPOINT.md](CHECKPOINT.md).

## Run it

Needs Python 3.9 or later and nothing else. From this directory:

```sh
python3 -m unittest discover -s tests -t .   # 47 tests, under a second
python3 demo.py                              # the narrated Digihouse scenario
python3 bench.py                             # timings with real fsync (a few seconds)
```

Recorded outputs are in `results/`. `demo.py` and the tests are deterministic:
no clock, no randomness, fixed ids. `bench.py` depends on your disk.

## What is where

| File | Contents |
| --- | --- |
| `lab/core.py` | `Code`, the crash-injecting file system, `Story`, `Library` |
| `lab/preserve.py` | Backup, incremental backup, restore, ledger escrow |
| `lab/alt.py` | The alternatives: today's per-thing append, a journal, SQLite, write-once objects |
| `lab/apps.py` | Synthetic Digihouse, and a scoreboard as a second application |
| `lab/harness.py` | Crashes an operation before each durable step, under three crash modes |
| `tests/test_atomic.py` | #26: all-or-nothing appends |
| `tests/test_preserve.py` | #25: backup, verification, restore |
| `tests/test_retention.py` | #27: stone and sand |
| `tests/test_second_app.py` | The contracts on a non-Digihouse application |

## Reading a test as evidence

Each test name states the guarantee it proves, for example
`test_multi_story_commit_is_all_or_nothing_across_every_crash_point` or
`test_without_newer_evidence_the_restore_reports_resurrection_and_refuses_writes`.
Three tests document defects of the current design on purpose; they are in the
`LegacyBaseline` class and model `FileStoryLog` as read at `1a0647f`.
