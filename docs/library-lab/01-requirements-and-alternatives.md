# Requirements and alternatives (stories #25, #26, #27)

Written 2026-11-10 after inspecting the code, before the experiments. The
conclusions are in [MEMO.md](MEMO.md); this note records the starting point.

## What actually exists

Checked against `stories` main at `1a0647f`, `data` at `b4205c1`, `digihouse` at
`61e632b1`, and open PRs #19 and #24.

| Concept | State today |
| --- | --- |
| Bit / Code | Real, in `BigRedProf.Data.Core`. Codes are bit-exact and need not end on a byte. |
| Story | Real. `IScribe.RecordSomething(params Code[])`, `IStoryteller`, with memory, file and API implementations. |
| Library, catalog, Edition | **Do not exist.** #23 is a design note. `FileStoryManager` is a directory of story files with no listing. |
| Content store | Does not exist in Stories. |
| Stone / sand | Does not exist anywhere. |
| Tapes | `BigRedProf.Data.Tape`: series of bounded tapes (1 billion bits each) with labels, a series digest chain and a client checkpoint. |

Findings that shaped the experiments:

1. **A multi-thing append is not atomic.** `FileStoryLog.Append(Code[])` writes and
   fsyncs one frame per thing. A crash leaves any prefix of the batch.
   Digihouse's `GatehouseService` forwards client batches this way today.
2. **A damaged final frame is indistinguishable from a torn tail.** A checksum
   failure in the last frame is silently truncated on the next open, even if that
   frame was acknowledged long ago. Nothing outside the file records how long the
   story should be.
3. **There is no expected-position append and no retry identity.** #22 was closed
   as not planned. A retry after a lost acknowledgment appends twice unless the
   application deduplicates by its own event ids (Digihouse does, per aggregate).
4. **The API service keeps stories in memory.** `StartUpHelper` wires
   `MemoryStoryManager` only; the file-backed story from #18 is not wired in.
   So for the deployed service, tapes are currently the only durability.
5. **The tape verbs on main are incomplete.** `restore` and `verify` throw
   `NotImplementedException`; restore-from-series detects the end of the tape by
   catching an exception, so truncation and a clean end look the same at that
   layer. PR #19 (open) fixes this with `backup-all` / `restore-all` /
   `verify-all`, immutable segments and a weekly manifest, and was proven end to
   end against live services.
6. **Digihouse already copies stories with a prefix check.** `StoryCopier` is
   library-to-library preservation in miniature: resume from a prefix, refuse a
   target that is not a prefix.
7. **Real Digihouse events match the brief.** `GoodMinted`, `GoodTransferred`,
   `OrderPlaced`, `PaymentSucceeded`, `OrderFulfilled`, `GoodEnteredRoom`,
   `ObjectMoved`, `ManyObjectsMoved`, `AvatarMoved`.

## Requirements

**R1 Exactness.** A preserved or restored story has the same codes, to the bit,
in the same positions, with the same identities.

**R2 Honest completion.** Nothing reports "complete" unless every item in the
declared scope is present and verified, or absent with recorded authorization.
An interrupted attempt is visibly incomplete. Truncation is never a clean end.

**R3 Four separate guarantees for writes.** Atomic visibility (readers see all or
none), crash durability (acknowledged means it survives), concurrency control
(two writers with the same expectation cannot both win), consistent reads (a
snapshot across stories). Each is stated and tested on its own.

**R4 Unknown outcomes reconcile.** A timeout is not a rollback. The same request
sent again must be safe.

**R5 Stone outlives sand.** Removing sand never changes a stone fact, a position
or a history identity. Removing stone is refused.

**R6 Removal is an authorized act, not an absence.** A missing payload with no
evidence is damage. Evidence must exist durably before any byte is reclaimed.

**R7 Recovery respects removals when it can and says so when it cannot.**

**R8 Independent stories stay simple.** A story that needs none of this pays
nothing for it.

## Alternatives prototyped

Atomic appends:

| | Design | Scope |
| --- | --- | --- |
| A | Today: one frame and one fsync per thing | baseline |
| B | One frame per append, with expected position and retry id | one story |
| C | Marked frames in each story, published by one record in a library commit log | many stories |
| D | Write-ahead journal holding the whole commit, applied to stories afterwards | many stories |
| S | A provider with real transactions (SQLite), as #23 proposed | many stories |

Backup: tape series per story (today, PR #19) against Library-to-Library copy
through the #23 accept rule, with editions as the unit of completion.

Retention: class per entry, per story, per content claim; payload removal that
keeps the header ("tombstone") against dropping a prefix behind an anchor;
pinning an edition during backup against retrying; restore with and without
newer removal evidence.

## Order of work

Largest uncertainty first: (1) can one storage shape give atomic commits *and*
let sand disappear without changing history identity; (2) does the accept rule
plus editions make a Library a sound backup, including interruption and damage;
(3) the retention races. Timings and the second application came last.
