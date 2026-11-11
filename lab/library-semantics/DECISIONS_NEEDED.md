# Decisions needed

Russell's calls. Each one has a recommendation, the consequence of following it, and the default the lab already uses so work can continue. None of these are accepted.

—CJ

## 1. Is a second library the backup, with tapes only as a carrier?

Recommendation: yes. Keep the tape CLI and #21 until a library exists. Do not make `TapeLibrary` a dependency of preservation.

Consequence: backup work moves to edition copy, pin, verify, and restore. #21 stays a single-story carrier, not the design for Digihouse. Data #87 stays a storage-provider question.

Default in the lab: `Preserver` copies an edition into another library. `TapeSeriesBackup` exists only to show the tear and the missing blobs.

## 2. Do we add a length-prefixed batch frame, or only the edition pointer?

Recommendation: do not add the frame until Russell answers whether v1 readers are story heads or editions. That is the same question as D8 on #29. Today's `FileStoryteller`, `Watch-Story`, and the live listeners read heads. For those readers, a pointer does not hide a flushed prefix. A length-prefixed frame hides a torn prefix and does not hide a flushed batch that the pointer has not published yet. The change that hides both from a head reader is to stop that reader at the published length.

Consequence: no shipped format change while that question is open. If the answer is "heads stay, and they must not hear a record that is later replaced," the storyteller has to learn the published cutoff. The frame alone does not do that job.

Default in the lab: both formats still run. Edition readers are the consistent read. Head reads are available and can see unpublished frames.

## 3. Does restoring an old backup bring deleted sand back?

Recommendation: only when the caller sets `AcceptResurrection`. Otherwise exact restore of an edition that contains sand is refused. Applying a newer edition's certificates does not need that flag, and it does not copy later business events. Do not infer a deletion from a missing blob.

Consequence: a restore tool cannot resurrect a JPEG by default. An operator who wants the old bytes back has to say so. An air-gapped copy still contains them.

Default: `AcceptResurrection` defaults to false. `PreventResurrection` without `NewerEvidence` returns refused and writes nothing.

## 4. Can stone ever be erased?

Recommendation: not in v1. Compaction and release reject stone. A legal erasure, if it is ever required, is a separate explicit operation with its own audit story, not a flag on compaction.

Consequence: the lab cannot model a court-ordered deletion of a purchase. It can model deletion of a JPEG and compaction of movement.

Default: stone compaction and stone release return `Rejected`.

## 5. Does v1 preservation depend on SQL or a publishing house?

Recommendation: no. Command-side SQL can wait. The backup contract is the edition and the bytes.

Consequence: #23's mismatch recovery (library behind SQL, SQL behind library) is still unsolved and is not required to back up a library. Do not block #25 on it.

Default: the lab has no SQL.

## 6. Where does the idempotency key live?

Recommendation: on the append, beside the code, checked by the library. A blind retry of the same key and body is `AlreadyCommitted`. A different body is a conflict. Expected offset stays the concurrency check and is still worth doing as #22 even before any of this ships.

Consequence: callers must send a stable key. The library stores it. Compaction must copy superseded keys onto the certificate or a retried move is appended again.

Default: `AppendRequest.IdempotencyKey` is required.
