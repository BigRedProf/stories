# Decisions needed

Russell's calls. Each one has a recommendation, the consequence of following it, and the default the lab already uses so work can continue. None of these are accepted.

—CJ

## 1. Is a second library the backup, with tapes only as a carrier?

Recommendation: yes. Keep the tape CLI and #21 until a library exists. Do not make `TapeLibrary` a dependency of preservation.

Consequence: backup work moves to edition copy, pin, verify, and restore. #21 stays a single-story carrier, not the design for Digihouse. Data #87 stays a storage-provider question.

Default in the lab: `Preserver` copies an edition into another library. `TapeSeriesBackup` exists only to show the tear and the missing blobs.

## 2. Do we add a length-prefixed batch frame, or only the edition pointer?

Recommendation: both, for different jobs. The frame is for several records in one story that must appear together (room seed and ready). The pointer is for several stories (catalog plus room). One-record purchases stay one record. Do not add a commit marker.

Consequence: a future story file is not a pure extension of today's per-frame `BRPSTORY` layout. Shipping it is a new format, copied forward, not an in-place upgrade. This lab does not ship it.

Default: the lab library is created with `BatchFormat.LengthPrefixed`. The other two formats remain in the tests.

## 3. Does restoring an old backup bring deleted sand back?

Recommendation: yes, on an exact restore, and say so in the result. Preventing that is a second step that needs a newer edition's certificates. Refuse the request when those certificates were not supplied. Do not infer a deletion from a missing blob.

Consequence: operators who want a deletion to stick must keep a backup from after the deletion, or pass that edition in. An air-gapped older tape will resurrect the blob. That limit should be written next to the restore command when one exists.

Default: `Restore` is exact. `PreventResurrection` without `NewerEvidence` returns refused and writes nothing.

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
