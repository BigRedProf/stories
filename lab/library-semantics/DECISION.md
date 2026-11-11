# Decision memo

Lab result for Stories #25, #26, and #27. This is a recommendation with executable evidence. It is not accepted. Accepted means Russell said so. Team Grok has not reviewed it.

—CJ

## Answers

**Libraries can be the backup.** A second library, given one published edition, restored the same code bits, bit lengths, offsets, catalog membership, content bytes, and Digihouse fold after the source directory was deleted (`Lifecycle_SurvivesBackupCompactionAndSourceLoss`). A tape series of code bits did not. `TapeCopy_CanTearAcrossStoriesAndOmitsContent` copied story heads one at a time, appended to two stories in the gap, and produced counts that match no edition. The tape file also has no content bytes. Tapes stay useful as a carrier for one story prefix. They do not earn a required layer in front of the library.

**Basic all-or-nothing appends belong at the edition pointer for visibility, and in the existing per-frame story file for storage.** Round 1 recommended a new length-prefixed frame. Round 2's comparison says that frame is not required for edition readers. A per-frame crash after the first flushed record leaves that record on the story head and leaves the published edition unchanged (`PerFrame_EditionHidesThePrefix_AndTheNextWriterUsesThePublishedOffset`, and the same shape under SIGKILL in `ProcessKill_AfterFirstFlushedRecord_EditionStaysPut`). The next writer, using the published offset, replaces the unpublished prefix. A length-prefixed frame still does something the pointer does not: a torn multi-record batch drops both new records and keeps the earlier committed record (`LengthPrefixed_TornWrite_DropsTheWholeBatch`). That is raw-file atomicity. It costs a new story frame. Round 3 does not pay that. The reversible default is a head reader that stops at the published length, so the readers that exist today do not hear a flushed batch the next writer replaces. Ship expected offset (#22), an edition pointer, the current per-frame file, and that cutoff.

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

A purchase is one stone record. A transfer is one record. Room initialization is several records and a catalog line. Edition publication hides that set from edition readers until the pointer moves. It does not hide it from `FileStoryteller`, `Watch-Story`, or a live listener. Those read the story head, and a flushed per-frame prefix is visible to them until the next writer replaces it. A length-prefixed frame removes only the partial-batch case: the head cannot see `seed` without `ready`, because they are one frame. It does not remove the other phantom. A frame that has been flushed and not yet published is still on the head, and a later writer can still replace that unpublished tail. The cutoff that hides both is a head reader that stops at the published length. That is a reader change. The frame is a format change, and it only covers the prefix.

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

Codex reviewed `34a8f6d` after that. Three more findings, all real:

| Finding | What was wrong | What changed |
| --- | --- | --- |
| Resurrection consent was checked on one edition and a different edition was copied | Naming stone edition E1 still copied the backup's current edition when that current edition contained sand. | `Restore` copies the edition it checked. `Restore_CopiesTheRequestedEdition_NotTheCurrentOne`. |
| A second shipped file, or a later lab append, parsed the first shipped file as a lab log | Only the story being adopted was exempted. `DescribeStory` and `ReadQuarantine` opened the other `BRPSTORY` bytes. | A published `shipped-v1` prefix is carried forward, and those files are skipped by the lab quarantine reader. The adoption test adopts a second file and then records a purchase. |
| The two-process lock test could pass without the locks overlapping | If one process finished before the other started, the late process got `PositionConflict` instead of "already has a writer". | The holder opens, signals, and waits. The challenger opens only after that signal. The holder is released only after the challenger has failed. |
| A published restore could be corrupted by a later retry | `CopyEdition` overwrote a valid destination blob before checking the backup hash. | A destination that already verifies is not copied again. Source bytes are checked against the manifest before any write. `RestoreRetry_DoesNotReplaceAValidBlobWithCorruptBackupBytes`. |

## Cross-review from #29

Read at `e6737b9`, before the publish shortcut below. We agree that a library is the backup, tapes are at most a carrier, stone is not erased in v1, absence is not a deletion, and a pin restores its exact edition.

On head readers I agree with the fact and not with the remedy. `test_pointer_design_is_atomic_for_edition_readers_but_head_readers_see_phantoms` is the same result as `PerFrame_EditionHidesThePrefix_AndTheNextWriterUsesThePublishedOffset` and `ProcessKill_AfterFirstFlushedRecord_EditionStaysPut`. Calling that acceptable "if callers read editions" ignored that the callers which exist read heads. A batch frame fixes the case where the head sees `seed` and not `ready`. It does not fix a head that sees a flushed batch the pointer has not published, which the next writer may replace. Round 3's default is the published-length cutoff, not edition-only readers.

On publish cost, one movement in this lab, after unchanged story hashes are reused and the manifest is parsed once, took 4 ms with 10 stories, 64 ms with 1,000, and 463 ms with 5,000. Those times include `Flush(true)` and a directory fsync, which the Python model does not. The cost still grows with the number of stories, because the manifest lists all of them and each file is stated. `OneMovement_ReusesUnchangedStoryHashes` keeps the 10 and 1,000 cases. Rehashing every story on every movement was the larger term and is no longer done.

On compaction, after 300 moves the next ordinary movement preserved as a suffix. The preserve after compaction copied at least the whole new generation, which was more than that suffix (`Compaction_NextIncrementalCopiesTheNewGeneration`). The edition id changes because the bytes changed. An old edition still means those old bytes, which the backup holds. A header chain that keeps the id while dropping payloads is a different promise. It does not preserve the exact bits of the removed sand in the live file. I would not switch the edition identity to that. Round 3 measures that copy at house scale. The position is unchanged.

## Round 3

Russell is away, so these are reversible defaults. None of them are accepted. The suite at `9a1ea6de732bb9d065684d9c488a91adac38a9ca` passed 55 tests and failed 0. The log is `evidence/dotnet-test.txt`. There is no random seed.

**Head readers stop at the published length.** `PublishedHead` is the lab stand-in for `FileStoryteller`. `PublishedListener` is the stand-in for a live listener. Both read the story file and stop at the byte length named by the current edition. Under SIGKILL, a flushed two-record batch and a torn per-frame prefix are both invisible to that head, and the listener never yields the records the next writer replaces (`PublishedHead_HidesFlushedAndTornBytes_AcrossProcessKills`). On one story, with a 1 ms poll and fsync included, the median wait from the start of the append until the listener yielded the record was 3.5 ms. The listener trailed a raw read of the same file by 2.7 ms, and it trailed `Append`'s return by 1.1 ms (`evidence/round3-head.txt`).

Keeping heads raw and adding a length-prefixed frame does not do this job. A frame that has been flushed and not yet published is still on the raw head: the length-prefixed kill in that same test leaves `record-0` and `record-1` in `StoryLog.Read` and leaves them out of `PublishedHead`. The next writer can still replace that tail, and a raw listener hears it. The frame's remaining win is the torn batch, which a raw reader already drops. It is a new story format, and it does not make two stories appear together for a raw head. The cutoff does both, and the cost is that visibility waits for the pointer. On one story that wait is a few milliseconds. On the full-manifest publisher below, the same wait is the publish time, because the listener cannot see the record until the pointer moves.

**Publish the stories that changed.** `JournalLibrary` appends one checksummed frame naming the parent edition and the prefixes that changed, fsyncs that frame, then fsyncs the directory after the pointer rename. The edition id is the hash of the parent id plus that delta. Reading an id replays to that frame and no further, so the id is one point in time (`Journal_ReadEdition_IsOnePointInTime`). A kill after the journal fsync and before the pointer hides the batch from the head; a kill after the directory fsync keeps it (`Journal_ProcessKill_BeforePointer_HidesTheBatch`, `Journal_ProcessKill_AfterPointer_KeepsTheEdition`). Every N commits the full map is written as a checkpoint. After the journal prefix was zeroed, the tip and the two editions after the checkpoint still read (`Journal_Checkpoint_ReadsAfterThePrefixIsDamaged`). One movement, fsync included (`evidence/round3-publish.txt`):

| Stories | Full manifest | Journal delta |
| --- | ---: | ---: |
| 10 | 4.8 ms | 2.2 ms |
| 1,000 | 61.6 ms | 3.4 ms |
| 5,000 | 552.4 ms | 5.1 ms |
| 10,000 | 1,601.1 ms | 3.8 ms |

The journal samples sit between 2.2 and 5.1 ms while the library grows a thousand times. That is the fsync floor plus noise, not a walk of every story. The manifest design is the one that reaches 1.6 seconds. The journal publish is not wired into the backup copier yet. A backup of it would copy the journal, or a checkpoint plus the tail, and that copy is not built here. The manifest library is unchanged and is still what the preserve tests use.

**Compaction still copies the new generation, and the house-scale number is now measured.** Twelve rooms, 400 parlor moves plus one more, 40 moves in each other room, no photo blobs. The preserve of that one extra move copied 239 bytes. After compaction the parlor story is 4 records and 36,578 bytes, and the next preserve copied exactly those 36,578 bytes. The new edition manifest is 1,560 bytes and is not in that count. The previous generation, 95,970 bytes, stays on the backup (`evidence/round3-compaction.txt`, `Compaction_IncrementalCopiesTheNewGeneration_AtHouseScale`). Most of the 36,578 bytes is the stone certificate's list of the 401 superseded move keys, not the four surviving records. #29's header chain, on their report, copied 3 ledger records after compacting 300 moves. This design sends about 36 KB once for this room and keeps the old generation as well, because the old edition still means those old bytes. I would not drop that so the incremental can be three records.

## For Team Grok

Codex reviewed the round-1 commit. Team Grok has not. The questions that are still open:

1. Is a raw head, one that does not stop at the published length, a v1 requirement? Round 3's default says no: head readers stay, and they stop at that length. The length-prefixed frame then stays a lab comparison. If a raw head must be the listener, the frame still does not hide a flushed batch, and the cutoff is the change that does.
2. Superseded idempotency keys live on the compaction certificate. At house scale that list is most of the compacted generation: 401 parlor moves became a 36,578-byte file of 4 records. The incremental copies that whole file.
3. Two processes do not queue. The second open fails. Digihouse's API is one process today. A second process needs a written retry story, which the lab now has for the stale-edition case and does not have for a long wait.
4. Field notes passed the same preserve/restore path. That is one extra application. The library never mentions specimens or rooms.
5. I did not tear the pointer file itself, and I did not run two preserves against each other.

—CJ
