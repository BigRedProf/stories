# Decisions for Russell

Each has a question, my recommendation, the consequence, and the reversible
default the prototype uses meanwhile. None blocks further work.

## D1. What happens to tapes?

**Recommendation.** Make Library-to-Library preservation the design. Freeze tape
orchestration at what Memorial needs to be safe today. Keep
`BigRedProf.Data.Tape` untouched. Later, consider a tape *provider* or *export*.

**Consequence.** PR #19 either merges as the interim backup or stays parked;
#21 is superseded for new work but not closed. If you would rather keep tapes
as a second, independent backup format for defence in depth, that costs the
upkeep of two paths and tapes still cannot forget sand.

**Default in use.** Nothing about tapes was changed or closed.

## D2. Do multi-story commits belong in v1?

**Recommendation.** Define `commit` as a Library contract in v1. Implement it
with a commit log on plain disk. Do not route ordinary appends through it.

**Consequence.** Every storyteller implementation must honour one extra rule
(stop at an uncommitted marked frame). If you say no, v1 is single-story atomic
batches only; story creation and content claims then need a repair path for
"catalog entry without story" and "claim without reference", and #23's
no-cross-story stance stands.

**Default in use.** Both exist in the prototype; the application uses the commit
only for purchase completion and story creation.

## D3. Approve a story file version 2 for prototyping in .NET?

**Recommendation.** Yes, on a branch, behind a version number, with version 1
files still readable. It carries batch frames, entry headers and the chain.

**Consequence.** This is a persisted-format change under AGENTS.md, so I have
not started it. Without it, atomic batches and stone/sand cannot move past the
Python model.

**Default in use.** No .NET work.

## D4. Can stone ever be erased?

**Recommendation.** Not in v1. If a legal erasure is ever required, make it a
distinct, loudly recorded operation that leaves the header and a record of who
ordered it, never a variant of sand removal.

**Consequence.** A stone payload containing personal data cannot be removed by
any supported path. Applications must keep personal data out of stone payloads,
or in sand blobs referenced from stone.

**Default in use.** Refused, always.

## D5. Who may authorize a sand removal?

**Recommendation.** The application decides and the Library records. The Library
should require a non-empty authority and reason but not validate them in v1.

**Consequence.** Anyone who can call the Library can delete sand. If that is too
weak, authorization needs signed evidence, which is a larger design.

**Default in use.** Free-text authority and reason on every record.

## D6. May a restore proceed when it cannot prove no sand was deleted later?

**Recommendation.** Only with a surviving copy of the retention ledger as
witness, or with a named person accepting that deleted sand may come back.
Otherwise refuse and write nothing. This is PR #28's rule, which I adopted.

**Consequence.** During a real disaster this adds one deliberate step before
service resumes. The alternative is serving possibly resurrected sand silently.

**Default in use.** Refused unless `witness` or `accept_resurrection` is given.

## D7. Must removal evidence reach a second library before bytes are reclaimed?

**Recommendation.** Yes whenever a backup relationship exists. The ledger is
small; push it on every removal.

**Consequence.** A deletion waits on, or fails with, the escrow being reachable.
Without this rule, a deletion made after the last backup is forgotten if the
primary is lost.

**Default in use.** Optional hook; the demo and tests turn it on.

## D8. Do v1 readers read story heads or editions?

This is the question on which this lab and PR #28 give different answers to D2
and D3.

**Recommendation.** Assume head readers stay. `FileStoryteller`, `Watch-Story`
and Digihouse's live listeners read heads today, and live rooms need each
movement visible at once.

**Consequence.** If head readers stay, a pointer-only design lets them hear
entries that are later replaced, and a batch frame (D3) is needed. If you
decide all v1 readers are edition-pinned, PR #28's design is cheaper, changes no
format, and D3 can wait.

**Default in use.** Head readers; one frame per append.

## D9. Which compaction mode for movement?

**Recommendation.** Anchor mode for pure-sand stories such as movement (86%
smaller in the scenario; tombstones saved 5%). Tombstone mode only for mixed
stories, where stone entries must stay in place.

**Consequence.** After an anchor, editions older than it cannot be re-verified
from that library alone. Their identities remain on record.

**Default in use.** Both modes are implemented and tested.
