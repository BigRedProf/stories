"""Timings on a real file system with real fsync. Run: python3 bench.py [dir]

Numbers depend entirely on the disk under `dir`; read them as ratios between
designs on the same disk, never as absolute throughput.
"""
import os
import shutil
import sys
import tempfile
import time

from lab import apps
from lab.alt import JournalLibrary, LegacyStory, SqlHouse
from lab.core import CONTENT, SAND, STONE, Library, RealFS, txn_id


def timed(n, fn):
    t = time.perf_counter()
    for i in range(n):
        fn(i)
    dt = time.perf_counter() - t
    return n / dt, dt / n * 1e6


def purchase(i):
    return {apps.OWNERSHIP: [(apps.rec("GoodPurchased", good="g%d" % i, buyer="b", purchase="P%d" % i), STONE)],
            apps.room("den"): [(apps.rec("GoodPlaced", good="g%d" % i), STONE),
                               (apps.rec("MediaAttached", good="g%d" % i, label="j", blob="0" * 64, claim="c%d" % i), STONE)],
            CONTENT: [(apps.rec("ContentClaimed", claim="c%d" % i, blob="0" * 64, cls=SAND), STONE)]}


def raw(writes):
    return {sid: [c.data for c, _ in items] for sid, items in writes.items()}


def main(root):
    rows = []
    fs = RealFS()
    t = time.perf_counter()
    for i in range(200):
        fs.append(root + "/probe", b"x" * 64)
        fs.fsync(root + "/probe")
    rows.append(("fsync probe (64 bytes + fsync)", 200 / (time.perf_counter() - t), (time.perf_counter() - t) / 200 * 1e6, ""))

    N, M = 300, 2000
    # --- a Digihouse purchase: 4 entries across 3 stories
    legacy = {s: LegacyStory(fs, "%s/legacy/%s" % (root, s.replace("/", "_"))) for s in purchase(0)}
    rows.append(("purchase  A  today: per-thing append",) + timed(N, lambda i: [legacy[s].append(p) for s, p in raw(purchase(i)).items()]) + ("4 fsyncs, NOT atomic",))
    lib = Library.create(fs, root + "/lib", "bench")
    apps.setup(lib)
    rows.append(("purchase  C  library commit",) + timed(N, lambda i: lib.commit(purchase(i), txn_id("p%d" % i))) + ("4 fsyncs, atomic + isolated",))
    j = JournalLibrary(fs, root + "/journal")
    rows.append(("purchase  D  journal then apply",) + timed(N, lambda i: j.commit(raw(purchase(i)))) + ("4 fsyncs, atomic durability only",))
    sql = SqlHouse(root + "/house.db")
    rows.append(("purchase  S  SQLite transaction",) + timed(N, lambda i: sql.commit(raw(purchase(i)))) + ("provider ACID baseline",))

    # --- movement traffic: one 34-bit code per append
    mv = LegacyStory(fs, root + "/legacy/moves")
    rows.append(("movement  A  today: per-thing append",) + timed(M, lambda i: mv.append([apps.move_code(i % 3, i % 1024, i % 1024).data])) + ("1 fsync",))
    rows.append(("movement  B  story append",) + timed(M, lambda i: lib.append(apps.MOVES, [(apps.move_code(i % 3, i % 1024, i % 1024), SAND)])) + ("1 fsync",))
    rows.append(("movement  C  forced through commit log",) + timed(M, lambda i: lib.commit({apps.MOVES: [(apps.move_code(i % 3, i % 1024, i % 1024), SAND)]}, txn_id("m%d" % i))) + ("2 fsyncs",))
    rows.append(("movement  S  SQLite transaction",) + timed(M, lambda i: sql.commit({"moves": [apps.move_code(i % 3, i % 1024, i % 1024).data]})) + ("",))

    # --- a burst of 50 moves as one unit
    burst = [apps.move_code(k % 3, k, k) for k in range(50)]
    rows.append(("burst-50  A  today: per-thing append",) + timed(40, lambda i: mv.append([c.data for c in burst])) + ("50 fsyncs",))
    rows.append(("burst-50  B  one atomic batch",) + timed(40, lambda i: lib.append(apps.MOVES, [(c, SAND) for c in burst])) + ("1 fsync",))

    t = time.perf_counter()
    reopened = Library.open(RealFS(), root + "/lib")
    rows.append(("reopen + recover library (%d commits, %d moves)" % (N + M, reopened.head(apps.MOVES)[0]), 1 / (time.perf_counter() - t), (time.perf_counter() - t) * 1e6, "full scan"))
    fs.close()
    print("%-44s %12s %12s  %s" % ("workload / design", "ops per sec", "usec per op", "notes"))
    for name, rate, usec, note in rows:
        print("%-44s %12.0f %12.0f  %s" % (name, rate, usec, note))


if __name__ == "__main__":
    base = sys.argv[1] if len(sys.argv) > 1 else tempfile.mkdtemp(prefix="library-lab-")
    try:
        main(base)
    finally:
        shutil.rmtree(base, ignore_errors=True)
