"""One narrated end-to-end run of the Digihouse scenario. Run: python3 demo.py

Everything is deterministic (fixed ids, no clock, no randomness), so the output
is reproducible byte for byte and is checked in as results/demo.txt.
"""
import json

from lab import apps
from lab.core import RETENTION, Library, Refused, SimFS
from lab.preserve import last_preserved, preservations, preserve, push_ledger, restore


def say(title):
    print("\n== " + title)


def show(label, value):
    print("   %-46s %s" % (label, value))


def verdicts(report):
    return "  ".join("%s=%s" % (k, "ok" if report[k]["ok"] else "FAIL")
                     for k in ("integrity", "completeness", "retention", "application") if k in report)


def main():
    fs = SimFS()
    src = Library.create(fs, "/primary", "digihouse-primary")
    bak = Library.create(fs, "/backup", "digihouse-backup")
    escrow = Library.create(fs, "/escrow", "ledger-escrow")
    src.escrow = lambda lib: push_ledger(lib, escrow)

    say("1. Digihouse activity, phase 1")
    apps.phase1(src)
    s = apps.fold(src)
    show("owners", json.dumps(s["owners"], sort_keys=True))
    show("ownership history entries (stone)", len(s["history"]))
    show("stories in catalog", len(src.policy))
    show("movement entries (34-bit codes)", src.head(apps.MOVES)[0])

    say("2. Full preservation into an independent Library")
    r = preserve(src, bak, fold=apps.fold)
    show("status / entries / blobs copied", "%s / %d / %d" % (r["status"], r["stats"]["entries"], r["stats"]["blobs"]))
    show("edition", r["edition"][:16])
    show("verification", verdicts(r["verify"]))

    say("3. More activity, then an incremental preservation")
    apps.phase2(src)
    r = preserve(src, bak, fold=apps.fold)
    show("status / entries / blobs copied", "%s / %d / %d" % (r["status"], r["stats"]["entries"], r["stats"]["blobs"]))
    show("edition", r["edition"][:16])
    edition_before_removals = src.capture_edition()

    say("4. Deliberate sand removal at the primary (evidence first, bytes second)")
    lamp = src.claims()["lamp:lamp.jpg"]["blob"]
    src.release_claim("lamp:lamp.jpg", "owner asked for the photo to be deleted", "alice")
    src.remove_payload(apps.room("kitchen"), 4, "note retracted", "alice")
    src.compact(apps.MOVES, 300, apps.positions_summary(src, 300), "tombstone", "latest-position", "activity A1 policy")
    s2 = apps.fold(src)
    show("owners unchanged", s2["owners"] == apps.fold(bak)["owners"])
    show("ownership history unchanged", s2["history"] == apps.fold(bak)["history"])
    show("lamp photo", s2["rooms"]["kitchen"]["media"]["lamp/lamp.jpg"] + " (bytes gone: %s)" % (src.get_blob(lamp) is None))
    show("kitchen notes", s2["rooms"]["kitchen"]["notes"])
    show("object positions after compaction", json.dumps(s2["positions"], sort_keys=True))
    changed = [a[0] for a, b in zip(edition_before_removals["manifest"], src.manifest()) if a != b]
    show("stories whose identity changed", changed)
    old = src.verify(edition_before_removals["manifest"], apps.fold)
    show("pre-removal edition still verifies", "%s (authorized removals: %d)" % (verdicts(old), old["completeness"]["removed_authorized"]))
    show("removal evidence already in escrow", "%d records" % escrow.head(RETENTION)[0])

    say("5. The primary is lost. The backup predates the removals.")
    want = apps.fold(src)
    want_bits, want_manifest = src.fingerprint(), src.manifest()
    for path in [p for p in fs.files if p.startswith("/primary/")]:
        del fs.files[path]
    bak = Library.open(fs, "/backup")
    show("backup attempts on record", ["%s:%s" % (a["edition"][:8], "complete" if a["complete"] else "INCOMPLETE") for a in preservations(bak)])
    show("backup verification", verdicts(bak.verify(last_preserved(bak)["manifest"], apps.fold)))

    say("5a. Restore WITHOUT newer removal evidence")
    try:
        restore(bak, fs, "/restore-a", "digihouse-a", fold=apps.fold)
    except Refused as ex:
        show("first attempt", "REFUSED: " + str(ex)[:58] + "...")
    a, ra = restore(bak, fs, "/restore-a", "digihouse-a", accept_resurrection="russell", fold=apps.fold)
    sa = apps.fold(a)
    show("status", ra["status"] + "; " + verdicts(ra["verify"]))
    show("stone state equals state at loss", sa["owners"] == want["owners"] and sa["history"] == want["history"])
    show("lamp photo", sa["rooms"]["kitchen"]["media"]["lamp/lamp.jpg"] + "   <- resurrected")
    show("resurrection statement", ra["resurrection"]["statement"])

    say("5b. Restore WITH the escrowed retention ledger as witness")
    b, rb = restore(bak, fs, "/restore-b", "digihouse-b", witness=Library.open(fs, "/escrow"), fold=apps.fold)
    sb = apps.fold(b)
    show("status", rb["status"] + "; " + verdicts(rb["verify"]))
    show("newer evidence applied", "%d records, reclaimed %s" % (rb["removal_evidence"]["newer_evidence_applied"], rb["removal_evidence"]["reclaimed"]))
    show("lamp photo", sb["rooms"]["kitchen"]["media"]["lamp/lamp.jpg"])
    show("application state equals state at loss", sb == want)
    show("every retained bit and identity equal", b.fingerprint() == want_bits)
    show("edition identity equal", b.manifest() == want_manifest)
    apps.transfer(b, "rug", "carol", "dan", 1)
    show("writes accepted after attested restore", apps.fold(b)["owners"]["rug"])


if __name__ == "__main__":
    main()
