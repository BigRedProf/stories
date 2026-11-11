# Decision memo: Bit → Code → Story → Library, backup, atomic appends, stone and sand

For Russell. Covers stories #25, #26, #27 and reopens #23.
Draft of 2026-11-11. Evidence is the Python prototype in this directory:
50 deterministic tests ([results/tests.txt](results/tests.txt)), a narrated run
([results/demo.txt](results/demo.txt)) and timings
([results/bench.txt](results/bench.txt)). Run instructions are in
[README.md](README.md).

**Status of everything below: implemented and tested in a prototype. Not
independently reviewed (Team Grok has not seen it). Not accepted. No production
code, persisted format, package or deployment was touched.**

## Summary

1. **Libraries can be the backups.** A backup Library preserved, verified and
   restored the Digihouse scenario bit for bit with the source deleted, through
   full, incremental, interrupted and racing runs. Tapes showed no capability a
   Library provider cannot supply. One thing counts for tapes: PR #19 works
   today against real services and the Library does not exist yet.
2. **Yes to all-or-nothing appends, at two levels with different costs.** A
   single-story atomic batch is cheap and fixes a real defect in today's file
   story. A multi-story commit is worth having as a *Library contract*, used
   sparingly; it should not sit on the path of every append.
3. **Stone and sand work if history identity is built from entry headers, not
   payloads, and if removal is always a recorded act.** Then sand can vanish
   without changing a position, a chain or an edition, and a verifier can tell
   an authorized removal from a loss.

A second, independent lab reached the same three headline answers in .NET
(PR #28, by CJ). Where the two differ is set out under "Comparison with PR #28";
the differences are more useful to you than the agreements.

The three answers depend on one shared choice, so it comes first.

## The one structural choice

Every story entry has a header: position, class (stone or sand), exact bit
length, and the code's hash. The story's running hash chains **headers only**.
The payload is stored beside its header and may later be absent.

Consequences, each tested:

- Deleting a sand payload or compacting movement leaves every position, chain
  value and edition identity unchanged. In the demo only `_retention` changes
  identity after three removals.
- An edition captured before a removal still verifies afterwards, reporting 301
  entries as "removed with authorization" instead of failing.
- "Does the backup hold a prefix of the source?" is one hash comparison. Without
  it the prototype accepted one foreign entry into a backup before noticing; a
  test caught that and the comparison fixed it.
- The class is inside the hash, so nobody can quietly relabel stone as sand.

This answers the open question in the #23 review comment (should stories keep a
running hash): yes, over headers.

## Responsibilities

| Layer | Owns | Does not own |
| --- | --- | --- |
| **Bit** | The unit. Exactness is to the bit; a code may end mid-byte. | |
| **Code** | An immutable bit sequence with an exact length. Identity is the hash of length and bits. | Meaning, schema, retention. |
| **Story** | One independently ordered, append-only sequence of entries with zero-based positions. Atomic append of one or more codes, optional expected position, optional retry id. The header chain. Single writer. | Other stories, catalog, content, policy decisions. |
| **Library** | The catalog (a story), the stories, content blobs with claims, the retention ledger (a story), and a commit log that publishes multi-story commits and records editions, pins and preservation milestones. The accept rule for replication. Verification. | Business rules, sagas, schedules, what counts as stone for an application. |
| **Edition** | An immutable, sorted list of (story, next position, chain). It *identifies* history, independent of storage. Recorded editions also act as *witnesses*: a story that later falls short of one is damage. | Publishing commits (the commit log does that), and any promise that the bytes are still retained. |

Editions identify; the commit log publishes. Making every commit an edition
would put a full manifest on the write path for no gain.

A Publishing House in #23's sense becomes "a Library provider that has real
transactions". See the SQLite row in the timings.

## Question 1: can libraries serve as backups?

**Recommendation: yes. Make Library-to-Library preservation the design. Keep
tapes only as an optional provider or export, not a required layer.**

How it works in the prototype: capture an edition at the source; pin it; copy
what the destination lacks by (story, position) through the accept rule; copy
claimed blobs by hash; verify the destination against the edition; then append
a `preserved` record to the destination's commit log. That record's fsync is the
durability point of the backup. Restore is the same copy in the other direction
from the newest *completed* preservation.

Evidence (all in `tests/test_preserve.py` unless noted):

| Behaviour | Result |
| --- | --- |
| Full, then incremental, then a no-change run | 245, then 162, then 0 entries copied |
| Restore with the source deleted | Every header and payload bit, the edition identity and the folded Digihouse state are equal. 350 movement codes of 34 bits each survive exactly |
| Empty story | Stays in the catalog and the edition at position 0 |
| Crash at every third durable step of a backup, two crash modes | Always either visibly incomplete or complete with a lost acknowledgment. A re-run always finishes, copying 0 entries in the lost-ack case |
| Source written during the backup | The backup equals the captured edition exactly; the next run copies the 11 newer entries |
| Occupied restore target; backup with no completed preservation | Refused |
| Interrupted restore | Resumes and completes |
| Flipped payload bit, truncation on a clean frame boundary, altered or missing blob | Open fails, or verification fails and restore reports incomplete. Never "complete" |
| Source missing a payload with no removal evidence | Backup is "incomplete" and lists the entry |
| Source restarted empty (the #21 concern) | Reported incomplete: "the source has lost history". No completion record; the earlier preservation stands |
| Different history under the same story names | Reported incomplete: "histories diverge". Nothing foreign is accepted |

Verification returns four separate verdicts: integrity (hashes and chains),
completeness within the edition, retention compliance, and whether the
application's own fold can rebuild its state.

### Do tapes still earn a place?

| Capability claimed for tapes | Can a Library supply it? |
| --- | --- |
| Bounded, immutable, sequential segments on cheap or offline media | Yes, as a provider. `ObjectFS` puts every write under a new key and never overwrites; a full backup and restore ran on it with zero deletes |
| Integrity chain across a series | Yes. The header chain and editions cover it, and also cover the catalog and blobs, which a tape series does not |
| Incremental from a checkpoint | Yes, and resumable from any prefix without a stored checkpoint |
| Whole-library scope | Tapes have no catalog; PR #19 added a story listing endpoint and a weekly manifest to compensate |
| Content blobs | Not in tapes |
| Forgetting sand | **Worse on tapes.** A series is append-only, so a deleted photo stays on tape until the series is rewritten. The same is true of any write-once medium: the prototype's backup on write-once objects completes but reports the photo as "retired, pending reclamation" |
| Readable without a restore step | A backup Library can be opened and verified in place. A tape must be restored first |

What tapes still have going for them, stated as strongly as I can:

- **They exist and are proven.** PR #19 round-tripped five stories and 121
  things between two live services. The Library here is a Python model.
- **Format diversity.** A backup written by different code will not share a bug
  with the primary. A Library backing up a Library shares everything.
- **A tape cannot be mistaken for a live service.** A backup Library is a
  Library; something could write to it. The prototype closes this with a
  durable "backup" role that refuses application writes and accepts only
  preservation.
- **Today's deployed API holds stories in memory,** so tapes are the only
  durability the running system has. Removing them before a durable Library
  ships would be a regression.

None of these is a capability the Library design cannot reasonably supply, which
was your test. They are reasons about timing and defence in depth. My
recommendation is to stop investing in tape orchestration beyond what Memorial
needs now, keep `BigRedProf.Data.Tape` as it is, and revisit a tape *provider*
or *export* once the Library exists. This is decision D1.

## Question 2: all-or-nothing appends

**Recommendation: yes to both, scoped differently.**

- **Story level: every append is one atomic batch**, with an optional expected
  position and an optional retry id.
- **Library level: a `commit` of writes to several stories**, with optional
  expected positions or expected edition and a required transaction id. Used by
  the Library itself (story creation with its catalog entry; a content claim
  with the entry that references it) and by applications for the few facts that
  truly span stories. Ordinary appends do not go through it.

### The four guarantees, kept separate

| | Today (A) | Story batch (B) | Library commit (C) | Journal (D) | Pointer (E) | SQLite (S) |
| --- | --- | --- | --- | --- | --- | --- |
| Operation | append things | append a batch to one story | commit to many stories | commit to many stories | commit to many stories | commit to many stories |
| **Atomic visibility** | No. A reader, or a crash, can see part of a batch | Yes, one story | Yes, all stories | **No.** Stories update one by one | Yes for edition readers. **No for story-head readers** | Yes |
| **Durability point** | fsync per thing; "acknowledged" means the last thing | The one fsync | The commit-log fsync | The journal fsync | The pointer swing | COMMIT |
| **Concurrency control** | None | Expected position → conflict | Expected positions or edition → conflict | Not prototyped | Not prototyped here (PR #28 has it) | Row predicate |
| **Consistent reads** | No | Within the story | Only via an edition-pinned read | No | Every edition read is a snapshot | Snapshot |
| Unknown outcome | Retry duplicates | Retry with the same id is a no-op | Same | Not prototyped | Not prototyped here | Application key |
| Recovery | Truncate torn tail | Truncate torn tail | Also truncate frames whose transaction is not in the commit log | Replay journal | Next writer overwrites the unpublished tail | Engine |
| Detects loss of acknowledged tail | **No** | Only after an edition is recorded | Yes, the commit log is a witness | n/a | Yes, the manifest is a witness | Engine |
| Format change to the story file | none | new frame | new frame | none | **none** | n/a |
| Provider needs | append + fsync | same | same, plus one extra log | same, data written twice | same, plus atomic rename | a SQL engine |

E is my model of the design PR #28 recommends: keep today's per-thing frames and
publish by writing a full manifest and swinging a pointer.

Evidence (`tests/test_atomic.py`):

- **Today's append tears.** Crashing a four-thing append at every durable step
  leaves 0, 1, 2, 3 or 4 things. A retry after a lost acknowledgment leaves 8.
  A flipped bit in the last acknowledged frame silently shortens the story to 3.
- **B is all-or-nothing** across 9 crash cases (each step, three crash modes),
  and durable once acknowledged. Four threads racing on one expected position:
  exactly one wins, 25 rounds out of 25. The retry id survives a restart.
- **C is all-or-nothing** across 27 crash cases for a purchase touching three
  stories. After every crash, retrying the same transaction id lands it exactly
  once. Story creation is atomic with its catalog entry and first entries.
- **A reader mid-commit sees none of it,** at every stage.
- **D is durable but not atomically visible:** a live reader observed one story
  updated and two not.
- **E is all-or-nothing for edition readers** across its crash matrix, with no
  change to the story file. But a reader of the story head was told `a1, a2`,
  the writer died before the pointer moved, and the next writer replaced them.
  What that reader heard is not in history. `FileStoryteller` and Digihouse's
  live listeners are head readers today.
- **Atomic publication is not a snapshot.** Two ordinary reads straddling a
  commit saw a vase placed in a room that nobody had bought. The same two reads
  pinned to an edition agreed. This distinction has to be in the reader contract.
- **A raw file copy taken mid-commit** either recovers to the pre-commit state
  or is refused as damaged. It is never silently half a commit. Reading the
  story files naively, ignoring transaction markers, does show half a commit,
  which is the argument for backups going through editions.

### Reader guarantees to publish

| Reader | Guarantee |
| --- | --- |
| Ordinary storyteller | Sees only durable entries, in order, never part of an append or commit |
| Live listener | The same, delivered per append or commit. Two listeners on two stories are not synchronized |
| Edition-pinned read | A consistent cut across all stories: no commit is split |
| Independent reads of several stories | No cross-story guarantee. May observe a state between two commits |

### Cost

Same disk, real fsync, ratios only ([results/bench.txt](results/bench.txt)):

| Workload | Today | B | C | E (pointer) | SQLite |
| --- | --- | --- | --- | --- | --- |
| Purchase (4 entries, 3 stories) | 610 µs, not atomic | n/a | 707 µs | 1,152 µs | 279 µs |
| One movement | 131 µs | 169 µs | 390 µs if forced through the commit log | 463 µs with 10 stories in the library; 741 µs with 1,000; 4,027 µs with 10,000 | 173 µs |
| Burst of 50 movements | 6,229 µs | 271 µs | | | |

Run-to-run noise on this disk was about 20%, so read only the large ratios. B
costs about one fsync, like today, and makes a burst about 23 times cheaper. C
costs about what today's non-atomic writes cost for a purchase, and a bit over
twice a plain append if single movements were forced through it, which is why
they should not be. E's cost grows with the number of stories in the library,
because each publish rewrites the manifest. That is my model's behaviour; PR
#28's implementation also writes a full manifest per publish, but I have not
timed theirs.

### Evidence against my own recommendation

- **SQLite did the purchase about 2.5 times faster than C, with snapshots for
  free.** That is #23's position: when you need cross-story transactions, use a
  provider that has them. I think the answer is to define `commit` as the
  Library contract and let providers implement it, with the commit log as the
  plain-disk implementation, not to hand-build C everywhere.
- **C puts a transaction check in every reader,** which #23 warned about. In
  the prototype it reduces to one rule (stop at the first marked frame whose
  transaction is not committed, the same shape as "stop at a torn tail"), but it
  is a rule every storyteller implementation must carry.
- **The commit log is a single point of serialization** and grows without bound.
  It needs checkpointing, which I did not build. Reopening a library with 2,300
  commits and 6,000 moves took 30 ms as a full scan.
- **Digihouse may not need C at all.** Its design already puts one authoritative
  fact in one story and deduplicates by event id. The cases that needed C in the
  scenario were the Library's own (catalog and content claims). If content
  claims were dropped or made repairable, B alone might do for v1.

Limits to state in any contract: one writer process per library in the
prototype (competing writers are concurrent callers of that process, as with the
API service today); a commit must fit in memory; retry ids are remembered
indefinitely, which needs a window.

## Question 3: stone and sand

**Recommendation.**

| Question | Answer in the prototype |
| --- | --- |
| Where is policy declared? | Per **entry** (class in the hashed header), constrained per **story** (declared stone, sand or mixed in its catalog entry), and per **content claim**. Per library only for backup behaviour |
| Purchases and ownership | Stone story. Removal and compaction are refused |
| Photos and models | The *reference* is a stone entry; the *bytes* are a blob under a sand claim. Releasing the claim removes the bytes; the reference still reads "removed" |
| Movement | Sand story. Compaction replaces a prefix with an application-supplied summary |
| Mixed records | Allowed in a mixed story. They commit together and retire independently |
| Shared blob | Kept until its last claim is released. A stone claim is never releasable |
| Default | A story must declare its policy. System stories are stone |
| Sand to stone | `promote`: a stone ledger record that forbids later removal, and travels to backups |
| Stone to sand, or erasing stone | Not supported. Decision D4 |
| Who authorizes | Every removal record carries a reason and an authority string. The Library records them; it does not judge them. Decision D5 |

Four separate acts, in this order: **logical retirement** (a stone record in the
`_retention` ledger), **compaction** (the same, carrying a summary),
**physical reclamation** (bytes removed, only after the record is durable and
no edition is pinned), **erasure from all controlled copies** (the ledger
reaching each copy and being applied there). Offline and write-once copies
cannot be reached; the prototype reports them as pending rather than pretending.

Evidence (`tests/test_retention.py`):

- Ownership history and owners are identical before and after deleting a photo,
  removing a note and compacting 300 movements. Object positions are identical.
- Crashing a deletion at every step never leaves bytes gone without evidence.
- Verification separates the cases: sand missing with no evidence is an
  unauthorized removal; stone missing is stone loss and the application fold
  fails; a blob missing under a live claim is incomplete; all three differ from
  an authorized removal, which passes.
- **Compaction modes.** Keeping headers ("tombstone") saved 5% on movement,
  because a 34-bit move is smaller than its own header. Dropping the prefix
  behind an anchor saved 86%. Both keep positions, head and chain. After an
  anchor, an older edition below it is reported as "unverifiable below anchor",
  not as verified; its identity remains on record.
- **Compaction policy is the application's.** Digihouse movement keeps latest
  positions. The scoreboard keeps finished set scores, which latest-position
  would have destroyed. The Library stores whatever summary it is given.
- **Backup racing compaction.** Pinned: the backup gets its exact edition,
  reclamation waits, and the next run carries the removals across. Unpinned: the
  copier notices, retries at a newer edition and completes on attempt 2, in both
  compaction modes. Under constant churn with retries exhausted it reports
  incomplete and writes no completion record.
- **A backup may keep sand longer** by policy; verification then reports 302
  items as retired but still held.
- **Restore from a backup older than a deletion.**
  Without newer evidence the restore is refused and nothing is written. If a
  named person accepts the risk, it completes: stone is exact, the deleted photo
  is back, and the record says the restore is not attested and who accepted.
  With the ledger from an escrow, the three newer removal records are applied
  and the restored state equals the state at the moment of loss, bit for bit.
  The mechanism is that evidence leaves the building before bytes are reclaimed.
  The ledger is small and stone, so it can be replicated far more often than a
  full backup.

The honest limit: if the only surviving copy predates a deletion and no newer
ledger survives anywhere, nothing can know the deletion happened. The prototype
refuses until someone accepts that. It cannot prevent it.

## Compatibility and migration

Nothing here is implemented in .NET and nothing was changed. If adopted, these
are the persisted-format changes, each needing your explicit approval under
AGENTS.md:

1. **Story file version 2.** A frame holds a batch; each entry has a header
   (class, bit length, code hash) and an optional payload; a frame may carry a
   transaction marker and id. Version 1 files read as batches of one with every
   entry stone, and the chain can be computed on first open. The reserved header
   field in version 1 is zero today, so the version number is the switch.
2. **`StoryThing` on the wire is unchanged** if the class and hash stay at the
   storage layer. Exposing class to readers would be an additive field.
3. **New library files:** `commits.log`, blobs, and the three system stories.
4. **`IScribe`** gains an append with expected position and retry id.
   `RecordSomething(params Code[])` keeps its signature and becomes atomic,
   which is a strengthening. This is the surface #22 was parked on; the Library
   may be a better home for it than `IScribe`, as Quinn suggested.
5. **Tapes:** no change. Existing tape series stay restorable. A one-time import
   from a tape series into a Library is the migration path.
6. **Hashing every code** costs CPU the current path does not spend. Not
   measured in .NET.

The prototype's own encodings (JSON records, SHA-256 over a length prefix) are
stand-ins. The real design should use pack rats and `Multihash`.

## Remaining uncertainties

- **No .NET evidence.** This session had no .NET SDK and no package access, so
  nothing was built against the real code. Every claim about `FileStoryLog`
  comes from reading it and modelling it.
- **The crash model is simplified:** rename, truncate and delete are treated as
  atomic and durable; directory fsync and reordered writes are not modelled.
- **Retry ids across anchor compaction** are carried on the compaction record,
  following PR #28, which found the problem. The list grows with the range
  compacted; neither lab has measured that.
- **Not built:** commit-log checkpointing, multi-process writers, per-edition
  pins (any pin defers all reclamation), ledger compaction,
  encryption, and retry-id expiry.
- **Catalog retirement** (`StoryEnded`) is not exercised.
- **SQL house behind or ahead of the Library** (#23's hardest case) is out of
  scope here; SQLite appears only as a timing baseline.

## Comparison with PR #28

PR #28 is a separate lab by CJ, written in .NET against the real packages and
reviewed twice by Codex. I found it after finishing my first pass and have not
changed its branch. The two labs were built without sight of each other, so
agreement between them is worth something.

**Agreement.** A Library is the backup and tapes are at most a carrier. A
tape-style copy tears across stories and carries no blobs. Stone is never
erased in v1. Removal is never inferred from absence. Verification returns
separate verdicts. A pinned backup gets its exact edition; an unpinned one that
loses the race publishes nothing. Compaction summaries belong to the
application. Sand can be promoted. No SQL is needed for backup. A retry key
rides on the append.

**Where PR #28 is stronger.** It runs on .NET with the shipped `FileScribe`
bytes adopted unchanged and compared against Data 0.11.0-rc.1. It kills real
processes and tests a real cross-process lock, which shows that expected
position alone does not decide a race between processes. It has had an
independent review that found at least eight real defects. Mine has had none.

**Where this lab adds evidence.** Simulated power loss at every durable step in
three modes, which process kills cannot show. Timings. The header chain. The
ledger escrow. Write-once media.

**Disagreements.**

| Topic | PR #28 | This lab | What would settle it |
| --- | --- | --- | --- |
| Batch frame | Not for v1. Publish through the edition pointer; keep today's frame | One frame per append | Whether v1 readers read story heads. Today they do. With a pointer, a head reader can hear entries that are later replaced (tested here), and making each movement visible costs a manifest rewrite that grows with the library (measured here). If every v1 reader is edition-pinned and movement is batched, PR #28's answer is cheaper and changes no format |
| Identity after compaction | A new story generation. Old edition hashes no longer match the live file; an incremental backup re-copies the whole story | Chain over headers. Identity never changes; the incremental after compacting 300 moves copied 3 ledger records | Whether stable edition identity across retention is a requirement. I think it is, because stone facts will cite editions |
| Multi-story commit | The pointer | A commit log, touched stories only | The same reader question, plus library size |
| Unattested restore | Refuse unless `AcceptResurrection` | I first restored and then refused writes. PR #28's rule is better: mine still served reads of resurrected sand. **I adopted theirs**, and added the ledger witness as the way to restore without accepting |
| Where removal evidence lives | Certificates inside a newer edition | A ledger story that can be escrowed on its own | Whether you want removals to survive loss of everything newer than the last full backup |

## Disagreements with Team Grok

None recorded: they have not reviewed either lab. The places I expect challenge,
and would want it:

1. Whether C belongs in v1 at all, given SQLite's numbers and #23's reasoning.
2. Whether refusing an unattested restore by default is right, or an
   availability hazard during a real disaster.
3. Whether per-entry class is worth its cost against one class per story.
4. Whether anchor compaction, which makes old editions unverifiable locally, is
   acceptable for anything a stone fact might later cite.
5. Whether a backup that shares all its code with the primary is really
   independent.

Decisions needed from you are in [DECISIONS.md](DECISIONS.md).
