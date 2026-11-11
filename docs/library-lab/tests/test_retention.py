"""Story #27: stone and sand across deletion, compaction, backup and restore."""
import unittest

from lab import apps
from lab.core import (CONTENT, RETENTION, SAND, STONE, Library, Refused, SimFS, StoneViolation,
                      edition_id, txn_id)
from lab.harness import crash_matrix
from lab.preserve import last_preserved, preserve, push_ledger, restore

LAMP_JPG = "lamp:lamp.jpg"


def world(phases=(apps.phase1, apps.phase2)):
    fs = SimFS()
    src = Library.create(fs, "/src", "lib-src")
    for p in phases:
        p(src)
    return fs, src


def retire(src, mode="tombstone"):
    """The deliberate removals of the scenario: one photo, one note, old movement."""
    src.release_claim(LAMP_JPG, "owner asked for the photo to be deleted", "alice")
    src.remove_payload(apps.room("kitchen"), 4, "note retracted", "alice")
    src.compact(apps.MOVES, 300, apps.positions_summary(src, 300), mode, "latest-position", "activity A1 policy")


class Lifecycle(unittest.TestCase):
    def test_stone_history_survives_content_deletion_and_movement_compaction(self):
        fs, src = world()
        before, edition = apps.fold(src), src.capture_edition()
        blob = src.claims()[LAMP_JPG]["blob"]
        retire(src)
        after = apps.fold(src)
        self.assertEqual(after["owners"], before["owners"])
        self.assertEqual(after["history"], before["history"])          # every purchase and transfer, in order
        self.assertEqual(after["positions"], before["positions"])      # movement state survives compaction
        self.assertEqual(after["rooms"]["kitchen"]["media"]["lamp/lamp.jpg"], "removed")   # the reference outlives the bytes
        self.assertEqual(after["rooms"]["kitchen"]["notes"], ["[removed]", "call the electrician"])
        self.assertIsNone(src.get_blob(blob))
        self.assertEqual(after["unknown"], [])
        # Identity: only the retention ledger grew. Every other story's position and chain is unchanged,
        # and the edition captured before the removals still verifies.
        changed = [a[0] for a, b in zip(edition["manifest"], src.manifest()) if a != b]
        self.assertEqual(changed, [RETENTION])
        old = src.verify(edition["manifest"], apps.fold)
        self.assertTrue(old["ok"], old)
        self.assertEqual(old["completeness"]["removed_authorized"], 301)   # 300 moves and one note

    def test_shared_blob_is_kept_until_its_last_claim_is_released_and_stone_claims_never_are(self):
        fs, src = world()
        seat = src.claims()["sofa:seat.glb"]["blob"]
        self.assertEqual(seat, src.claims()["chair:seat.glb"]["blob"])
        src.release_claim("chair:seat.glb", "chair model retired", "bob")
        self.assertIsNotNone(src.get_blob(seat))                       # the sofa still needs it
        self.assertEqual(apps.fold(src)["rooms"]["den"]["media"]["sofa/seat.glb"], "present")
        src.release_claim("sofa:seat.glb", "sofa model retired", "bob")
        self.assertIsNone(src.get_blob(seat))
        with self.assertRaises(StoneViolation):
            src.release_claim("deed:deed.pdf", "tidy up", "bob")

    def test_stone_cannot_be_removed_or_compacted_away(self):
        fs, src = world()
        with self.assertRaises(StoneViolation):
            src.remove_payload(apps.OWNERSHIP, 0, "oops", "bob")
        with self.assertRaises(StoneViolation):
            src.compact(apps.room("kitchen"), 4, apps.rec("X"), "anchor", "p", "bob")
        src.compact(apps.room("kitchen"), 6, apps.rec("RoomNotesCleared"), "tombstone", "clear notes", "alice")
        kinds = [(e.cls, e.payload is not None) for e in src.read(apps.room("kitchen"))]
        self.assertEqual(kinds[:6], [(STONE, True)] * 4 + [(SAND, False)] * 2)   # stone kept, sand retired
        self.assertTrue(src.verify(fold=apps.fold)["ok"])

    def test_sand_promoted_to_stone_can_no_longer_be_removed_or_compacted(self):
        fs, src = world()
        src.promote(apps.room("kitchen"), 5, "this note is evidence in a dispute", "russell")
        with self.assertRaises(StoneViolation):
            src.remove_payload(apps.room("kitchen"), 5, "tidy", "alice")
        src.compact(apps.room("kitchen"), 6, apps.rec("RoomNotesCleared"), "tombstone", "clear notes", "alice")
        note4, note5 = src.read(apps.room("kitchen"), 4, 6)
        self.assertEqual((note4.payload is None, note5.payload is None), (True, False))
        bak = Library.create(fs, "/bak", "lib-bak")
        self.assertEqual(preserve(src, bak, fold=apps.fold)["status"], "complete")
        self.assertIsNotNone(bak.read(apps.room("kitchen"), 5, 6)[0].payload)   # the promise travels with the ledger
        with self.assertRaises(StoneViolation):
            bak.remove_payload(apps.room("kitchen"), 5, "tidy", "alice")

    def test_verification_tells_authorized_removal_from_loss(self):
        fs, src = world()
        retire(src)
        self.assertTrue(src.verify()["ok"])

        def break_and_verify(mutate):
            lib = Library.open(fs.clone(), "/src")
            mutate(lib)
            return Library.open(lib.fs, "/src").verify(fold=apps.fold)

        def drop_payload(sid, pos):
            def m(lib):
                st = lib._story(sid)
                st.entries[pos - st.base].payload = None
                lib.fs.replace(lib._path(sid), st.serialize())
            return m
        sand = break_and_verify(drop_payload(apps.MOVES, 320))           # accidental sand loss
        self.assertEqual((sand["retention"]["ok"], sand["integrity"]["ok"]), (False, True))
        self.assertIn("no removal evidence", sand["retention"]["unauthorized_removals"][0])
        stone = break_and_verify(drop_payload(apps.OWNERSHIP, 1))        # stone loss
        self.assertIn("stone payload is gone", stone["retention"]["stone_lost"][0])
        self.assertFalse(stone["application"]["ok"])                     # and ownership cannot be rebuilt
        gone = break_and_verify(lambda lib: lib.fs.remove("/src/blobs/" + lib.claims()["rug:rug.jpg"]["blob"]))
        self.assertFalse(gone["completeness"]["ok"])
        self.assertEqual(gone["application"]["unknown"], ["rug/rug.jpg bytes missing without a release"])

    def test_evidence_is_durable_before_any_byte_is_reclaimed(self):
        base = SimFS()
        lib = Library.create(base, "/src", "lib-src")
        apps.phase1(lib)
        blob = lib.claims()[LAMP_JPG]["blob"]

        def setup(fs):
            fs.files.update({p: list(f) for p, f in base.files.items()})
            return Library.open(fs, "/src")

        def check(fs, acked, step, mode):
            lib = Library.open(fs, "/src")
            released = lib.claims()[LAMP_JPG]["released"]
            if lib.get_blob(blob) is None:
                self.assertTrue(released, "bytes were reclaimed with no durable evidence")
            lib.reclaim()                                               # recovery finishes the job
            self.assertEqual(lib.get_blob(blob) is None, released)
            self.assertTrue(lib.verify()["ok"])
        crash_matrix(setup, lambda lib: lib.release_claim(LAMP_JPG, "asked", "alice"), check)


class Compaction(unittest.TestCase):
    def test_tombstone_and_anchor_modes_keep_identity_and_state(self):
        sizes = {}
        for mode in ("tombstone", "anchor"):
            fs, src = world()
            before, state = src.manifest(), apps.fold(src)
            edition = src.capture_edition()
            size0 = len(fs.read(src._path(apps.MOVES)))
            src.compact(apps.MOVES, 300, apps.positions_summary(src, 300), mode, "latest-position", "policy")
            sizes[mode] = (size0, len(fs.read(src._path(apps.MOVES))))
            now = {s: (p, c) for s, p, c in src.manifest()}
            self.assertEqual(now[apps.MOVES], {s: (p, c) for s, p, c in before}[apps.MOVES])   # same head, same chain
            self.assertEqual(apps.fold(src)["positions"], state["positions"])
            self.assertEqual([e.pos for e in src.read(apps.MOVES)][-1], 349)                    # positions never renumber
            reopened = Library.open(fs, "/src")
            self.assertTrue(reopened.verify(fold=apps.fold)["ok"])
            apps.moves(reopened, 350, 5)                                                        # and appends continue
            self.assertEqual(reopened.head(apps.MOVES)[0], 355)
        self.assertLess(sizes["anchor"][1], sizes["tombstone"][1])
        self.assertLess(sizes["tombstone"][1], sizes["tombstone"][0])
        Compaction.sizes = sizes

    def test_an_edition_below_an_anchor_is_reported_as_unverifiable_not_as_verified(self):
        fs, src = world((apps.phase1,))
        early = src.capture_edition()                                    # movement head 200
        apps.phase2(src)
        src.compact(apps.MOVES, 300, apps.positions_summary(src, 300), "anchor", "latest-position", "policy")
        report = src.verify(early["manifest"])
        self.assertEqual(len(report["completeness"]["unverifiable_below_anchor"]), 1)
        self.assertTrue(any(r["kind"] == "edition" and r["id"] == early["id"] for r in src.records))  # its identity is on record


class BackupRacingRetention(unittest.TestCase):
    def race(self, pin, mode="tombstone", every_attempt=False, retries=2):
        fs, src = world()
        bak = Library.create(fs, "/bak", "lib-bak")
        fired = []

        def retention_runs_mid_copy(step):
            if step.startswith("copied " + apps.MOVES) and (every_attempt or not fired):
                if not fired:
                    retire(src, mode)
                else:
                    src.remove_payload(apps.MOVES, 349 - len(fired), "churn", "policy")
                fired.append(step)
        return fs, src, bak, preserve(src, bak, pin=pin, on_step=retention_runs_mid_copy, fold=apps.fold, retries=retries)

    def test_pinned_backup_gets_its_exact_edition_and_reclamation_waits(self):
        fs, src, bak, r = self.race(pin=True)
        self.assertEqual((r["status"], r["attempts"]), ("complete", 1))
        kept = [e for e in bak.read(apps.MOVES, 0, 300) if e.payload is not None]
        self.assertEqual(len(kept), 300)                                 # the backup holds the pinned edition in full
        self.assertEqual(len([e for e in src.read(apps.MOVES, 0, 300) if e.payload is not None]), 0)  # source reclaimed after unpin
        r2 = preserve(src, bak, fold=apps.fold)                          # next run carries the evidence across
        self.assertEqual(r2["status"], "complete")
        self.assertEqual(len([e for e in bak.read(apps.MOVES, 0, 300) if e.payload is not None]), 0)
        self.assertEqual(bak.fingerprint(), src.fingerprint())

    def test_unpinned_backup_notices_and_retries_at_a_newer_edition(self):
        for mode in ("tombstone", "anchor"):
            fs, src, bak, r = self.race(pin=False, mode=mode)
            self.assertEqual((r["status"], r["attempts"]), ("complete", 2), r["problems"])
            self.assertEqual(bak.fingerprint(), src.fingerprint())
            self.assertEqual(apps.fold(bak), apps.fold(src))

    def test_unpinned_backup_under_constant_churn_fails_explicitly(self):
        fs, src, bak, r = self.race(pin=False, every_attempt=True, retries=1)
        self.assertEqual(r["status"], "incomplete")
        self.assertIsNone(last_preserved(bak))                           # no completion record was written
        self.assertTrue(any("removed after this edition" in p for p in r["problems"]))

    def test_a_backup_may_hold_sand_longer_by_policy_and_reports_it(self):
        fs, src = world()
        bak = Library.create(fs, "/bak", "lib-bak")
        preserve(src, bak)
        retire(src)
        r = preserve(src, bak, hold_sand=True)
        self.assertEqual(r["status"], "complete")
        self.assertEqual(r["verify"]["retention"]["retired_pending_reclaim"], 302)   # 300 moves, a note, a photo


class StaleBackup(unittest.TestCase):
    def stale(self, escrow=False, ledger_to_backup=False):
        fs, src = world()
        bak = Library.create(fs, "/bak", "lib-bak")
        esc = Library.create(fs, "/escrow", "lib-escrow")
        if escrow:
            src.escrow = lambda lib: push_ledger(lib, esc)               # evidence leaves the building before bytes go
        preserve(src, bak)                                               # the backup predates the deletion
        retire(src)
        if ledger_to_backup:
            push_ledger(src, bak)
        want = apps.fold(src)
        for path in [p for p in fs.files if p.startswith("/src/")]:
            del fs.files[path]                                           # source lost
        return fs, Library.open(fs, "/bak"), Library.open(fs, "/escrow"), want

    def test_without_newer_evidence_the_restore_reports_resurrection_and_refuses_writes(self):
        fs, bak, esc, want = self.stale()
        new, report = restore(bak, fs, "/new", "lib-new", fold=apps.fold)
        self.assertEqual(report["status"], "complete")
        self.assertFalse(report["resurrection"]["attested"])
        state = apps.fold(new)
        self.assertEqual(state["rooms"]["kitchen"]["media"]["lamp/lamp.jpg"], "present")     # the deleted photo is back
        self.assertEqual(state["owners"], want["owners"])                                    # stone is exact either way
        self.assertEqual(report["resurrection"]["sand_payloads_present"], 352)
        with self.assertRaises(Refused):
            apps.transfer(new, "rug", "carol", "dan", 1)
        new.acknowledge_restore("russell")
        apps.transfer(new, "rug", "carol", "dan", 1)
        self.assertIsNone(Library.open(fs, "/new").frozen)                                   # the acknowledgment is durable

    def test_with_an_escrowed_ledger_the_deletion_is_honored(self):
        fs, bak, esc, want = self.stale(escrow=True)
        new, report = restore(bak, fs, "/new", "lib-new", witness=esc, fold=apps.fold)
        self.assertTrue(report["resurrection"]["attested"])
        self.assertEqual(report["removal_evidence"]["newer_evidence_applied"], 3)
        self.assertEqual(apps.fold(new), want)                           # exactly the state at the moment of loss
        self.assertIsNone(new.frozen)

    def test_a_ledger_only_push_to_the_backup_is_enough(self):
        fs, bak, esc, want = self.stale(ledger_to_backup=True)
        new, report = restore(bak, fs, "/new", "lib-new", fold=apps.fold)
        self.assertEqual(apps.fold(new)["rooms"]["kitchen"]["media"]["lamp/lamp.jpg"], "removed")
        self.assertEqual(apps.fold(new), want)
        self.assertFalse(report["resurrection"]["attested"])             # still unattested: no independent witness


if __name__ == "__main__":
    unittest.main()
