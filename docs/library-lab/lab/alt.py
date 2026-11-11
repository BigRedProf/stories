"""Alternatives that the Library design is compared against.

LegacyStory     the behaviour of today's FileStoryLog: one frame and one fsync
                per thing, no batch boundary, no expected position, no retry id.
PointerLibrary  the shape recommended by the parallel lab in PR #28: keep today's
                per-thing frames, then publish a full manifest by swinging a
                pointer. Readers of the pointer see all or nothing; readers of
                the story head see whatever has been flushed.
JournalLibrary  a write-ahead journal: one journal record holds a whole
                multi-story commit; stories are updated afterwards.
SqlHouse        the '#23 baseline': a provider with real transactions (SQLite).
ObjectFS        write-once object storage (tape- or S3-like) behind the same
                file interface the Library uses.
"""
import json
import sqlite3
import struct

from .core import Crash, Refused, frame, read_frames


class LegacyStory:
    def __init__(self, fs, path):
        self.fs, self.path = fs, path

    def append(self, payloads):
        for p in payloads:                      # one frame, one fsync, per thing
            self.fs.append(self.path, frame(p))
            self.fs.fsync(self.path)

    def read(self):
        return [body for _, _, body in read_frames(self.fs.read(self.path) or b"", self.path)]


class PointerLibrary:
    def __init__(self, fs, root, other_stories=0):
        self.fs, self.root, self.hook = fs, root, None
        raw = fs.read(root + "/pointer")
        # story -> [count, byte length] as of the published edition
        self.heads = json.loads(raw.decode()) if raw else {"filler-%d" % i: [0, 0] for i in range(other_stories)}

    def _story(self, sid):
        return "%s/%s.story" % (self.root, sid.replace("/", "_"))

    def read_head(self, sid):
        """What today's FileStoryteller or a live listener sees: every flushed frame."""
        return [body for _, _, body in read_frames(self.fs.read(self._story(sid)) or b"", sid)]

    def read(self, sid):
        """An edition reader: only what the pointer has published."""
        return self.read_head(sid)[:self.heads.get(sid, [0, 0])[0]]

    def commit(self, writes):
        new = dict(self.heads)
        for sid in sorted(writes):
            count, size = self.heads.get(sid, [0, 0])
            path = self._story(sid)
            if len(self.fs.read(path) or b"") > size:     # an unpublished prefix from a dead writer:
                self.fs.truncate(path, size)              # the next writer replaces it
            for p in writes[sid]:
                f = frame(p)
                self.fs.append(path, f)
                self.fs.fsync(path)
                size += len(f)
            new[sid] = [count + len(writes[sid]), size]
            if self.hook:
                self.hook("flushed " + sid)
        self.fs.replace(self.root + "/pointer", json.dumps(new, sort_keys=True).encode())   # <- publish
        self.heads = new


class JournalLibrary:
    def __init__(self, fs, root):
        self.fs, self.root = fs, root
        self.hook = None
        self.heads = {}
        self.recover()

    def _story(self, sid):
        return "%s/%s.story" % (self.root, sid.replace("/", "_"))

    def read(self, sid):
        """An ordinary reader looks at the story file. It knows nothing of the journal."""
        return [body for _, _, body in read_frames(self.fs.read(self._story(sid)) or b"", sid)]

    def recover(self):
        for _, _, body in read_frames(self.fs.read(self.root + "/journal") or b"", "journal"):
            self._apply(json.loads(body.decode()), recovering=True)

    def _apply(self, rec, recovering=False):
        for sid in sorted(rec["writes"]):
            first, items = rec["writes"][sid]
            have = first
            if recovering:
                data = self.fs.read(self._story(sid)) or b""
                valid = max([end for _, end, _ in read_frames(data, sid)] or [0])
                if valid < len(data):              # trim a torn tail before appending after it
                    self.fs.truncate(self._story(sid), valid)
                have = len(self.read(sid))
            for i, hexed in enumerate(items):
                if first + i >= have:
                    self.fs.append(self._story(sid), frame(bytes.fromhex(hexed)))
            if self.fs.exists(self._story(sid)):
                self.fs.fsync(self._story(sid))
            self.heads[sid] = first + len(items)
            if self.hook:
                self.hook("applied " + sid)

    def commit(self, writes):
        rec = {"writes": {sid: [self.heads.get(sid, 0), [p.hex() for p in items]] for sid, items in writes.items()}}
        self.fs.append(self.root + "/journal", frame(json.dumps(rec, sort_keys=True).encode()))
        self.fs.fsync(self.root + "/journal")     # <- commit point (durable)
        self._apply(rec)                          # visible story by story


class SqlHouse:
    def __init__(self, path, sync="FULL"):
        self.db = sqlite3.connect(path, isolation_level=None, check_same_thread=False)
        self.db.execute("PRAGMA journal_mode=WAL")
        self.db.execute("PRAGMA synchronous=" + sync)
        self.db.execute("CREATE TABLE IF NOT EXISTS entry(story TEXT, pos INTEGER, body BLOB, PRIMARY KEY(story, pos))")
        self.db.execute("CREATE TABLE IF NOT EXISTS head(story TEXT PRIMARY KEY, next INTEGER)")

    def commit(self, writes, expected=None):
        self.db.execute("BEGIN IMMEDIATE")
        try:
            for sid, items in writes.items():
                row = self.db.execute("SELECT next FROM head WHERE story=?", (sid,)).fetchone()
                nxt = row[0] if row else 0
                if expected and sid in expected and expected[sid] != nxt:
                    raise Refused("conflict on " + sid)
                self.db.executemany("INSERT INTO entry VALUES(?,?,?)", [(sid, nxt + i, p) for i, p in enumerate(items)])
                self.db.execute("INSERT OR REPLACE INTO head VALUES(?,?)", (sid, nxt + len(items)))
            self.db.execute("COMMIT")
        except Exception:
            self.db.execute("ROLLBACK")
            raise

    def read(self, sid):
        return [r[0] for r in self.db.execute("SELECT body FROM entry WHERE story=? ORDER BY pos", (sid,))]


class ObjectFS:
    """Write-once objects. Every fsync or replace PUTs a brand-new key; nothing is
    ever overwritten. Deleting old objects is the only destructive operation, and
    `worm=True` forbids it (object lock, or a tape that cannot be rewritten)."""

    def __init__(self, worm=False):
        self.objects = {}      # key -> bytes, immutable once put
        self.index = {}        # path -> list of keys, in order
        self.pending = {}      # path -> unsynced bytes
        self.worm = worm
        self.puts = self.deletes = self.seq = 0

    def _put(self, path, data):
        self.seq += 1
        key = "%s#%08d" % (path, self.seq)
        assert key not in self.objects, "an object would be overwritten"
        self.objects[key] = bytes(data)
        self.puts += 1
        return key

    def _drop(self, keys):
        if keys and self.worm:
            raise Refused("write-once media: objects cannot be deleted")
        for k in keys:
            del self.objects[k]
            self.deletes += 1

    def append(self, path, data):
        self.pending[path] = self.pending.get(path, b"") + bytes(data)

    def fsync(self, path):
        data = self.pending.pop(path, b"")
        if data:
            self.index.setdefault(path, []).append(self._put(path, data))

    def replace(self, path, data):
        old = self.index.get(path, [])
        if old and self.worm:
            raise Refused("write-once media: objects cannot be replaced")
        self.index[path] = [self._put(path, data)]
        self.pending.pop(path, None)
        self._drop(old)

    def truncate(self, path, n):
        durable = b"".join(self.objects[k] for k in self.index.get(path, []))
        if n < len(durable):
            raise Refused("write-once media: cannot truncate durable data")
        self.pending[path] = (self.pending.get(path, b""))[:n - len(durable)]

    def remove(self, path):
        self._drop(self.index.get(path, []))
        self.index.pop(path, None)
        self.pending.pop(path, None)

    def read(self, path):
        if path not in self.index and path not in self.pending:
            return None
        return b"".join(self.objects[k] for k in self.index.get(path, [])) + self.pending.get(path, b"")

    def exists(self, path):
        return path in self.index or path in self.pending
