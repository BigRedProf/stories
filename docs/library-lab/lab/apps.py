"""Two synthetic applications over the Library contracts.

Digihouse: purchases and ownership transfers (stone), room stories (mixed),
photos/models (sand or stone content claims), activity movement (sand, bit-packed).
Scoreboard: a second, unrelated application, to test that nothing in the
contracts is Digihouse-shaped. Its compaction keeps set results, not last position.
"""
from .core import CATALOG, CONTENT, SAND, STONE, Code, txn_id

OWNERSHIP = "digihouse/ownership"
MOVES = "digihouse/activity/A1/movement"


def room(name):
    return "digihouse/house/H1/room/" + name


def purchase(pid):
    return "digihouse/purchase/" + pid


def rec(kind, **kw):
    return Code.of_json(dict(k=kind, **kw))


def move_code(obj, x, y):
    """A movement is 34 bits: an 8-bit tag, a 6-bit object, 10-bit x, 10-bit y.
    Deliberately not byte-aligned, to prove bit-exact preservation."""
    return Code.from_bits(format(0x4D, "08b") + format(obj, "06b") + format(x, "010b") + format(y, "010b"))


def decode(code):
    if code.bits == 34 and code.data[0] == 0x4D:
        b = code.bitstring()
        return {"k": "Move", "obj": int(b[8:14], 2), "x": int(b[14:24], 2), "y": int(b[24:34], 2)}
    return code.json()


def blob(label, size=600):
    """Deterministic fake JPEG/model bytes."""
    return (label.encode() + b"\x00") * (size // (len(label) + 1) + 1)


def setup(lib):
    lib.begin_story(OWNERSHIP, "stone")
    lib.begin_story(room("kitchen"), "mixed", [(rec("RoomCreated", room="kitchen"), STONE)])
    lib.begin_story(room("den"), "mixed", [(rec("RoomCreated", room="den"), STONE)])
    lib.begin_story(MOVES, "sand")


def buy(lib, pid, good, buyer, price, where, media, content_cls=SAND):
    """A purchase is a saga recorded in its own story; the ownership fact, the
    room placement, the content reference and the content claim commit together."""
    lib.begin_story(purchase(pid), "stone", [(rec("PurchaseStarted", purchase=pid, good=good, buyer=buyer, price=price), STONE)])
    lib.append(purchase(pid), [(rec("PaymentCaptured", purchase=pid), STONE)], txn=txn_id("pay:" + pid))
    writes = {OWNERSHIP: [(rec("GoodPurchased", good=good, buyer=buyer, purchase=pid), STONE)],
              room(where): [(rec("GoodPlaced", good=good), STONE)],
              purchase(pid): [(rec("PurchaseCompleted", purchase=pid), STONE)], CONTENT: []}
    for label in media:
        name = lib.put_blob(blob(label))
        claim = "%s:%s" % (good, label)
        writes[room(where)].append((rec("MediaAttached", good=good, label=label, blob=name, claim=claim), STONE))
        writes[CONTENT].append((rec("ContentClaimed", claim=claim, blob=name, cls=content_cls, **{"for": good}), STONE))
    return lib.commit(writes, txn_id("complete:" + pid))


def transfer(lib, good, seller, buyer, n):
    """One authoritative fact in one story. No multi-story commit is needed."""
    return lib.append(OWNERSHIP, [(rec("GoodTransferred", good=good, seller=seller, buyer=buyer), STONE)],
                      txn=txn_id("transfer:%s:%d" % (good, n)))


def moves(lib, start, count):
    for i in range(start, start + count):
        lib.append(MOVES, [(move_code(i % 3, (i * 37) % 1024, (i * 91) % 1024), SAND)])


def phase1(lib):
    setup(lib)
    buy(lib, "P1", "lamp", "alice", 40, "kitchen", ["lamp.jpg", "lamp.glb"])
    buy(lib, "P2", "sofa", "bob", 900, "den", ["sofa.jpg", "seat.glb"])
    buy(lib, "P3", "chair", "bob", 120, "den", ["seat.glb"])                 # shares seat.glb with the sofa
    buy(lib, "P4", "deed", "alice", 1, "den", ["deed.pdf"], content_cls=STONE)  # stone content
    transfer(lib, "lamp", "alice", "carol", 1)
    lib.append(room("kitchen"), [(rec("Note", text="lamp looks great here"), SAND),
                                 (rec("Note", text="call the electrician"), SAND)])
    moves(lib, 0, 200)


def phase2(lib):
    buy(lib, "P5", "rug", "carol", 75, "kitchen", ["rug.jpg"])
    transfer(lib, "sofa", "bob", "alice", 1)
    lib.begin_story(room("garage"), "mixed", [(rec("RoomCreated", room="garage"), STONE)])
    lib.begin_story(room("attic"), "mixed")                                   # an empty story
    moves(lib, 200, 150)


def positions_summary(lib, upto):
    """Movement compaction policy 'latest-position': keep where each object is."""
    pos = {}
    c = lib.compaction(MOVES)
    start = 0
    if c:
        start, pos = c[0], dict(decode(c[1])["positions"])
    for e in lib.read(MOVES, start, upto):
        m = decode(e.code())
        pos[str(m["obj"])] = [m["x"], m["y"]]
    return rec("MoveCheckpoint", upto=upto, positions=pos)


def fold(lib, manifest=None):
    """Rebuild Digihouse state from a library (optionally pinned to an edition)."""
    def read(sid):
        return lib.read_at(manifest, sid) if manifest else lib.read(sid)
    sids = [m[0] for m in manifest] if manifest else lib.story_ids()
    state = {"owners": {}, "history": [], "rooms": {}, "purchases": {}, "positions": {}, "unknown": []}
    for e in read(OWNERSHIP):
        r = decode(e.code())                 # stone: a missing payload must raise
        state["owners"][r["good"]] = r["buyer"]
        state["history"].append([r["k"], r["good"], r.get("seller"), r["buyer"]])
    claims = lib.claims()
    for sid in sids:
        if sid.startswith("digihouse/house/"):
            rm = state["rooms"].setdefault(sid.rsplit("/", 1)[1], {"goods": [], "media": {}, "notes": []})
            for e in read(sid):
                if e.payload is None:
                    rm["notes"].append("[removed]")
                    continue
                r = decode(e.code())
                if r["k"] == "GoodPlaced":
                    rm["goods"].append(r["good"])
                elif r["k"] == "MediaAttached":
                    c = claims.get(r["claim"])
                    have = lib.get_blob(r["blob"]) is not None
                    status = "present" if have and not (c and c["released"]) else \
                        ("removed" if c and c["released"] else "MISSING")
                    rm["media"]["%s/%s" % (r["good"], r["label"])] = status
                    if status == "MISSING":
                        state["unknown"].append("%s/%s bytes missing without a release" % (r["good"], r["label"]))
                elif r["k"] == "Note":
                    rm["notes"].append(r["text"])
        elif sid.startswith("digihouse/purchase/"):
            kinds = [decode(e.code())["k"] for e in read(sid)]
            state["purchases"][sid.rsplit("/", 1)[1]] = kinds[-1] if kinds else "none"
    c = lib.compaction(MOVES)
    start = 0
    if c:
        start, state["positions"] = c[0], dict(decode(c[1])["positions"])
    for e in read(MOVES):
        if e.pos < start:
            continue
        if e.payload is None:
            state["unknown"].append("movement %d removed with no checkpoint covering it" % e.pos)
            continue
        m = decode(e.code())
        state["positions"][str(m["obj"])] = [m["x"], m["y"]]
    return state


# ---------------------------------------------------------------- second application

RALLIES = "scoreboard/match/M1/rallies"
RESULTS = "scoreboard/results"


def scoreboard_play(lib, rallies=120):
    lib.begin_story(RESULTS, "stone")
    lib.begin_story(RALLIES, "sand")
    for i in range(rallies):
        lib.append(RALLIES, [(rec("Rally", n=i, won="home" if (i * 7) % 5 < 3 else "away"), SAND)])


def scoreboard_sets(lib, upto=None, per_set=40):
    """Set scores so far. Honors a 'keep-set-results' compaction checkpoint."""
    sets, start = [], 0
    c = lib.compaction(RALLIES)
    if c:
        start, sets = c[0], [list(s) for s in c[1].json()["sets"]]
    for e in lib.read(RALLIES, start, upto):
        r = e.code().json()
        if r["n"] % per_set == 0:
            sets.append([0, 0])
        sets[-1][0 if r["won"] == "home" else 1] += 1
    return sets


def scoreboard_finish(lib):
    sets = scoreboard_sets(lib)
    lib.append(RESULTS, [(rec("MatchResult", match="M1", sets=sets), STONE)], txn=txn_id("result:M1"))
    return sets
