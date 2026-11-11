"""Story #25: a Library preserved into, verified in, and restored from another Library."""
import unittest

from lab import apps
from lab.alt import ObjectFS
from lab.core import CATALOG, Conflict, Corrupt, Damage, Library, Refused, SimFS, STONE, txn_id
from lab.harness import crash_matrix
from lab.preserve import last_preserved, preservations, preserve, restore


def source(fs, phases=(apps.phase1,)):
    lib = Library.create(fs, "/src", "lib-src")
    for p in phases:
        p(lib)
    return lib


def lose_source(fs):
    for path in [p for p in fs.files if p.startswith("/src/")]:
        del fs.files[path]


class FullIncrementalRestore(unittest.TestCase):
    def test_full_then_incremental_then_restore_with_the_source_gone(self):
        fs = SimFS()
        src = source(fs)
        bak = Library.create(fs, "/bak", "lib-bak")
        full = preserve(src, bak, fold=apps.fold)
        self.assertEqual((full["status"], full["stats"]["entries"]), ("complete", 245))
        apps.phase2(src)
        inc = preserve(src, bak, fold=apps.fold)
        self.assertEqual((inc["status"], inc["stats"]["entries"], inc["stats"]["blobs"]), ("complete", 162, 1))
        again = preserve(src, bak, fold=apps.fold)                 # nothing new: nothing copied
        self.assertEqual((again["status"], again["stats"]["entries"], again["stats"]["blobs"]), ("complete", 0, 0))
        want_bits, want_state, want_manifest = src.fingerprint(), apps.fold(src), src.manifest()
        lose_source(fs)                                            # the original environment is gone
        bak = Library.open(fs, "/bak")
        new, report = restore(bak, fs, "/new", "lib-new", accept_resurrection="operator", fold=apps.fold)
        self.assertEqual(report["status"], "complete")
        self.assertEqual(new.fingerprint(), want_bits)             # every retained bit, position, class, id
        self.assertEqual(new.manifest(), want_manifest)            # identical edition identity
        self.assertEqual(apps.fold(new), want_state)               # identical application state
        self.assertTrue(all(report["verify"][k]["ok"] for k in ("integrity", "completeness", "retention", "application")))
        moves = new.read(apps.MOVES)
        self.assertEqual(len(moves), 350)
        self.assertTrue(all(e.bits == 34 for e in moves))          # codes that end mid-byte survive exactly
        self.assertIn(apps.room("attic"), new.policy)              # an empty story is still a story
        self.assertEqual(new.head(apps.room("attic"))[0], 0)

    def test_concurrent_source_writes_do_not_leak_into_the_preserved_edition(self):
        fs = SimFS()
        src = source(fs)
        bak = Library.create(fs, "/bak", "lib-bak")
        fired = []

        def writer(step):
            if not fired:
                fired.append(step)
                apps.transfer(src, "sofa", "bob", "erin", 7)
                apps.moves(src, 200, 10)
        r = preserve(src, bak, on_step=writer)
        self.assertEqual(r["status"], "complete")
        heads = {s: p for s, p, _ in last_preserved(bak)["manifest"]}
        self.assertEqual((heads[apps.OWNERSHIP], heads[apps.MOVES]), (5, 200))       # the captured edition, exactly
        self.assertEqual((src.head(apps.OWNERSHIP)[0], src.head(apps.MOVES)[0]), (6, 210))
        r2 = preserve(src, bak)
        self.assertEqual((r2["status"], r2["stats"]["entries"]), ("complete", 11))

    def test_interrupted_backup_is_visibly_incomplete_and_resumes_exactly(self):
        base = SimFS()
        source(base)
        Library.create(base, "/bak", "lib-bak")
        resumed = []

        def setup(fs):
            fs.files.update({p: list(f) for p, f in base.files.items()})
            return Library.open(fs, "/src"), Library.open(fs, "/bak")

        def check(fs, acked, step, mode):
            src, bak = Library.open(fs, "/src"), Library.open(fs, "/bak")
            attempts = preservations(bak)
            if not acked:
                # Either the crash came after the completion record was durable (a lost
                # acknowledgment), or the attempt is recorded as incomplete. Never a silent in-between.
                self.assertTrue(last_preserved(bak) is not None or not any(a["complete"] for a in attempts))
            src.unpin_all = [src.unpin(e) for e in list(src.pins)]       # an operator clears the dead run's pin
            r = preserve(src, bak, fold=apps.fold)
            self.assertEqual(r["status"], "complete", r["problems"])
            self.assertEqual(bak.fingerprint(r["verify"] and last_preserved(bak)["manifest"]),
                             src.fingerprint(last_preserved(bak)["manifest"]))
            resumed.append(r["stats"]["entries"])
        cases = crash_matrix(setup, lambda ctx: preserve(ctx[0], ctx[1]), check, stride=3, modes=("lose", "torn"))
        self.assertGreater(cases, 20)
        self.assertIn(0, resumed)            # lost-ack case: the retry copied nothing
        self.assertTrue(any(n > 0 for n in resumed))

    def test_restore_refuses_an_occupied_target_and_a_backup_with_no_completed_preservation(self):
        fs = SimFS()
        src = source(fs)
        bak = Library.create(fs, "/bak", "lib-bak")
        with self.assertRaises(Refused):
            restore(bak, fs, "/new", "lib-new", accept_resurrection="operator")                    # nothing completed yet
        preserve(src, bak)
        with self.assertRaises(Refused):
            restore(bak, fs, "/src", "lib-x", accept_resurrection="operator")                      # the live source is occupied
        restore(bak, fs, "/new", "lib-new", accept_resurrection="operator")
        with self.assertRaises(Refused):
            restore(bak, fs, "/new", "lib-new", accept_resurrection="operator")                    # so is a finished restore

    def test_interrupted_restore_resumes(self):
        fs = SimFS()
        bak = Library.create(fs, "/bak", "lib-bak")
        preserve(source(fs), bak)
        fs.crash_at = fs.steps + 14
        with self.assertRaises(Exception):
            restore(bak, fs, "/new", "lib-new", accept_resurrection="operator")
        after = fs.after_crash("torn")
        new, report = restore(Library.open(after, "/bak"), after, "/new", "lib-new", accept_resurrection="operator", fold=apps.fold)
        self.assertEqual(report["status"], "complete")
        self.assertEqual(new.fingerprint(), Library.open(after, "/src").fingerprint())


class DamageIsNeverCompleteness(unittest.TestCase):
    def setUp(self):
        self.fs = SimFS()
        self.src = source(self.fs)
        self.bak = Library.create(self.fs, "/bak", "lib-bak")
        preserve(self.src, self.bak)

    def flip(self, path, at):
        data = bytearray(self.fs.read(path))
        data[at] ^= 0x40
        self.fs.replace(path, bytes(data))

    def test_flipped_payload_bit_fails_verification_and_restore(self):
        self.flip(self.bak._path(apps.OWNERSHIP), 80)
        with self.assertRaises((Corrupt, Damage)):
            restore(Library.open(self.fs, "/bak"), self.fs, "/new", "lib-new", accept_resurrection="operator")

    def test_truncation_on_a_clean_frame_boundary_is_not_a_clean_end(self):
        path = self.bak._path(apps.MOVES)
        self.fs.replace(path, self.fs.read(path)[:len(self.fs.read(path)) // 2])
        with self.assertRaises(Damage):
            Library.open(self.fs, "/bak")

    def test_missing_or_altered_blob_fails_verification(self):
        blob = sorted(c["blob"] for c in self.bak.claims().values())[0]
        self.flip("/bak/blobs/" + blob, 3)
        bak = Library.open(self.fs, "/bak")
        self.assertFalse(bak.verify()["integrity"]["ok"])
        _, report = restore(bak, self.fs, "/new", "lib-new", accept_resurrection="operator")
        self.assertEqual(report["status"], "incomplete")
        self.fs.remove("/bak/blobs/" + blob)
        self.assertFalse(Library.open(self.fs, "/bak").verify()["completeness"]["ok"])

    def test_source_damage_yields_an_incomplete_backup_never_a_complete_one(self):
        apps.phase2(self.src)
        st = self.src._story(apps.room("kitchen"))
        st.entries[-1].payload = None                              # a payload vanishes with no evidence
        self.fs.replace(self.src._path(apps.room("kitchen")), st.serialize())
        r = preserve(self.src, self.bak)
        self.assertEqual(r["status"], "incomplete")
        self.assertTrue(any("no removal evidence" in p for p in r["problems"]))
        self.assertEqual(preservations(self.bak)[-1]["complete"], False)

    def test_diverged_history_is_rejected_by_the_accept_rule(self):
        other = Library.create(self.fs, "/other", "lib-other")       # a different library, same story names
        apps.setup(other)
        for i in range(6):
            other.append(apps.OWNERSHIP, [(apps.rec("GoodPurchased", good="x", buyer="mallory", purchase="P%d" % i), STONE)])
        r = preserve(other, self.bak)
        self.assertEqual(r["status"], "incomplete")
        self.assertTrue(any("diverge" in p for p in r["problems"]))
        self.assertEqual(self.bak.head(apps.OWNERSHIP)[0], 5)        # nothing foreign was accepted
        with self.assertRaises(Conflict):                            # and the accept rule itself refuses
            self.bak.accept(apps.OWNERSHIP, 4, other.read(apps.OWNERSHIP, 4, 5))

    def test_a_source_that_lost_history_is_refused_as_a_new_baseline(self):
        fresh = Library.create(self.fs, "/fresh", "lib-src")          # the service restarted empty
        apps.setup(fresh)
        r = preserve(fresh, self.bak)
        self.assertEqual(r["status"], "incomplete")
        self.assertTrue(any("lost history" in p for p in r["problems"]))
        self.assertEqual(last_preserved(self.bak)["manifest"], self.src.capture_edition()["manifest"])


class TapeLikeMedia(unittest.TestCase):
    def test_a_backup_library_can_live_on_write_once_objects(self):
        fs = SimFS()
        src = source(fs, (apps.phase1, apps.phase2))
        media = ObjectFS()
        bak = Library.create(media, "/bak", "lib-bak")
        self.assertEqual(preserve(src, bak, fold=apps.fold)["status"], "complete")
        self.assertEqual(media.deletes, 0)                           # append-only on the media: nothing rewritten
        new, report = restore(Library.open(media, "/bak"), SimFS(), "/new", "lib-new", accept_resurrection="operator", fold=apps.fold)
        self.assertEqual(report["status"], "complete")
        self.assertEqual(new.fingerprint(), src.fingerprint())

    def test_write_once_media_cannot_forget_sand_and_says_so(self):
        fs = SimFS()
        src = source(fs)
        media = ObjectFS(worm=True)
        bak = Library.create(media, "/bak", "lib-bak")
        preserve(src, bak)
        src.release_claim("lamp:lamp.jpg", "owner asked", "alice")
        r = preserve(src, bak)                                       # evidence is preserved; bytes cannot go
        self.assertEqual(r["status"], "complete")
        report = Library.open(media, "/bak").verify()
        self.assertGreater(report["retention"]["retired_pending_reclaim"], 0)   # and that is reported


if __name__ == "__main__":
    unittest.main()
