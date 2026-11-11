# Requirements and alternatives

Investigation for Stories issues #25, #26, and #27. This note records what the code does today, what #23 only proposed, and the alternatives the lab has to separate. Nothing here is an accepted product decision.

—CJ

## What actually exists

Checked against `stories` `main` at `1a0647f7c648d3e08f5a4afca041c49b68752a0e`, plus the GitHub text of Data PR #91 (merged), Content PR #10 (open), and Stories PR #24 (open). Sibling repositories were read through the GitHub API. They were not cloned into this workspace.

| Layer | In code today | Only proposed |
| --- | --- | --- |
| Bit / Code | `BigRedProf.Data.Core.Code` on Data 0.10.1 in this repo. Data `main` has replaced `CodeReader` / `CodeWriter` with `BitStream` (PR #91, 0.11.0-rc.1). A code has a bit length; it is not a byte string. | Nothing further required for this investigation. |
| Content | Not referenced by Stories. Content PR #10 stores content-addressed codes (blob, multihash, catalog event) and is moving onto 0.11.0-rc.1. | A library content store does not exist here. |
| Story | Append-only `StoryThing` (`Offset` + `Code`). `FileStoryLog` writes one CRC frame per record, flushes that frame, and drops a torn tail on reopen. One writer, via an exclusive lock file. Offsets are dense and assigned by the writer. | Expected-offset append (#22) is not implemented. A multi-record `RecordSomething` is not all-or-nothing: a crash keeps the frames that already flushed. |
| Tape | CLI `backup` / `restore` copy one story's codes through Data's `TapeLibrary`, `BackupWizard`, and `RestorationWizard`. Incremental resume is a checkpoint offset. `verify` throws `NotImplementedException`. The round-trip test is skipped. | #21's full contract (gaps, occupied targets, series identity) is not finished. Data #87's S3 tape provider is a separate package, not in this repo. |
| Library / Edition | No types, no catalog story, no manifest, no tests. | #23 describes a catalog, an edition as a hashed set of story prefixes, and a separate "preservation completed" fact. |
| Publishing House / SQL | `SyncLogsToSql` copies log entries into SQL. It is not a command-side outbox or a source of story order. | #23's SQL publishing house, distributor, and mismatch recovery. #25 says not to require SQL just because #23 mentioned it. |
| Stone / sand | No retention field, no compaction, no deletion certificate. | #27. |

#23's leanings (transactions stay outside the library, purchases become single-story facts, SQL in the publishing house, editions identify prefixes but are not snapshots, tapes remain the backup medium to reconcile) are evidence. Issues #25–#27 reopen them. This lab does not treat them as constraints.

## Requirements the lab has to meet

1. A second library must be able to preserve a named edition and restore it into an empty library after the source directory is gone. Exact code bits, bit lengths, offsets, catalog membership, and content bytes that the edition still requires must match. Application state folded from those records must match.
2. An incomplete, torn, or hash-mismatched copy must not become the destination's current edition.
3. Full and incremental preservation, a lost preservation ack, an occupied destination, and an empty story must have explicit results.
4. All-or-nothing has to be split into atomic visibility, crash durability, concurrency control, and consistent reads. A passing test for one of those is not a passing test for the others.
5. Two writers that both observed the same expected edition must not both commit different batches.
6. A client that retries a request whose commit landed but whose acknowledgement was lost must not append a second copy. A timeout after commit is not a rollback.
7. Stone ownership history must survive sand-blob deletion and movement compaction. A gap or a missing blob without an authorized certificate must fail verification. The same shape with a certificate must pass and say why bytes are absent.
8. A backup that races compaction must either keep the pinned bytes or fail. It must not publish a success whose hashes do not match the edition it named.
9. Restoring a backup older than a deletion must say whether deleted sand comes back, and must refuse a "do not resurrect" request when newer removal evidence was not supplied.
10. The same library operations must run a second, non-Digihouse application without Digihouse types inside the library.

## Alternatives worth building, and no others

**Backup.** (A) Library edition copied into an independent library. (B) A tape series of code bits, which is what the CLI does now. The lab implements both at the smallest size that can show a tear across stories and a missing blob. A tape *provider* under a library is not a third design and is not built.

**Single-story batch durability.** (A) Per-frame flush, shipped behavior. (B) Tentative frames plus a commit marker. (C) One length-prefixed frame for the whole batch. All three are implemented behind one create-time format so the crash points can be compared. No write-ahead log beyond the marker, and no SQL.

**Multi-story visibility.** One edition-pointer rename after the story frames are durable. Readers of that edition see every story prefix in it or the previous edition, not a mix. Raw story heads are allowed to sit ahead of the published pointer; the lab treats that as a different reader. No two-phase commit and no cross-library transaction.

**Lost acknowledgements.** The request carries an idempotency key and a body hash. A retry of the same key and hash returns the original edition. A retry of the same key and a different hash is a conflict. Expected edition is checked only for a key the library has not already committed, because a lost ack retries with the pre-commit edition.

**Stone / sand.** Retention is a field beside the opaque code, not a bit inside it. Compaction writes a new story generation: kept records keep their offsets, dropped sand offsets become gaps, and a stone certificate covers every gap. Content blobs are separate files. A sand record claims a blob; releasing one claim does not delete bytes another claim or a stone promise still needs. Stone records and certificates are not removed by compaction. Exact restore of an old edition copies that edition's bytes, so sand that existed then is present again. A second, explicit step can apply later certificates. That step is not a roll-forward of later business events.

**Out of this lab.** Publishing-house SQL, distributor replay, public API changes, shipped file-format changes, tape deletion, and package publishing. Proposed bytes stay under `lab/library-semantics`.
