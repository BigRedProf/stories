"""Library-to-library preservation: backup, verification, restore.

A backup is an ordinary Library. Preserving copies entries by (story, position)
through the accept rule, copies claimed blobs by hash, verifies the destination
against the edition, and only then appends a `preserved` record to the
destination's commit log. That record's fsync is the durability point of the
backup; an attempt without it is visibly incomplete.
"""
import hashlib

from .core import (CATALOG, CONTENT, RETENTION, SAND, STONE, SYSTEM, Conflict, Library,
                   Refused, edition_id)


def _copy(src, dst, manifest, on_step=None, chunk=64):
    """Bring dst up to `manifest` from src. Returns (problems, stats); a problem
    is (kind, text) with kind 'moved' (the source changed under us; retry at a
    newer edition) or 'damage' (the source cannot supply what the edition names)."""
    problems, stats = [], {"entries": 0, "blobs": 0, "stories": 0}
    heads = {sid: (pos, chain) for sid, pos, chain in manifest}
    order = [s for s in (CATALOG, RETENTION, CONTENT) if s in heads] + sorted(s for s in heads if s not in SYSTEM)
    for sid in order:
        pos, chain = heads[sid]
        sbase, sbase_chain = src.base(sid)
        dhead, _ = dst.head(sid)
        if sbase > dhead:
            try:
                dst.adopt_anchor(sid, sbase, sbase_chain)
            except Refused:
                later = any(r["k"] == "Compacted" and r["story"] == sid and r["upto"] == sbase for r in src.evidence())
                problems.append(("moved" if later else "damage",
                                 "%s: source now starts at %d and this edition's ledger does not authorize that" % (sid, sbase)))
                continue
            dhead = sbase
        removed, compacted = dst.authorized()
        src_removed, src_compacted = src.authorized()
        entries = src.read(sid, dhead, pos)
        if len(entries) != max(0, pos - dhead):
            problems.append(("damage", "%s: source holds fewer entries than the edition names" % sid))
            continue
        stop = False
        for i in range(0, len(entries), chunk):
            part = entries[i:i + chunk]
            for e in part:
                if e.payload is not None:
                    continue
                here = (sid, e.pos) in removed or (e.cls == SAND and e.pos < compacted.get(sid, 0))
                there = (sid, e.pos) in src_removed or (e.cls == SAND and e.pos < src_compacted.get(sid, 0))
                if here:
                    continue
                if there:
                    problems.append(("moved", "%s position %d was removed after this edition was captured" % (sid, e.pos)))
                    stop = True
                    break
                problems.append(("damage", "%s position %d: %s payload missing at source with no removal evidence"
                                 % (sid, e.pos, "STONE" if e.cls == STONE else "sand")))
            if stop:
                break
            dst.accept(sid, part[0].pos, part)
            stats["entries"] += len(part)
            if on_step:
                on_step("copied %s %d" % (sid, part[-1].pos))
        if not stop:
            got = dst._story(sid).chain_at(pos)
            if got is None or got.hex() != chain:
                problems.append(("damage", "%s: copied history does not reproduce the edition chain" % sid))
        stats["stories"] += 1
    for claim, c in sorted(dst.claims(heads.get(CONTENT, (0,))[0]).items()):
        if c["released"] or dst.get_blob(c["blob"]) is not None:
            continue
        data = src.get_blob(c["blob"])
        if data is None:
            later = src.claims().get(claim, {}).get("released")
            problems.append(("moved" if later else "damage", "blob %s for claim %s is not at the source" % (c["blob"][:12], claim)))
        elif hashlib.sha256(data).hexdigest() != c["blob"]:
            problems.append(("damage", "blob %s at the source does not match its hash" % c["blob"][:12]))
        else:
            dst.put_blob(data)
            stats["blobs"] += 1
            if on_step:
                on_step("copied blob " + c["blob"][:12])
    return problems, stats


def preserve(src, dst, pin=True, on_step=None, retries=2, fold=None, hold_sand=False):
    """Preserve src's current edition into dst. Incremental by construction:
    only what dst lacks is copied. Safe to re-run after any interruption."""
    attempts = 0
    while True:
        attempts += 1
        ed = src.capture_edition("preserve")
        if pin:
            src.pin(ed["id"])
        dst._log({"kind": "preserve-begin", "edition": ed["id"], "source": src.library_id})
        try:
            problems, stats = _copy(src, dst, ed["manifest"], on_step)
        finally:
            if pin:
                src.unpin(ed["id"])
        if not any(k == "moved" for k, _ in problems) or attempts > retries:
            break
    if not hold_sand:
        dst.reclaim()
    report = dst.verify(ed["manifest"], fold)
    complete = report["ok"] and not problems
    if complete:
        dst._log({"kind": "preserved", "edition": ed["id"], "manifest": ed["manifest"], "source": src.library_id})
    return {"status": "complete" if complete else "incomplete", "edition": ed["id"], "attempts": attempts,
            "problems": [t for _, t in problems], "stats": stats, "verify": report}


def preservations(lib):
    """Every attempt recorded in a backup library, complete or not."""
    done = {r["edition"] for r in lib.records if r["kind"] == "preserved"}
    return [{"edition": r["edition"], "complete": r["edition"] in done}
            for r in lib.records if r["kind"] == "preserve-begin"]


def last_preserved(lib):
    for rec in reversed(lib.records):
        if rec["kind"] == "preserved":
            return rec
    return None


def push_ledger(src, dst):
    """Replicate only the retention ledger (removal evidence). It is small and
    stone, so it can go to an escrow far more often than a full backup runs."""
    head, _ = dst.head(RETENTION)
    entries = src.read(RETENTION, head)
    if entries:
        dst.accept(RETENTION, head, entries)


def restore(backup, fs, root, library_id, witness=None, fold=None):
    """Rebuild a library at `root` from the newest COMPLETE preservation in
    `backup`. The original environment is not consulted.

    `witness` is any surviving copy of the retention ledger (an escrow, another
    backup). With it, removals newer than the backup are applied and the restore
    is attested. Without it the restore still succeeds, but it reports that sand
    removed after the backup may have been resurrected, and the restored library
    refuses writes until that is acknowledged.
    """
    rec = last_preserved(backup)
    if rec is None:
        raise Refused("the backup holds no complete preservation")
    if fs.exists(root.rstrip("/") + "/LIBRARY"):
        target = Library.open(fs, root)
        resuming = target.records and target.records[-1]["kind"] == "restore-begin" \
            and target.records[-1]["edition"] == rec["edition"]
        if not (target.is_empty() and not target.records) and not resuming:
            raise Refused("restore target is occupied")
    else:
        target = Library.create(fs, root, library_id)
    target._log({"kind": "restore-begin", "edition": rec["edition"], "from": backup.library_id})
    # The ledger travels at its newest known head: evidence first, data second.
    for source in (backup, witness):
        if source is not None:
            push_ledger(source, target)
    problems, stats = _copy(backup, target, rec["manifest"])
    ledger_at_backup = rec_head = dict((s, p) for s, p, _ in rec["manifest"])[RETENTION]
    ledger_now, _ = target.head(RETENTION)
    reclaimed = target.reclaim()
    report = target.verify(target.manifest(), fold)
    sand_payloads = sum(1 for sid in target.story_ids() for e in target.read(sid) if e.cls == SAND and e.payload is not None)
    sand_blobs = len({c["blob"] for c in target.claims().values() if not c["released"] and c["cls"] == SAND})
    attested = witness is not None
    ok = report["ok"] and not problems
    if ok:
        target._log({"kind": "restored", "edition": rec["edition"], "manifest": target.manifest(),
                     "from": backup.library_id, "attested": attested})
    return target, {
        "status": "complete" if ok else "incomplete", "edition": rec["edition"],
        "problems": [t for _, t in problems], "stats": stats, "verify": report,
        "removal_evidence": {"at_backup": ledger_at_backup, "applied_through": ledger_now,
                             "newer_evidence_applied": ledger_now - rec_head, "reclaimed": reclaimed},
        "resurrection": {"attested": attested, "sand_payloads_present": sand_payloads, "sand_blobs_present": sand_blobs,
                         "statement": "attested: removals through ledger position %d applied" % ledger_now if attested else
                         "NOT attested: no ledger witness was supplied, so sand removed after this backup "
                         "may be present again; writes are refused until acknowledged"},
    }
