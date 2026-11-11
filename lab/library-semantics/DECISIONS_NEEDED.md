# Decisions needed

Russell's calls. Each one has a recommendation, the consequence of following it, and the default the lab already uses so work can continue. None of these are accepted.

—CJ

## 1. Is a second library the backup, with tapes only as a carrier?

Recommendation: yes. Keep the tape CLI and #21 until a library exists. Do not make `TapeLibrary` a dependency of preservation.

Consequence: backup work moves to edition copy, pin, verify, and restore. #21 stays a single-story carrier, not the design for Digihouse. Data #87 stays a storage-provider question.

Default in the lab: `Preserver` copies an edition into another library. `TapeSeriesBackup` exists only to show the tear and the missing blobs.

## 2. Do we add a length-prefixed batch frame, or only the edition pointer?

Recommendation: do not add the frame. Head readers stay, and they stop at the published length. That is D8 below. A length-prefixed frame hides a torn batch from a raw reader. It does not hide a flushed batch the pointer has not published, and a raw listener can still hear a record the next writer replaces.

Consequence: `FileStoryteller` and the live listeners learn the published length. A record becomes visible when the edition pointer is durable. On one story in this lab that was a median of 3.5 ms from the start of the append, 2.7 ms after a raw read could already see the frame. No shipped story format changes.

Default in the lab: `PublishedHead` and `PublishedListener`. Both formats still run. The length-prefixed frame remains a comparison, not the default.

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

## 7. Does every append rewrite a manifest of the whole library?

Recommendation: no. Publish one journal frame for the stories that changed, fsync it, then move the edition pointer and fsync that directory. The edition id is the hash of the parent id plus that delta. A checkpoint every N commits is a cached map so a reader does not replay from the first commit. Keep the full manifest in the lab as the comparison.

Consequence: one movement stays near the fsync floor as the library grows. In this run, 10,000 stories cost 3.8 ms on the journal and 1,601.1 ms on the full manifest, fsync included. A reader of an old id still sees that point in time and not later commits. `Preserver` does not yet copy a journal library. The manifest library remains the one the backup tests use.

Default: `JournalLibrary`, checkpoint every 1,000 commits unless a test says otherwise. The manifest `Library` is unchanged.

## 8. Do v1 readers read story heads or editions? (D8)

This is the same question as D8 on #29. Round 3 does not wait on it.

Recommendation: head readers stay. `FileStoryteller`, `Watch-Story`, and Digihouse's live listeners read heads today, and a live room needs each movement to show up. They stop at the last published length, so a flushed batch the next writer replaces is never delivered. Edition reads stay the way to name a point in time across stories. Callers do not have to switch to edition-only reads for v1.

Consequence: a listener does not see a record until the pointer is durable. That is the publish time plus one poll: a few milliseconds on the journal, and the full manifest rewrite if publication still rewrites every story. A raw head that ignores the published length still hears phantoms. The length-prefixed frame does not close that hole.

Default: `PublishedHead` and `PublishedListener`. Reversible by reading the story file without the cutoff. Nothing here is accepted.

## 9. Does v1 restore apply newer deletion evidence onto an older edition?

Recommendation: no. v1 restore copies one checked edition and stops. It does not run erasure replay. A deletion is kept by preserving the edition that already contains it, and restoring that edition. An older edition that still contains sand is restored only when the caller sets `AcceptResurrection`.

Consequence: there is no operation that restores an older edition and also keeps a later deletion deleted. The lab's `PreventResurrection` path stays an experiment. The eleven preserve, restore, and erasure-replay bugs in `DECISION.md` are point fixes, not a proof that replay is finished. Choosing replay later means a new design, either ordinary idempotent appends or a crash-and-retry harness, not another fix on the current loop.

Default: `PreventResurrection` remains implemented in the lab and is not the v1 recommendation. Exact restore of sand still requires `AcceptResurrection`.
