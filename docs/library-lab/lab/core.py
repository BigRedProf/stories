"""Library Lab core: Code, a fault-injecting file system, Story, Library.

A PROTOTYPE of semantics for stories #25/#26/#27. Python stdlib only. Nothing here
is the BigRedProf.Stories persisted format; see ../MEMO.md for what is proposed.

Layers, bottom up:
  Code     an exact bit sequence. Identity = SHA-256 over (bit length, bits).
  Story    an ordered append-only sequence of entry headers (position, class,
           bit length, code id), hash-chained, each with an optional payload.
           The chain covers headers only, so a sand payload can be removed
           without changing any position, chain value or edition identity.
  Library  catalog + stories + content blobs + a commit log. The commit log
           publishes multi-story commits and records editions, pins and
           preservation milestones.
"""
import hashlib
import json
import os
import struct
import threading
import zlib

STONE, SAND = 0, 1
CLASS_NAME = {STONE: "stone", SAND: "sand"}
CATALOG, RETENTION, CONTENT = "_cardCatalog", "_retention", "_content"
SYSTEM = (CATALOG, RETENTION, CONTENT)
NO_TXN = bytes(16)


class Crash(Exception):
    """The simulated machine lost power."""


class Conflict(Exception):
    """An expected-position or expected-edition predicate did not hold."""


class Corrupt(Exception):
    """Stored bytes are damaged somewhere other than a torn tail."""


class Damage(Exception):
    """A story no longer matches a head the commit log durably recorded."""


class StoneViolation(Exception):
    """An operation would remove or rewrite stone."""


class Refused(Exception):
    """The operation is refused rather than done unsafely."""


def H(*parts):
    h = hashlib.sha256()
    for p in parts:
        h.update(p)
    return h.digest()


def txn_id(label):
    return H(b"txn", label.encode())[:16]


# ---------------------------------------------------------------- Code

class Code:
    """An immutable, exact sequence of bits. Bits are MSB-first; padding is zero."""
    __slots__ = ("bits", "data", "id")

    def __init__(self, bits, data):
        data = bytes(data)
        if len(data) != (bits + 7) // 8:
            raise ValueError("data length does not match bit length")
        if bits % 8 and data[-1] & (0xFF >> (bits % 8)):
            raise ValueError("padding bits must be zero")
        self.bits, self.data = bits, data
        self.id = H(b"code", struct.pack(">Q", bits), data)

    @classmethod
    def from_bits(cls, s):
        padded = s + "0" * (-len(s) % 8)
        return cls(len(s), bytes(int(padded[i:i + 8], 2) for i in range(0, len(padded), 8)))

    @classmethod
    def of_json(cls, obj):
        b = json.dumps(obj, sort_keys=True, separators=(",", ":")).encode()
        return cls(len(b) * 8, b)

    def bitstring(self):
        return "".join(format(b, "08b") for b in self.data)[:self.bits]

    def json(self):
        return json.loads(self.data.decode())

    def __eq__(self, other):
        return isinstance(other, Code) and self.id == other.id

    def __hash__(self):
        return hash(self.id)


# ---------------------------------------------------------------- file systems

class SimFS:
    """In-memory files with explicit durability and a crash switch.

    Each file has durable bytes (as of its last fsync) and current bytes (what
    any reader sees). Every mutating call is one step; `crash_at = n` raises
    Crash instead of performing step n. `after_crash(mode)` returns what is on
    disk afterwards: 'lose' drops all unsynced bytes, 'keep' keeps them all,
    'torn' keeps half of each unsynced suffix (a torn write).
    Simplification: replace/truncate/remove are atomic and durable in one step.
    """

    def __init__(self, files=None):
        self.files = files or {}     # path -> [durable bytes or None, current bytes]
        self.steps = 0
        self.crash_at = None

    def _step(self, what):
        if self.crash_at is not None and self.steps >= self.crash_at:
            raise Crash("%s at step %d" % (what, self.steps))
        self.steps += 1

    def append(self, path, data):
        self._step("append " + path)
        f = self.files.setdefault(path, [None, b""])
        f[1] = f[1] + bytes(data)

    def fsync(self, path):
        self._step("fsync " + path)
        f = self.files[path]
        f[0] = f[1]

    def replace(self, path, data):
        self._step("replace " + path)
        self.files[path] = [bytes(data), bytes(data)]

    def truncate(self, path, n):
        self._step("truncate " + path)
        f = self.files[path]
        f[1] = f[1][:n]
        f[0] = f[1]

    def remove(self, path):
        self._step("remove " + path)
        self.files.pop(path, None)

    def read(self, path):
        f = self.files.get(path)
        return None if f is None else f[1]

    def exists(self, path):
        return path in self.files

    def after_crash(self, mode="lose"):
        out = {}
        for path, (durable, current) in self.files.items():
            base = durable if durable is not None else b""
            extra = current[len(base):] if current.startswith(base) else b""
            if mode == "keep":
                data = base + extra
            elif mode == "torn":
                data = base + extra[:len(extra) // 2]
            else:
                data = base
            if durable is not None or data:
                out[path] = [data, data]
        return SimFS(out)

    def clone(self):
        return SimFS({p: [f[0], f[1]] for p, f in self.files.items()})


class RealFS:
    """The same interface over a real directory, with real fsync. For timings."""

    def __init__(self, sync=True):
        self.sync = sync
        self.handles = {}

    def append(self, path, data):
        h = self.handles.get(path)
        if h is None:
            os.makedirs(os.path.dirname(path), exist_ok=True)
            h = self.handles[path] = open(path, "ab")
        h.write(data)

    def fsync(self, path):
        h = self.handles[path]
        h.flush()
        if self.sync:
            os.fsync(h.fileno())

    def replace(self, path, data):
        os.makedirs(os.path.dirname(path), exist_ok=True)
        self._close(path)
        tmp = path + ".tmp"
        with open(tmp, "wb") as f:
            f.write(data)
            f.flush()
            if self.sync:
                os.fsync(f.fileno())
        os.replace(tmp, path)

    def truncate(self, path, n):
        self._close(path)
        with open(path, "r+b") as f:
            f.truncate(n)

    def remove(self, path):
        self._close(path)
        if os.path.exists(path):
            os.remove(path)

    def read(self, path):
        h = self.handles.get(path)
        if h:
            h.flush()
        if not os.path.exists(path):
            return None
        with open(path, "rb") as f:
            return f.read()

    def exists(self, path):
        return os.path.exists(path)

    def _close(self, path):
        h = self.handles.pop(path, None)
        if h:
            h.close()

    def close(self):
        for p in list(self.handles):
            self._close(p)


# ---------------------------------------------------------------- frames

def frame(body):
    return struct.pack("<I", len(body)) + body + struct.pack("<I", zlib.crc32(body))


def read_frames(data, what):
    """Yield (start, end, body). Stops silently at a torn tail; raises Corrupt
    when a bad frame has bytes after it (that is damage, not a crash)."""
    at, n = 0, len(data)
    while at < n:
        if n - at < 8:
            return
        (length,) = struct.unpack_from("<I", data, at)
        end = at + 8 + length
        if end > n:
            return
        body = data[at + 4:at + 4 + length]
        (crc,) = struct.unpack_from("<I", data, at + 4 + length)
        if crc != zlib.crc32(body):
            if end == n:
                return
            raise Corrupt("%s: checksum mismatch at byte %d with data after it" % (what, at))
        yield at, end, body
        at = end


# ---------------------------------------------------------------- Story

class Entry:
    __slots__ = ("pos", "cls", "bits", "cid", "payload")

    def __init__(self, pos, cls, bits, cid, payload):
        self.pos, self.cls, self.bits, self.cid, self.payload = pos, cls, bits, cid, payload

    def code(self):
        return None if self.payload is None else Code(self.bits, self.payload)

    def header(self):
        return (self.pos, self.cls, self.bits, self.cid.hex())


def story_seed(sid):
    return H(b"story", sid.encode())


def link(prev, e):
    return H(prev, struct.pack(">QBQ", e.pos, e.cls, e.bits), e.cid)


BATCH, ANCHOR = 1, 2


class Story:
    def __init__(self, sid):
        self.sid = sid
        self.base = 0                    # first retained position (compaction anchor)
        self.base_chain = story_seed(sid)
        self.entries = []                # entries[i] is position base + i
        self.chains = []                 # chain value after entries[i]
        self.frames = []                 # [flag, txn, first, n], to re-serialize faithfully
        self.txns = {}                   # txn id -> (first, n), for idempotent retries

    def head(self):
        return self.base + len(self.entries)

    def chain_at(self, pos):
        """Chain value after `pos` entries (the identity of the prefix [0, pos))."""
        if pos == self.base:
            return self.base_chain
        if pos < self.base or pos > self.head():
            return None
        return self.chains[pos - self.base - 1]

    def add(self, flag, txn, entries):
        prev = self.chain_at(self.head())
        first = self.head()
        for e in entries:
            prev = link(prev, e)
            self.entries.append(e)
            self.chains.append(prev)
        self.frames.append([flag, txn, first, len(entries)])
        if txn != NO_TXN:
            self.txns[txn] = (first, len(entries))

    @staticmethod
    def batch_frame(flag, txn, first, entries):
        parts = [struct.pack("<BB16sQI", BATCH, flag, txn, first, len(entries))]
        for e in entries:
            parts.append(struct.pack("<BBQ32s", e.cls, 0 if e.payload is not None else 1, e.bits, e.cid))
            if e.payload is not None:
                parts.append(e.payload)
        return frame(b"".join(parts))

    def serialize(self):
        out = []
        if self.base:
            out.append(frame(struct.pack("<BQ32s", ANCHOR, self.base, self.base_chain)))
        for flag, txn, first, n in self.frames:
            lo, hi = max(first, self.base), first + n
            if hi > lo:
                out.append(self.batch_frame(flag, txn, lo, self.entries[lo - self.base:hi - self.base]))
        return b"".join(out)

    @classmethod
    def parse(cls, sid, data, committed):
        """Returns (story, valid_length). Reading stops at a torn tail and at the
        first library-transaction frame whose transaction is not in `committed`
        (pass committed=None to read naively, ignoring transaction markers)."""
        st = cls(sid)
        valid = 0
        for start, end, body in read_frames(data, sid):
            if body[0] == ANCHOR:
                _, st.base, st.base_chain = struct.unpack("<BQ32s", body)
            elif body[0] == BATCH:
                _, flag, txn, first, n = struct.unpack_from("<BB16sQI", body, 0)
                if flag and committed is not None and txn not in committed:
                    break
                if first != st.head():
                    raise Corrupt("%s: frame starts at %d, expected %d" % (sid, first, st.head()))
                at, entries = struct.calcsize("<BB16sQI"), []
                for i in range(n):
                    c, state, bits, cid = struct.unpack_from("<BBQ32s", body, at)
                    at += struct.calcsize("<BBQ32s")
                    payload = None
                    if state == 0:
                        size = (bits + 7) // 8
                        payload = body[at:at + size]
                        at += size
                    entries.append(Entry(first + i, c, bits, cid, payload))
                st.add(flag, txn, entries)
            else:
                raise Corrupt("%s: unknown frame type %d" % (sid, body[0]))
            valid = end
        return st, valid


# ---------------------------------------------------------------- Library

def edition_id(manifest):
    """An edition is a sorted list of [story id, next position, chain hex]. Its
    identity depends on nothing physical."""
    return H(b"edition", json.dumps(sorted(manifest), separators=(",", ":")).encode()).hex()


class Library:
    def __init__(self, fs, root):
        self.fs, self.root = fs, root.rstrip("/")
        self.lock = threading.RLock()
        self.stories = {}
        self.records = []            # the commit log, in order
        self.committed = {}          # txn id -> commit record
        self.witness = {}            # story id -> (position, chain hex) last durably recorded
        self.pins = set()
        self.policy = {}             # story id -> 'stone' | 'sand' | 'mixed'
        self.hook = None             # test hook: called with a step name inside commit()
        self.escrow = None           # optional callable(library) run after evidence, before reclaim
        self.info = {}

    # ---- lifecycle

    @classmethod
    def create(cls, fs, root, library_id):
        if fs.exists(root.rstrip("/") + "/LIBRARY"):
            raise Refused("a library already exists at " + root)
        fs.replace(root.rstrip("/") + "/LIBRARY",
                   json.dumps({"id": library_id, "format": "library-lab-1"}).encode())
        return cls.open(fs, root)

    @classmethod
    def open(cls, fs, root):
        """Open and recover: drop a torn commit-log tail, drop story frames whose
        library transaction never committed, and check every story still reaches
        the heads the commit log recorded."""
        self = cls(fs, root)
        raw = fs.read(self.root + "/LIBRARY")
        if raw is None:
            raise Refused("no library at " + root)
        self.info = json.loads(raw.decode())
        log_path = self.root + "/commits.log"
        data = fs.read(log_path) or b""
        valid = 0
        for start, end, body in read_frames(data, "commits.log"):
            self._apply_record(json.loads(body.decode()))
            valid = end
        if valid < len(data):
            fs.truncate(log_path, valid)
        for sid in SYSTEM:
            self._story(sid)
        for e in self.stories[CATALOG].entries:
            r = e.code().json()
            if r["k"] == "StoryBegan":
                self.policy[r["story"]] = r["policy"]
                self._story(r["story"])
        return self

    @property
    def library_id(self):
        return self.info["id"]

    def _apply_record(self, rec):
        self.records.append(rec)
        kind = rec["kind"]
        if kind == "commit":
            self.committed[bytes.fromhex(rec["txn"])] = rec
            for sid, (pos, chain) in rec["heads"].items():
                self.witness[sid] = (pos, chain)
        elif kind in ("edition", "preserved", "restored"):
            for sid, pos, chain in rec["manifest"]:
                self.witness[sid] = (pos, chain)
        if kind == "pin":
            self.pins.add(rec["edition"])
        elif kind == "unpin":
            self.pins.discard(rec["edition"])

    def _log(self, rec):
        path = self.root + "/commits.log"
        self.fs.append(path, frame(json.dumps(rec, sort_keys=True).encode()))
        self.fs.fsync(path)
        self._apply_record(rec)

    def _path(self, sid):
        return "%s/stories/%s.story" % (self.root, H(sid.encode()).hex()[:40])

    def _story(self, sid):
        st = self.stories.get(sid)
        if st is None:
            data = self.fs.read(self._path(sid)) or b""
            st, valid = Story.parse(sid, data, self.committed)
            if valid < len(data):
                self.fs.truncate(self._path(sid), valid)
            w = self.witness.get(sid)
            if w and w[0] >= st.base:
                got = st.chain_at(w[0])
                if got is None:
                    raise Damage("%s holds %d entries but the commit log recorded %d" % (sid, st.head(), w[0]))
                if got.hex() != w[1]:
                    raise Damage("%s differs from the history the commit log recorded at %d" % (sid, w[0]))
            self.stories[sid] = st
        return st

    # ---- writing

    def _entries(self, sid, st, items):
        policy = "stone" if sid in SYSTEM else self.policy.get(sid)
        out = []
        for i, (code, cls) in enumerate(items):
            if policy == "stone" and cls != STONE or policy == "sand" and cls != SAND:
                raise Refused("%s is a %s story; it cannot hold a %s entry" % (sid, policy, CLASS_NAME[cls]))
            out.append(Entry(st.head() + i, cls, code.bits, code.id, code.data))
        return out

    def append(self, sid, items, expected_pos=None, txn=NO_TXN):
        """Atomic single-story append of one or more (Code, class) items.

        One frame, one fsync. The fsync is the durability point and the entries
        become visible to readers only after it. A retry with the same txn is a
        no-op that returns the original positions.
        """
        with self.lock:
            st = self._story(sid)
            if txn != NO_TXN and txn in st.txns:
                return {sid: st.txns[txn]}
            if expected_pos is not None and st.head() != expected_pos:
                raise Conflict("%s is at %d, expected %d" % (sid, st.head(), expected_pos))
            entries = self._entries(sid, st, items)
            path = self._path(sid)
            self.fs.append(path, Story.batch_frame(0, txn, st.head(), entries))
            self.fs.fsync(path)
            first = st.head()
            st.add(0, txn, entries)
            return {sid: (first, len(entries))}

    def commit(self, writes, txn, expected=None, expected_edition=None):
        """Atomic multi-story commit: all of `writes` ({story: [(Code, class)]})
        or none of it.

        Stage one transaction-marked frame per story (fsync each), then append
        one record to the commit log and fsync it. That last fsync is both the
        commit point and the durability point. A reader never returns a marked
        frame whose transaction is not in the commit log, and recovery truncates
        such frames. A retry with the same txn returns the original receipt.
        """
        with self.lock:
            if txn in self.committed:
                return self.committed[txn]["first"]
            for sid, pos in (expected or {}).items():
                if self._story(sid).head() != pos:
                    raise Conflict("%s is at %d, expected %d" % (sid, self._story(sid).head(), pos))
            if expected_edition is not None and edition_id(self.manifest()) != expected_edition:
                raise Conflict("the library has moved past edition " + expected_edition[:12])
            for sid, items in writes.items():           # learn policies declared in this commit
                if sid == CATALOG:
                    for code, _ in items:
                        r = code.json()
                        if r["k"] == "StoryBegan":
                            if r["story"] in self.policy:
                                raise Conflict("story %s already exists" % r["story"])
            staged, new_policy = [], {}
            for code, _ in writes.get(CATALOG, []):
                r = code.json()
                if r["k"] == "StoryBegan":
                    new_policy[r["story"]] = r["policy"]
            self.policy.update(new_policy)
            try:
                for sid in sorted(writes):
                    st = self._story(sid)
                    entries = self._entries(sid, st, writes[sid])
                    path = self._path(sid)
                    size = len(self.fs.read(path) or b"")
                    staged.append((st, entries, path, size))
                    self.fs.append(path, Story.batch_frame(1, txn, st.head(), entries))
                    self.fs.fsync(path)
                    if self.hook:
                        self.hook("staged " + sid)
                rec = {"kind": "commit", "txn": txn.hex(), "heads": {}, "first": {}}
                for st, entries, _, _ in staged:
                    chain = st.chain_at(st.head())
                    for e in entries:
                        chain = link(chain, e)
                    rec["heads"][st.sid] = [st.head() + len(entries), chain.hex()]
                    rec["first"][st.sid] = [st.head(), len(entries)]
                self._log(rec)                           # <- commit point
            except Crash:
                raise
            except Exception:
                for st, entries, path, size in staged:  # ordinary failure: undo staging
                    if self.fs.exists(path):
                        self.fs.truncate(path, size)
                for sid in new_policy:
                    self.policy.pop(sid, None)
                raise
            for st, entries, _, _ in staged:             # publish to readers, under the lock
                st.add(1, txn, entries)
            return rec["first"]

    def begin_story(self, sid, policy, first_items=(), txn=None):
        """Create a story: its catalog entry and its first entries commit together."""
        writes = {CATALOG: [(Code.of_json({"k": "StoryBegan", "story": sid, "policy": policy}), STONE)]}
        if first_items:
            writes[sid] = list(first_items)
        return self.commit(writes, txn or txn_id("begin:" + sid))

    def accept(self, sid, first, entries):
        """The replication accept rule. The same entry at a taken position is a
        no-op; a different entry there is an error; a gap is an error."""
        with self.lock:
            st = self._story(sid)
            fresh = []
            for i, e in enumerate(entries):
                pos = first + i
                if pos < st.base:
                    continue
                if pos < st.head():
                    mine = st.entries[pos - st.base]
                    if (mine.cls, mine.bits, mine.cid) != (e.cls, e.bits, e.cid):
                        raise Conflict("%s position %d already holds a different entry" % (sid, pos))
                elif pos == st.head() + len(fresh):
                    fresh.append(Entry(pos, e.cls, e.bits, e.cid, e.payload))
                else:
                    raise Conflict("%s is at %d; cannot accept position %d (gap)" % (sid, st.head(), pos))
            if fresh:
                path = self._path(sid)
                self.fs.append(path, Story.batch_frame(0, NO_TXN, st.head(), fresh))
                self.fs.fsync(path)
                st.add(0, NO_TXN, fresh)
                if sid == CATALOG:
                    for e in fresh:
                        r = e.code().json()
                        if r["k"] == "StoryBegan":
                            self.policy[r["story"]] = r["policy"]

    def adopt_anchor(self, sid, base, chain):
        """Start (or cut) a story at a compaction anchor. Only legal when this
        library's own retention ledger holds matching evidence."""
        with self.lock:
            ok = any(r["k"] == "Compacted" and r["story"] == sid and r["mode"] == "anchor"
                     and r["upto"] == base and r["chain"] == chain.hex() for r in self.evidence())
            if not ok:
                raise Refused("%s: no compaction evidence authorizes starting at %d" % (sid, base))
            st = self._story(sid)
            if st.head() > base and st.chain_at(base) != chain:
                raise Conflict("%s: anchor chain does not match local history" % sid)
            keep = [e for e in st.entries if e.pos >= base]
            st.frames = [f for f in st.frames if f[2] + f[3] > base]
            st.entries, st.chains, st.base, st.base_chain = [], [], base, chain
            prev = chain
            for e in keep:
                prev = link(prev, e)
                st.entries.append(e)
                st.chains.append(prev)
            self.fs.replace(self._path(sid), st.serialize())

    # ---- reading

    def story_ids(self):
        return list(SYSTEM) + sorted(self.policy)

    def head(self, sid):
        with self.lock:
            st = self._story(sid)
            return st.head(), st.chain_at(st.head())

    def chain_at(self, sid, pos):
        with self.lock:
            return self._story(sid).chain_at(pos)

    def base(self, sid):
        with self.lock:
            st = self._story(sid)
            return st.base, st.base_chain

    def read(self, sid, start=0, upto=None):
        """Entries in [start, upto). An ordinary storyteller read: it sees every
        committed entry and never part of a commit. Two reads of two stories are
        NOT a snapshot; use read_at for that."""
        with self.lock:
            st = self._story(sid)
            upto = st.head() if upto is None else min(upto, st.head())
            lo = max(start, st.base)
            return [Entry(e.pos, e.cls, e.bits, e.cid, e.payload) for e in st.entries[lo - st.base:max(lo, upto) - st.base]]

    def manifest(self):
        with self.lock:
            return [[sid, self._story(sid).head(), self._story(sid).chain_at(self._story(sid).head()).hex()]
                    for sid in sorted(self.story_ids())]

    def capture_edition(self, note=""):
        """Record the current heads as an edition. Taken under the library lock,
        so it can never include part of a commit."""
        with self.lock:
            m = self.manifest()
            rec = {"kind": "edition", "id": edition_id(m), "manifest": m, "note": note}
            self._log(rec)
            return rec

    def read_at(self, manifest, sid):
        """Edition-pinned read: the story exactly as the edition saw it."""
        for s, pos, chain in manifest:
            if s == sid:
                with self.lock:
                    got = self._story(sid).chain_at(pos)
                    if got is not None and got.hex() != chain:
                        raise Damage("%s no longer matches the edition at %d" % (sid, pos))
                return self.read(sid, 0, pos)
        return []

    # ---- content

    def put_blob(self, data):
        name = hashlib.sha256(data).hexdigest()
        path = "%s/blobs/%s" % (self.root, name)
        if not self.fs.exists(path):
            self.fs.replace(path, data)
        return name

    def get_blob(self, name):
        return self.fs.read("%s/blobs/%s" % (self.root, name))

    def claims(self, upto=None):
        """claim id -> dict(blob, cls, released). Claims come from the _content
        story; releases come from the retention ledger at its newest head."""
        out = {}
        for e in self.read(CONTENT, 0, upto):
            r = e.code().json()
            if r["k"] == "ContentClaimed":
                out[r["claim"]] = {"blob": r["blob"], "cls": r["cls"], "released": False, "for": r.get("for")}
        for r in self.evidence():
            if r["k"] == "ContentReleased" and r["claim"] in out:
                out[r["claim"]]["released"] = True
        return out

    # ---- retention: evidence first, physical removal second

    def evidence(self):
        return [e.code().json() for e in self.read(RETENTION)]

    def remove_payload(self, sid, pos, reason, authority):
        with self.lock:
            e = self.read(sid, pos, pos + 1)[0]
            if e.cls == STONE or (sid, pos) in self.promoted():
                raise StoneViolation("%s position %d is stone" % (sid, pos))
            ev = {"k": "PayloadRemoved", "story": sid, "pos": pos, "cid": e.cid.hex(),
                  "reason": reason, "authority": authority}
            self.append(RETENTION, [(Code.of_json(ev), STONE)])
            return self._after_evidence()

    def promote(self, sid, pos, reason, authority):
        """Sand to stone. The entry's class is part of its hashed header and never
        changes; promotion is a stone ledger record that forbids later removal.
        There is deliberately no demotion."""
        ev = {"k": "Promoted", "story": sid, "pos": pos, "reason": reason, "authority": authority}
        self.append(RETENTION, [(Code.of_json(ev), STONE)])

    def promoted(self):
        return {(r["story"], r["pos"]) for r in self.evidence() if r["k"] == "Promoted"}

    def compact(self, sid, upto, summary, mode, policy, authority):
        """Retire the sand in [base, upto) of a story, keeping `summary` in its place.

        mode 'tombstone' keeps every entry header (positions and chain intact,
        stone entries untouched). mode 'anchor' drops the entries themselves and
        keeps only (upto, chain at upto); it is refused if the range holds stone.
        """
        with self.lock:
            st = self._story(sid)
            if not st.base < upto <= st.head():
                raise Refused("%s: cannot compact up to %d" % (sid, upto))
            if mode == "anchor" and any(e.cls == STONE or (sid, e.pos) in self.promoted()
                                        for e in st.entries[:upto - st.base]):
                raise StoneViolation("%s: range holds stone; anchor compaction would drop it" % sid)
            ev = {"k": "Compacted", "story": sid, "upto": upto, "mode": mode,
                  "chain": st.chain_at(upto).hex(), "summary": [summary.bits, summary.data.hex()],
                  "policy": policy, "authority": authority}
            self.append(RETENTION, [(Code.of_json(ev), STONE)])
            return self._after_evidence()

    def release_claim(self, claim, reason, authority):
        with self.lock:
            c = self.claims()[claim]
            if c["cls"] == STONE:
                raise StoneViolation("claim %s is stone" % claim)
            ev = {"k": "ContentReleased", "claim": claim, "reason": reason, "authority": authority}
            self.append(RETENTION, [(Code.of_json(ev), STONE)])
            return self._after_evidence()

    def _after_evidence(self):
        if self.escrow:
            self.escrow(self)
        return self.reclaim()

    def compaction(self, sid):
        """(upto, summary Code) for the newest compaction of a story, or None."""
        best = None
        for r in self.evidence():
            if r["k"] == "Compacted" and r["story"] == sid:
                best = (r["upto"], Code(r["summary"][0], bytes.fromhex(r["summary"][1])))
        return best

    def authorized(self):
        """What the retention ledger authorizes: removed (story, pos) pairs and
        per-story compaction bounds."""
        removed, compacted = set(), {}
        for r in self.evidence():
            if r["k"] == "PayloadRemoved":
                removed.add((r["story"], r["pos"]))
            elif r["k"] == "Compacted":
                compacted[r["story"]] = max(compacted.get(r["story"], 0), r["upto"])
        return removed, compacted

    def reclaim(self):
        """Physically apply the retention ledger. Idempotent. Does nothing while
        any edition is pinned, and nothing on media that refuses rewriting; both
        leave the data 'retired, pending reclamation', which verify() reports.
        Returns {'deferred': bool, 'payloads': n, 'blobs': n}."""
        with self.lock:
            if self.pins:
                return {"deferred": True, "payloads": 0, "blobs": 0}
            try:
                return self._reclaim()
            except Refused as ex:
                self.stories.clear()             # forget half-applied in-memory changes
                return {"deferred": True, "payloads": 0, "blobs": 0, "blocked": str(ex)}

    def _reclaim(self):
        if True:
            dirty, payloads, blobs = set(), 0, 0
            for r in self.evidence():
                if r["k"] in ("ContentReleased", "Promoted") or r["story"] not in self.policy:
                    continue
                st = self._story(r["story"])
                if r["k"] == "PayloadRemoved":
                    i = r["pos"] - st.base
                    if 0 <= i < len(st.entries) and st.entries[i].payload is not None:
                        if st.entries[i].cid.hex() != r["cid"]:
                            raise Damage("removal evidence does not match %s position %d" % (st.sid, r["pos"]))
                        st.entries[i].payload = None
                        payloads += 1
                        dirty.add(st.sid)
                elif r["k"] == "Compacted" and st.head() >= r["upto"] > st.base:
                    if st.chain_at(r["upto"]).hex() != r["chain"]:
                        raise Damage("compaction evidence does not match %s at %d" % (st.sid, r["upto"]))
                    if r["mode"] == "anchor":
                        payloads += sum(1 for e in st.entries[:r["upto"] - st.base] if e.payload is not None)
                        self.adopt_anchor(st.sid, r["upto"], bytes.fromhex(r["chain"]))
                    else:
                        keep = self.promoted()
                        for e in st.entries[:r["upto"] - st.base]:
                            if e.cls == SAND and e.payload is not None and (st.sid, e.pos) not in keep:
                                e.payload = None
                                payloads += 1
                                dirty.add(st.sid)
            for sid in sorted(dirty):
                self.fs.replace(self._path(sid), self.stories[sid].serialize())
            live = {c["blob"] for c in self.claims().values() if not c["released"]}
            for c in self.claims().values():
                if c["released"] and c["blob"] not in live and self.get_blob(c["blob"]) is not None:
                    self.fs.remove("%s/blobs/%s" % (self.root, c["blob"]))
                    blobs += 1
            return {"deferred": False, "payloads": payloads, "blobs": blobs}

    def is_empty(self):
        return all(pos == 0 for _, pos, _ in self.manifest())

    def pin(self, edition):
        self._log({"kind": "pin", "edition": edition})

    def unpin(self, edition):
        self._log({"kind": "unpin", "edition": edition})
        return self.reclaim()

    # ---- verification

    def verify(self, manifest=None, fold=None):
        """Four separate verdicts: integrity, completeness, retention, application."""
        with self.lock:
            manifest = manifest or self.manifest()
            removed, compacted = self.authorized()
            promoted = self.promoted()
            integrity, missing, stone_missing, unauthorized, below = [], [], [], [], []
            counts = {"entries": 0, "present": 0, "removed_authorized": 0}
            pending = 0
            for sid, pos, chain in manifest:
                st = self._story(sid)
                if st.head() < pos:
                    missing.append("%s: holds %d of %d entries" % (sid, st.head(), pos))
                    continue
                if pos < st.base:
                    below.append("%s: edition head %d is below the compaction anchor %d" % (sid, pos, st.base))
                    continue
                prev = st.base_chain
                for e in st.entries[:pos - st.base]:
                    prev = link(prev, e)
                    counts["entries"] += 1
                    ok_removed = ((sid, e.pos) in removed or (e.cls == SAND and e.pos < compacted.get(sid, 0))) \
                        and (sid, e.pos) not in promoted
                    if e.payload is None:
                        if e.cls == STONE or (sid, e.pos) in promoted:
                            stone_missing.append("%s position %d: stone payload is gone" % (sid, e.pos))
                        elif ok_removed:
                            counts["removed_authorized"] += 1
                        else:
                            unauthorized.append("%s position %d: sand payload gone with no removal evidence" % (sid, e.pos))
                    else:
                        counts["present"] += 1
                        if ok_removed:
                            pending += 1
                        try:
                            good = Code(e.bits, e.payload).id == e.cid
                        except ValueError:
                            good = False
                        if not good:
                            integrity.append("%s position %d: payload does not match its code id" % (sid, e.pos))
                if prev.hex() != chain:
                    integrity.append("%s: chain at %d does not match the edition" % (sid, pos))
                if st.base and compacted.get(sid, 0) < st.base:
                    unauthorized.append("%s: starts at %d with no compaction evidence" % (sid, st.base))
            content_upto = dict((s, p) for s, p, _ in manifest).get(CONTENT)
            blobs_missing, blobs_ok, blobs_released = [], 0, 0
            claims = self.claims(content_upto)
            live = {}
            for c in claims.values():
                if not c["released"]:
                    live[c["blob"]] = min(live.get(c["blob"], SAND), c["cls"])
            for c in claims.values():
                if c["released"] and c["blob"] not in live:
                    blobs_released += 1
                    pending += self.get_blob(c["blob"]) is not None
            for blob, cls in sorted(live.items()):
                data = self.get_blob(blob)
                if data is None:
                    (stone_missing if cls == STONE else blobs_missing).append(
                        "blob %s: bytes are gone but a %s claim is still live" % (blob[:12], CLASS_NAME[cls]))
                elif hashlib.sha256(data).hexdigest() != blob:
                    integrity.append("blob %s: bytes do not match their hash" % blob[:12])
                else:
                    blobs_ok += 1
            report = {
                "edition": edition_id(manifest),
                "integrity": {"ok": not integrity, "problems": integrity},
                "completeness": {"ok": not (missing or unauthorized or blobs_missing or stone_missing),
                                 "missing": missing + unauthorized + blobs_missing + stone_missing,
                                 "unverifiable_below_anchor": below, "blobs_present": blobs_ok,
                                 "blobs_released": blobs_released, **counts},
                "retention": {"ok": not (stone_missing or unauthorized),
                              "stone_lost": stone_missing, "unauthorized_removals": unauthorized,
                              "retired_pending_reclaim": pending},
            }
            if fold is not None:
                try:
                    state = fold(self, manifest)
                    digest = H(json.dumps(state, sort_keys=True).encode()).hex()
                    report["application"] = {"ok": True, "state_digest": digest,
                                             "unknown": state.get("unknown", [])}
                except Exception as ex:   # the application could not rebuild its state
                    report["application"] = {"ok": False, "error": "%s: %s" % (type(ex).__name__, ex)}
            report["ok"] = all(v["ok"] for v in report.values() if isinstance(v, dict))
            return report

    def fingerprint(self, manifest=None):
        """Every retained header and payload bit, for exact comparison between libraries."""
        out = {}
        for sid, pos, chain in (manifest or self.manifest()):
            b, _ = self.base(sid)
            out[sid] = {"base": b, "head": pos, "chain": chain,
                        "entries": [[e.pos, e.cls, e.bits, e.cid.hex(),
                                     None if e.payload is None else Code(e.bits, e.payload).bitstring()]
                                    for e in self.read(sid, 0, pos)]}
        live = {c["blob"] for c in self.claims().values() if not c["released"]}
        out["#blobs"] = {b: hashlib.sha256(self.get_blob(b) or b"").hexdigest() for b in sorted(live)
                         if self.get_blob(b) is not None}
        return out
