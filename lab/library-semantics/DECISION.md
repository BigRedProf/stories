# Decision memo

Lab result for Stories #25, #26, and #27. This is a recommendation with executable evidence. It is not accepted. Accepted means Russell said so. Team Grok has not reviewed it.

—CJ

## Answers

**Libraries can be the backup.** A second library, given one published edition, restored the same code bits, bit lengths, offsets, catalog membership, content bytes, and Digihouse fold after the source directory was deleted (`Lifecycle_SurvivesBackupCompactionAndSourceLoss`). A tape series of code bits did not. `TapeCopy_CanTearAcrossStoriesAndOmitsContent` copied story heads one at a time, appended to two stories in the gap, and produced counts that match no edition. The tape file also has no content bytes. Tapes stay useful as a carrier for one story prefix. They do not earn a required layer in front of the library.

**Basic all-or-nothing appends belong in two small places, and nowhere else.** One story batch is one length-prefixed frame. Several stories become visible together when the edition pointer is renamed. Those are different mechanisms. Neither one is a snapshot of arbitrary reads, a lock across libraries, or a SQL transaction. The tests that pass for one guarantee fail for another on purpose.

**Stone and sand are different lifetimes, stored beside the code, not inside it.** Purchases and transfers stayed byte-for-byte after the JPEG was released and the moves were compacted (`StoneHistory_SurvivesSandDeletionAndCompaction`). A gap without a stone certificate fails retention even when the file hash matches the manifest. A flipped byte fails integrity and is not reported as a gap. Exact restore of an older edition brings the deleted sand back. Stopping that requires a newer edition's certificates, supplied on purpose. A missing file is not treated as a deletion.

## What each layer is for

| Layer | Responsibility in this recommendation | Not its job |
| --- | --- | --- |
| Bit | Data's bit. Unchanged. | Records, retention, backup. |
| Code | An opaque bit string with a length. The lab stores `Code.ToByteArray()` plus `Code.Length`. `Codes_KeepBitLengthThroughARoundTrip` restored a 0-bit code, a 5-bit code, and a non-aligned text code. | Story order, editions, blobs. |
| Story | Ordered records. The code is opaque. Beside it: retention, an optional content claim, an optional effect, and the idempotency key. Offsets are not reused. One writer, by an exclusive lock file, same shape as today's `FileStoryLog`. | Cross-story atomicity. Deciding that a purchase is stone. |
| Edition | The hash of a canonical manifest: parent, commit key, and for each story the generation, record count, high water, file length, and file hash, plus each content id's disposition. Publication is the rename of `edition-current.txt` after `Flush(true)`. | A promise that every cross-story read is a snapshot. A promise that the bytes have been copied anywhere else. |
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
| Atomic visibility and crash behavior of one story batch | One length-prefixed frame. A torn write keeps the previous frames only. | `LengthPrefixed_TornWrite_DropsTheWholeBatch`. Room init is two stone records; neither survived the tear. Retry wrote them once. Frame count for the three-record init (catalog plus room) was 2. |
| Commit marker | Works if every reader ignores frames the marker does not cover. The bytes are still on disk. | `CommitMarker_CrashBeforeMarker_HidesBytesANaiveReaderWouldKeep` saw physical records and an empty committed head. Frame count was 5. A reader that trusts any checksummed frame would publish the prefix. |
| Per-frame flush, today's `FileStoryLog` | Right for a one-record append. Wrong for a batch that must appear together. | `PerFrame_CrashAfterFirstRecord_IsVisibleOnTheStoryHeadOnly`. The catalog record was on the story head and not in the edition. Retry rewrote the unpublished prefix and the edition then showed the whole init. |
| Multi-story visibility | Rename the edition pointer after every story frame is durable. | Crash after the frames and before the pointer: story heads had the records, the current edition did not, retry published them and flushed 0 new frames (`CommitMarker_CrashAfterFramesBeforePublish_RetriesWithoutDuplicating`). |
| Concurrency | Expected edition and expected offset, checked in the same lock as the write. One writer process, same as today. | Two threads, one expected edition, one commit (`TwoWriters_SameExpectedEdition_OnlyOneCommits`). A second open throws. Turning the offset check off lets both records land (`SkippingTheOffsetCheck_LetsBothWritersLand`). |
| Lost acknowledgement | The key and body hash ride in the record. A retry of the same key and body returns the original edition even when the client still holds the pre-commit edition id. A different body conflicts. A crash after the pointer is not a rollback. | `LostAck_RetryWithThePreCommitEdition_DoesNotDuplicate`, `LostAck_DifferentBody_Conflicts`, `TimeoutAfterCommit_IsNotARollback`. |
| Consistent read | An edition id is the snapshot. The story head may be ahead of the pointer. A backup copies an edition or a pin, not live heads. | The tape tear is the head-copy failure. `PinnedBackup_KeepsBytesThatCompactionRemoves` copied the pinned edition while the live library compacted. |

A purchase is one stone record. It does not need a multi-record frame or a second story. A transfer is also one record, not a debit on one story and a credit on another. Room initialization is the case that needs both mechanisms: two records in the room story, and the catalog declaration in the same edition. That matches the reopened note that purchases are single-story facts, and it disagrees with the stronger claim that the library should refuse every batch.

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
| Backup vs compaction | Pin first: the backup kept the moves and the JPEG after the source compacted (`PinnedBackup_KeepsBytesThatCompactionRemoves`). No pin: the copy saw the edition change and published nothing (`UnpinnedBackup_FailsWhenCompactionWinsTheRace`). |
| Restore older than a deletion | Exact restore puts the sand back, and says so. `PreventResurrection` with no newer edition is refused and does not write. With the newer edition, certificates are applied and the later lamp purchase is not copied (`RestoreOlderThanDeletion_BringsSandBack_UnlessCertificatesAreSupplied`). |

The evidence against "deletion sticks": an exact restore of an older backup resurrects the JPEG and the moves. That is what the bytes in that edition are. Promising that every offline copy forgets a blob would be false. The lab refuses to guess a deletion from absence, which is the other failure (a truncated backup silently deleting history).

Compaction also gives up a simple story: the live file no longer matches older edition hashes. Verification of an old edition against the live generation fails, correctly, unless you still have the old generation or a backup. "Append-only" describes a generation, not the library's whole life.

## Compatibility

Nothing in `src/` changed. `FileStoryLog` is still one CRC frame per record. The tape CLI is untouched. The lab format magic is `BRPLABPF`, `BRPLABCM`, and `BRPLABLP`, not `BRPSTORY`.

If this were later adopted, it would be a new library directory, filled by copying existing stories into an edition, not an in-place rewrite of story files. One-record appends can stay per-frame. The length-prefixed frame is only for a batch that must appear together. Expected offset is the #22 change and is independent of the frame. Package versions do not move. Stories PR #24 (BitStream) does not conflict with this; the lab uses `Code` from Data 0.10.1, which this repo already references.

## What I would not do next

Do not merge this lab into the shipped packages. Do not delete the tape commands. Do not start the SQL publishing house to make backups work. Do not treat the lab content id as Content's id.

## For Team Grok

No review has come in. The challenges I most want pushed on:

1. Is the length-prefixed frame worth a new on-disk batch, given that a purchase is one record and the edition pointer already hides a partial multi-story write from edition readers? The per-frame test shows the story head still sees the prefix. If every supported reader is edition-pinned, the frame is optional. I do not think Digihouse's story head will be edition-pinned on day one.
2. The commit-marker bytes are a footgun. I rejected the marker because a checksum-valid frame is indistinguishable from a committed record unless the reader knows the rule. If you think a documented marker is enough, the lab already has the format behind `BatchFormat.CommitMarker`.
3. Exact restore resurrects sand. Is the refusal when no newer evidence is supplied loud enough, or should exact restore require the caller to pass a flag that means "I accept resurrection"?
4. Superseded idempotency keys live on the compaction certificate. A long-lived room could collect a large list. I have not measured that.
5. One writer per library is the lock file. Digihouse's API is one process today. Two processes still need a story. The lab rejects the second open; it does not queue it.
6. The lab does not fsync the parent directory after the rename. `Flush(true)` plus `File.Move` is the durability point I am willing to claim. I am not claiming anything about a disk that ignores flush.
7. Field notes passed the same preserve/restore path (`FieldNotes_UsesTheSameBackupContract`). That is one extra application, not a proof that every app fits. The library never mentions specimens or rooms. If a third app needs a fact that is stone in one story and sand in another at the same time, the claim model may be too small.
8. I did not test a crash that tears the edition pointer's temp file, and I did not test two libraries preserving each other at once.

The randomized run used seed `20261110`, 24 steps, and injected tears, crashes before the edition pointer, and crashes after publish. The log is in `evidence/dotnet-test.txt`. Stone identities survived and every current edition verified. That is one seed, not a proof.
