"""Story #26: all-or-nothing appends. Each test names the guarantee it proves."""
import threading
import unittest

from lab import apps
from lab.alt import JournalLibrary, LegacyStory
from lab.core import (CATALOG, CONTENT, SAND, STONE, Code, Conflict, Crash, Damage, Library, SimFS,
                      Story, edition_id, txn_id)
from lab.harness import crash_matrix

BATCH = [b"thing-%d" % i for i in range(4)]
ITEMS = [(Code.of_json({"k": "T", "i": i}), STONE) for i in range(4)]


def new_lib(fs, root="/lib"):
    lib = Library.create(fs, root, "lib-1")
    apps.setup(lib)
    return lib


def purchase_writes():
    return {apps.OWNERSHIP: [(apps.rec("GoodPurchased", good="vase", buyer="dan", purchase="P9"), STONE)],
            apps.room("den"): [(apps.rec("GoodPlaced", good="vase"), STONE), (apps.rec("Note", text="fragile"), SAND)],
            CONTENT: [(apps.rec("ContentClaimed", claim="vase:jpg", blob="0" * 64, cls=SAND), STONE)]}


class LegacyBaseline(unittest.TestCase):
    def test_todays_per_thing_append_tears_a_batch(self):
        """Evidence against the status quo: a 4-thing append can survive as 1, 2 or 3 things."""
        seen = set()

        def check(fs, acked, step, mode):
            seen.add(len(LegacyStory(fs, "/s").read()))
        crash_matrix(lambda fs: LegacyStory(fs, "/s"), lambda s: s.append(BATCH), check)
        self.assertEqual(seen, {0, 1, 2, 3, 4})

    def test_todays_retry_after_a_lost_ack_duplicates(self):
        fs = SimFS()
        s = LegacyStory(fs, "/s")
        s.append(BATCH)          # durable, but the caller never hears back
        s.append(BATCH)          # so it retries
        self.assertEqual(len(s.read()), 8)

    def test_bit_rot_in_the_last_acknowledged_frame_is_silently_dropped(self):
        fs = SimFS()
        s = LegacyStory(fs, "/s")
        s.append(BATCH)
        data = bytearray(fs.read("/s"))
        data[-6] ^= 1            # damage inside the final, fully acknowledged frame
        fs.replace("/s", bytes(data))
        self.assertEqual(len(LegacyStory(fs, "/s").read()), 3)   # no error: it looks like a torn tail


class StoryBatch(unittest.TestCase):
    def test_single_story_batch_is_all_or_nothing_and_durable_once_acked(self):
        seen = set()

        def check(fs, acked, step, mode):
            lib = Library.open(fs, "/lib")
            n = lib.head(apps.OWNERSHIP)[0]
            seen.add(n)
            self.assertIn(n, (0, 4), "step %d mode %s left a partial batch" % (step, mode))
            if acked:
                self.assertEqual(n, 4)
        cases = crash_matrix(new_lib, lambda lib: lib.append(apps.OWNERSHIP, ITEMS, txn=txn_id("b")), check)
        self.assertEqual(seen, {0, 4})
        self.assertGreaterEqual(cases, 9)

    def test_expected_position_lets_exactly_one_competing_writer_win(self):
        lib = new_lib(SimFS())
        for round_ in range(25):
            at = lib.head(apps.OWNERSHIP)[0]
            results, gate = [], threading.Barrier(4)

            def writer(name):
                gate.wait()
                try:
                    lib.append(apps.OWNERSHIP, [(apps.rec("GoodTransferred", good="g", seller="x", buyer=name), STONE)],
                               expected_pos=at)
                    results.append("won")
                except Conflict:
                    results.append("conflict")
            threads = [threading.Thread(target=writer, args=("w%d" % i,)) for i in range(4)]
            [t.start() for t in threads]
            [t.join() for t in threads]
            self.assertEqual(sorted(results), ["conflict"] * 3 + ["won"])
            self.assertEqual(lib.head(apps.OWNERSHIP)[0], at + 1)

    def test_stale_and_future_expected_positions_conflict(self):
        lib = new_lib(SimFS())
        lib.append(apps.OWNERSHIP, ITEMS[:1], expected_pos=0)
        for wrong in (0, 2):
            with self.assertRaises(Conflict):
                lib.append(apps.OWNERSHIP, ITEMS[:1], expected_pos=wrong)

    def test_retry_after_a_lost_ack_is_a_no_op(self):
        lib = new_lib(SimFS())
        first = lib.append(apps.OWNERSHIP, ITEMS, txn=txn_id("pay"))
        again = lib.append(apps.OWNERSHIP, ITEMS, txn=txn_id("pay"))
        self.assertEqual(first, again)
        self.assertEqual(lib.head(apps.OWNERSHIP)[0], 4)
        reopened = Library.open(lib.fs, "/lib")       # the retry id survives a restart
        self.assertEqual(reopened.append(apps.OWNERSHIP, ITEMS, txn=txn_id("pay")), first)
        self.assertEqual(reopened.head(apps.OWNERSHIP)[0], 4)


class LibraryCommit(unittest.TestCase):
    def test_multi_story_commit_is_all_or_nothing_across_every_crash_point(self):
        touched = (apps.OWNERSHIP, apps.room("den"), CONTENT)
        outcomes = set()

        def check(fs, acked, step, mode):
            lib = Library.open(fs, "/lib")
            grew = [lib.head(s)[0] - b for s, b in zip(touched, (0, 1, 0))]
            self.assertIn(grew, ([0, 0, 0], [1, 2, 1]), "step %d mode %s published part of a commit" % (step, mode))
            outcomes.add(tuple(grew))
            if acked:
                self.assertEqual(grew, [1, 2, 1])
            # Unknown outcome is reconciled by retrying the same transaction id: exactly once.
            lib.commit(purchase_writes(), txn_id("P9"))
            self.assertEqual([lib.head(s)[0] - b for s, b in zip(touched, (0, 1, 0))], [1, 2, 1])
            self.assertTrue(Library.open(fs, "/lib").verify()["integrity"]["ok"])
        cases = crash_matrix(new_lib, lambda lib: lib.commit(purchase_writes(), txn_id("P9")), check)
        self.assertEqual(outcomes, {(0, 0, 0), (1, 2, 1)})
        self.assertGreaterEqual(cases, 24)

    def test_story_creation_and_its_first_entries_commit_with_the_catalog_entry(self):
        sid = apps.room("study")

        def check(fs, acked, step, mode):
            lib = Library.open(fs, "/lib")
            listed = sid in lib.policy
            self.assertEqual(lib.head(sid)[0] if listed else 0, 1 if listed else 0)
            self.assertEqual(listed, lib.head(CATALOG)[0] == 5)
            if acked:
                self.assertTrue(listed)
        crash_matrix(new_lib, lambda lib: lib.begin_story(sid, "mixed", [(apps.rec("RoomCreated", room="study"), STONE)]), check)

    def test_competing_commits_with_the_same_expected_positions_cannot_both_win(self):
        lib = new_lib(SimFS())
        expected = {apps.OWNERSHIP: 0, apps.room("den"): 1}
        lib.commit(purchase_writes(), txn_id("A"), expected=expected)
        with self.assertRaises(Conflict):
            lib.commit(purchase_writes(), txn_id("B"), expected=expected)
        self.assertEqual(lib.head(apps.OWNERSHIP)[0], 1)

    def test_expected_edition_predicate(self):
        lib = new_lib(SimFS())
        seen = edition_id(lib.manifest())
        lib.commit(purchase_writes(), txn_id("A"), expected_edition=seen)
        with self.assertRaises(Conflict):
            lib.commit(purchase_writes(), txn_id("B"), expected_edition=seen)

    def test_readers_never_see_part_of_a_commit_in_flight(self):
        lib = new_lib(SimFS())
        seen = []

        def reader_between_stages(step):
            # A reader on another connection. It reads committed state from disk.
            other = Library(lib.fs.clone(), "/lib")
            other.committed = dict(lib.committed)
            other.policy = dict(lib.policy)
            seen.append((len(other.read(apps.OWNERSHIP)), len(other.read(apps.room("den")))))
        lib.hook = reader_between_stages
        lib.commit(purchase_writes(), txn_id("P9"))
        self.assertEqual(set(seen), {(0, 1)})                 # nothing of the commit, at every stage
        self.assertEqual((lib.head(apps.OWNERSHIP)[0], lib.head(apps.room("den"))[0]), (1, 3))

    def test_journal_design_is_durable_but_publishes_story_by_story(self):
        """Design D: all-or-nothing after a crash, yet a live reader sees half a commit."""
        writes = {"a": [b"a1"], "b": [b"b1"], "c": [b"c1"]}

        def check(fs, acked, step, mode):
            j = JournalLibrary(fs, "/j")              # recovery replays the journal
            counts = [len(j.read(s)) for s in "abc"]
            self.assertIn(counts, ([0, 0, 0], [1, 1, 1]))
        crash_matrix(lambda fs: JournalLibrary(fs, "/j"), lambda j: j.commit(writes), check)
        j = JournalLibrary(SimFS(), "/j")
        partial = []
        j.hook = lambda step: partial.append([len(j.read(s)) for s in "abc"])
        j.commit(writes)
        self.assertIn([1, 0, 0], partial)             # the intermediate state was visible

    def test_atomic_publication_is_not_a_snapshot_read(self):
        """Two ordinary reads straddling a commit disagree; an edition-pinned read does not."""
        lib = new_lib(SimFS())
        edition = lib.capture_edition()["manifest"]
        owners_before = len(lib.read(apps.OWNERSHIP))           # read story 1 ...
        lib.commit(purchase_writes(), txn_id("P9"))             # ... a commit lands ...
        den_after = len(lib.read(apps.room("den")))             # ... read story 2
        self.assertEqual((owners_before, den_after), (0, 3))    # the vase is placed but nobody bought it
        pinned = (len(lib.read_at(edition, apps.OWNERSHIP)), len(lib.read_at(edition, apps.room("den"))))
        self.assertEqual(pinned, (0, 1))                        # consistent: before the commit, in both

    def test_a_raw_file_copy_taken_mid_commit_is_never_silently_half_a_commit(self):
        lib = new_lib(SimFS())
        copies = []
        lib.hook = lambda step: copies.append(lib.fs.clone())
        lib.commit(purchase_writes(), txn_id("P9"))
        # 1. Naive reading of story files ignores transaction markers and DOES see half a commit.
        mid = copies[0]
        naive = [Story.parse(s, mid.read(lib._path(s)) or b"", None)[0].head() for s in (CONTENT, apps.OWNERSHIP)]
        self.assertEqual(naive, [1, 0])
        # 2. Opening that same copy as a Library recovers to exactly the pre-commit state.
        opened = Library.open(mid, "/lib")
        self.assertEqual([opened.head(s)[0] for s in (CONTENT, apps.OWNERSHIP, apps.room("den"))], [0, 0, 1])
        # 3. A copy with the NEW commit log but an OLD story file is refused, not accepted.
        mixed = lib.fs.clone()
        mixed.files[lib._path(apps.OWNERSHIP)] = list(copies[0].files.get(lib._path(apps.OWNERSHIP), [b"", b""]))
        with self.assertRaises(Damage):
            Library.open(mixed, "/lib")

    def test_commit_log_witness_turns_silent_tail_loss_into_a_detected_error(self):
        lib = new_lib(SimFS())
        lib.commit(purchase_writes(), txn_id("P9"))
        path = lib._path(apps.OWNERSHIP)
        data = bytearray(lib.fs.read(path))
        data[-6] ^= 1
        lib.fs.replace(path, bytes(data))
        with self.assertRaises(Damage):
            Library.open(lib.fs, "/lib")

    def test_plain_story_appends_have_no_witness_until_an_edition_is_captured(self):
        def damaged(capture):
            lib = new_lib(SimFS())
            lib.append(apps.OWNERSHIP, ITEMS)
            if capture:
                lib.capture_edition()
            path = lib._path(apps.OWNERSHIP)
            data = bytearray(lib.fs.read(path))
            data[-6] ^= 1
            lib.fs.replace(path, bytes(data))
            return lib.fs
        self.assertEqual(Library.open(damaged(False), "/lib").head(apps.OWNERSHIP)[0], 0)   # silently lost
        with self.assertRaises(Damage):
            Library.open(damaged(True), "/lib")

    def test_mixed_stone_and_sand_commit_together_and_retire_apart(self):
        lib = new_lib(SimFS())
        lib.commit(purchase_writes(), txn_id("P9"))
        chain = lib.head(apps.room("den"))[1]
        lib.remove_payload(apps.room("den"), 2, "owner asked", "alice")
        placed, note = lib.read(apps.room("den"))[1:]
        self.assertIsNotNone(placed.payload)                  # stone stays
        self.assertIsNone(note.payload)                       # sand went
        self.assertEqual(lib.head(apps.room("den"))[1], chain)  # history identity is unchanged


if __name__ == "__main__":
    unittest.main()
