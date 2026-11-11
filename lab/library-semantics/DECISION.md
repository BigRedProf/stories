# Decision memo

Lab result for Stories #25, #26, and #27. This is a recommendation with executable evidence. It is not accepted. Accepted means Russell said so. Team Grok has not reviewed it.

—CJ

## Answers

**Libraries can be the backup.** A second library, given one published edition, restored the same code bits, bit lengths, offsets, catalog membership, content bytes, and Digihouse fold after the source directory was deleted (`Lifecycle_SurvivesBackupCompactionAndSourceLoss`). A tape series of code bits did not. `TapeCopy_CanTearAcrossStoriesAndOmitsContent` copied story heads one at a time, appended to two stories in the gap, and produced counts that match no edition. The tape file also has no content bytes. Tapes stay useful as a carrier for one story prefix. They do not earn a required layer in front of the library.

**Basic all-or-nothing appends belong at the edition pointer for visibility, and in the existing per-frame story file for storage.** Round 1 recommended a new length-prefixed frame. Round 2's comparison says that frame is not required for edition readers. A per-frame crash after the first flushed record leaves that record on the story head and leaves the published edition unchanged (`PerFrame_EditionHidesThePrefix_AndTheNextWriterUsesThePublishedOffset`, and the same shape under SIGKILL in `ProcessKill_AfterFirstFlushedRecord_EditionStaysPut`). The next writer, using the published offset, replaces the unpublished prefix. A length-prefixed frame still does something the pointer does not: a torn multi-record batch drops both new records and keeps the earlier committed record (`LengthPrefixed_TornWrite_DropsTheWholeBatch`). That is raw-file atomicity. It costs a new story frame. I would not pay that for v1. Ship expected offset (#22), an edition pointer, and the current per-frame file. Keep the length-prefixed frame in the lab until a reader of the raw file is promised an atomic batch.

**Stone and sand are different lifetimes, stored beside the code, not inside it.** Purchases and transfers stayed byte-for-byte after the JPEG was released and the moves were compacted (`StoneHistory_SurvivesSandDeletionAndCompaction`). A gap without a stone certificate fails retention even when the file hash matches the manifest. A flipped byte fails integrity and is not reported as a gap. Exact restore of an edition that contains sand is refused unless the caller passes `AcceptResurrection`. With that flag, the deleted sand comes back. Stopping that without resurrecting requires a newer edition's certificates. A missing file is not treated as a deletion.

## What each layer is for

| Layer | Responsibility in this recommendation | Not its job |
| --- | --- | --- |
| Bit | Data's bit. Unchanged. | Records, retention, backup. |
| Code | An opaque bit string with a length. The lab stores `Code.ToByteArray()` plus `Code.Length` from Data 0.10.1, the version this repo references. `CodeBytes_MatchData_0_11_0_rc_1` runs Data 0.11.0-rc.1 in a separate process and matches those bytes for an aligned code, a 5-bit code, an empty code, and a one-bit code. Packing is LSB-first in the first byte. | BitStream's stream wrapper. This match is four codes, not Data's full golden file. |
| Story | Ordered records. The code is opaque. Beside it: retention, an optional content claim, an optional effect, and the idempotency key. Offsets are not reused. One writer, by an exclusive lock file, same shape as today's `FileStoryLog`. | Cross-story atomicity. Deciding that a purchase is stone. |
| Edition | The hash of a canonical manifest. Publication is `Flush(true)` of the new pointer, `File.Move` over `edition-current.txt`, then `fsync` of that directory. `Publish_FsyncsTheDirectoryAfterThePointerRename` counts the directory sync. | A promise that every cross-story read is a snapshot. Power-loss durability, which this VM's process kills cannot show. |
| Library | The directory of stories, content files, edition manifests, and the current pointer. An independent library is the backup. | Scheduling, legal policy, Digihouse rules, SQL. |

Content bytes live next to the story, content-addressed, because a story record that names a JPEG is not the JPEG. The lab's envelope is sha256 of a small header plus the code bytes. That is not Content's multihash format. Do not ship it as one.

## Tapes

| Question | Library edition | Tape series, as the CLI does it |
| --- | --- | --- |
| One story's code bits | Copied and hashed | Copied, in order, with a checkpoint count |
| Several stories at one cut | The edition names every prefix | Heads are copied one story at a time and can tear |
| Content blobs | Listed and copied, or marked erased | Absent |
| Stone vs sand | Certificates travel with the edition | No retention field |
| Incomplete copy | Pointer does not move | A partial series is not a distinct "not published" edition |
| Incremental | Byte suffix when the generation is unchanged; full file when compaction starts a new generation | Checkpoint offset |

#21's single-story tape copy can stay as a carrier. It is not the application backup, and this memo does not close it. Data #87, an S3 tape provider, is a place a library's bytes might live later. It is not a second backup design. The existing `verify` command still throws, and the tape round-trip test is still skipped. Nothing here deletes that code.

The evidence against this lean: for one story and no blobs, the tape copy is smaller and already sketched. The extra cost of a library shows up only when the backup has to name a set of prefixes, carry blobs, and tell a deletion from a hole. That is the Digihouse backup. A directory copy without an edition pointer would have the same tear the tape had.

## Transactions, in four rows

| Guarantee | Recommendation | What the tests actually did |
| --- | --- | --- |
| Atomic visibility of one story batch for edition readers | Keep today's per-frame file. Do not publish the edition until every record in the batch is flushed. Edition readers then see none or all. | `PerFrame_EditionHidesThePrefix_AndTheNextWriterUsesThePublishedOffset`. SIGKILL after the first flushed record: `ProcessKill_AfterFirstFlushedRecord_EditionStaysPut`. |
| Raw-file atomicity of one multi-record batch | A length-prefixed frame, only if a raw-file reader must not observe a prefix. Not recommended for v1. | `LengthPrefixed_TornWrite_DropsTheWholeBatch` tears a two-record batch after a committed record. The committed record remains. Neither new record does. The earlier wording that this was the room-init catalog frame was wrong; that tear happened on the one-record catalog story. Codex caught it. |
| Commit marker | Not recommended. The bytes of an unmarked batch are on disk. | `CommitMarker_CrashBeforeMarker_HidesBytesANaiveReaderWouldKeep`. |
| Per-frame story head | A head reader sees flushed records the edition does not. That is the cost of keeping the current frame. | `PerFrame_CrashAfterFirstRecord_IsVisibleOnTheStoryHeadOnly`. |
| Multi-story visibility | Rename the edition pointer after every story frame is durable. | Crash after the frames and before the pointer: story heads had the records, the current edition did not, retry published them and flushed 0 new frames (`CommitMarker_CrashAfterFramesBeforePublish_RetriesWithoutDuplicating`). |
| Concurrency | One writer process, via an exclusive lock file the kernel releases when the process dies. Expected edition and expected offset decide which request wins once a writer is inside. | Threads sharing one instance: `TwoWriters_SameExpectedEdition_OnlyOneCommits`. Two processes: the second `Open` throws "already has a writer" and does not queue. After the winner exits, the loser's retry with the stale edition is `PositionConflict` (`TwoProcesses_SameExpectedEdition_OneCommits`). Killing the holder lets the next open succeed. A pid file would still be there (`KilledHolder_ReleasesTheFileLock_APidFileWouldNot`). Without the library lock, the two processes do not get `PositionConflict`; both returned `Committed`, or one hit a sharing violation on the story file (`TwoProcesses_WithoutTheLock_DoNotGetSingleWinner`). |
| Lost acknowledgement | The key and body hash ride in the record. A retry returns the edition that committed that key, including when a later commit has moved the pointer. | `LostAck_AfterAnotherCommit_ReturnsTheOriginalEdition`. The earlier test only checked status and record count, with no intervening commit. Codex was right that it did not prove this row. |
| Consistent read | An edition id is the snapshot. The story head may be ahead of the pointer. A backup copies a captured edition. | `PinnedBackup_KeepsBytesThatCompactionRemoves` takes a pin, then compacts the source during the copy, and the backup still verifies as the pinned edition. `UnpinnedBackup_FailsWhenCompactionWinsTheRace` compacts after the first story file has been copied and does not publish; the previous backup still verifies. |

A purchase is one stone record. A transfer is one record. Room initialization is several records and a catalog line. Edition publication hides that set until the pointer moves. The room story's own head can still show a prefix if the records were flushed one frame at a time. That is acceptable for v1 if callers read editions. It is not acceptable if callers read the story head and need the seed and the ready record to appear together. That second requirement is the only reason to add a length-prefixed frame, and I am not recommending it yet.

SQL did not appear in any passing test. #23's publishing house can still own command-side transactions later. It is not part of this backup or this append contract. The mismatched SQL/library recovery in #23 stays open and is not answered here.

The evidence against a library idempotency key: the key could live inside the code, and expected offset (#22) already forces the loser to re-read. That is enough if every client re-reads on an unknown outcome. It is not enough for a blind retry, which is what a lost ack looks like. The lab key is a small field, not a transaction manager. I would still ship #22's expected offset on the real scribe even if the batch frame waits.

Limits I would not cross in v1: batches that do not fit one frame, more than one writer process, cross-library atomicity, and any read that is not either "this edition" or "this story's committed head."

## Stone and sand

Retention sits beside the code. The application declares it. The library does not infer it from the bits.

| Case | Rule the lab enforces |
| --- | --- |
| Purchase, transfer, room init, catalog | Stone. Compaction that covers a stone offset is rejected. |
| JPEG / model claim | Sand record plus a content id. The blob is a separate file. |
| Movement | Sand records. Compaction is an explicit range, not "keep the last position" as a hidden policy. The summary text is the application's. |
| Shared blob | Releasing one sand claim leaves the bytes while another claim, or a promotion, still needs them. |
| Promotion | A stone certificate. Later release of that claim is rejected. |
| Compaction | New story generation. Kept records keep their offsets. Dropped offsets are a gap. A stone certificate covers the gap and lists the idempotency keys it supersedes, so a retried move does not append again. |
| Old edition bytes | They are not in the new generation. They survive in a pin or a backup that copied them first. |
| Verification | Separate results: integrity (hashes and checksums), completeness (required blobs and story files present), retention (every gap has a certificate, erased blobs are absent, compacted ranges are empty), application fold (caller-supplied). |
| Backup vs compaction | A pin copied while the source compacts still verifies as the pinned edition. An unpinned copy that loses files after the copy has started publishes nothing, and the previous edition on the destination still verifies. | `PinnedBackup_KeepsBytesThatCompactionRemoves`, `UnpinnedBackup_FailsWhenCompactionWinsTheRace`. |
| Restore older than a deletion | If the edition contains sand, exact restore is refused until the caller sets `AcceptResurrection`. With the flag, sand in that edition is present again. `PreventResurrection` with no newer edition is refused and writes nothing. With the newer edition, certificates are applied and the later lamp purchase is not copied. | `ExactRestoreOfSand_RequiresAnExplicitAccept`, `RestoreOlderThanDeletion_BringsSandBack_UnlessCertificatesAreSupplied`. |
| Interrupted incremental preserve | Blobs for the edition that is still current are not deleted before the new pointer is published. | `InterruptedIncrementalPreserve_KeepsThePreviousBlobs`. |

The evidence against "deletion sticks": an exact restore of an older backup resurrects the JPEG and the moves. That is what the bytes in that edition are. Promising that every offline copy forgets a blob would be false. The lab refuses to guess a deletion from absence, which is the other failure (a truncated backup silently deleting history).

Compaction also gives up a simple story: the live file no longer matches older edition hashes. Verification of an old edition against the live generation fails, correctly, unless you still have the old generation or a backup. "Append-only" describes a generation, not the library's whole life.

## Compatibility

Nothing in `src/` changed. `FileStoryLog` is still one CRC frame per record. The tape CLI is untouched. `ShippedStoryFile_IsAdoptedWithoutRewritingBytes` writes two codes with `FileScribe`, copies those exact bytes into a lab edition marked `shipped-v1`, preserves them to a second library, and reads them back with `FileStoryteller`. A migration that only needs backup and verify can keep the shipped frame. A migration that wants lab retention, claims, and compaction has to write new records beside that file. The lab does not transcode `BRPSTORY` into `BRPLABLP`.

The lab process still references Data 0.10.1, because that is what `FileScribe` is built against. The 0.11.0-rc.1 comparison is a separate process so the two assemblies are not loaded together. Stories PR #24 can move the shipped packages onto 0.11.0-rc.1 without this lab changing the story frame.

## What I would not do next

Do not merge this lab into the shipped packages. Do not delete the tape commands. Do not start the SQL publishing house to make backups work. Do not treat the lab content id as Content's id.

## Where the evidence cuts against us

- Round 1 wanted a new length-prefixed frame. Edition-pinned readers plus the existing per-frame flush already give all-or-nothing visibility. The new frame's remaining job is raw-file readers. That is a smaller reason than round 1 claimed, and it is not enough to change the shipped frame.
- The first lost-ack test did not prove that a retry returns the original edition. An intervening commit used to hand back the later edition id. That is fixed and tested. The memo was ahead of the test.
- The first torn-write test tore the one-record catalog frame, then described that as the two-record room batch. The two-record case is a separate test now.
- The first compaction race fired before any byte was copied. It now fires after the first story file is copied. The pin test compacts during the copy, not before `Preserve` is called.
- In-process `throw` runs `Dispose`, which can flush a buffer a killed process would lose. `ProcessKill_BeforeFlush_DropsTheUnflushedFrame` is SIGKILL with no flush. `ProcessKill_AfterDirectoryFsync_KeepsTheEdition` is SIGKILL after the directory fsync. Both are process crashes on ext4 (`/dev/vdc`). Neither is a power cut. After `write` has entered the kernel, process death keeps the page cache. Directory fsync and file `Flush(true)` are still the right power-loss sequence. This VM cannot show that they differ under SIGKILL.
- Without the writer lock, expected offset does not decide the race. The second process gets a sharing violation, or both return `Committed`. The lock is the cross-process control. Expected offset is the control among threads that already hold the library.
- A fixed-seed random walk did not guarantee that each fault landed on an operation that could observe it. It is gone. `ExplicitFaultTests` arms a named fault on a named operation and asserts the throw.

## Codex review

Reviewed at `c97df0143a6d7587de36b4b7ec62086cfa290a67`, before this round. I checked each finding against the code. All five were real.

| Finding | What was wrong | What changed |
| --- | --- | --- |
| Defer backup blob deletion until the pointer moves | An incremental preserve deleted the destination's photo before publication. A crash left the old edition missing a blob it still required. | Erased blobs are reclaimed only after the pointer moves. `InterruptedIncrementalPreserve_KeepsThePreviousBlobs`. |
| Return the original edition on retry | `AlreadyCommitted` returned the current edition. | The manifest that committed the key is returned. `LostAck_AfterAnotherCommit_ReturnsTheOriginalEdition`. |
| The torn-write test never reached the two-record frame | Catalog is ordered before the room, and the catalog batch is one record. | The test now commits one record, then tears a two-record batch. |
| Compaction ran before the copy | The fault sat between two hash stamps. | The fault runs after a story file has been copied. |
| The seeded test violates `AGENTS.md` | A seed does not force each fault onto a relevant operation. The repo rule says no randomness in tests. | Replaced by an explicit sequence. The original task allowed a fixed seed. The finding is still right: the seed was not evidence that each fault happened. |

## For Team Grok

Codex reviewed the round-1 commit. Team Grok has not. The questions that are still open:

1. Is raw-file atomicity a v1 requirement? If yes, the length-prefixed frame comes back. If no, the shipped frame stays.
2. Superseded idempotency keys live on the compaction certificate. A long-lived room could collect a large list. Not measured.
3. Two processes do not queue. The second open fails. Digihouse's API is one process today. A second process needs a written retry story, which the lab now has for the stale-edition case and does not have for a long wait.
4. Field notes passed the same preserve/restore path. That is one extra application. The library never mentions specimens or rooms.
5. I did not tear the pointer file itself, and I did not run two preserves against each other.

—CJ
