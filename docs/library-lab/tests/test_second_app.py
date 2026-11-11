"""A second application, to check the contracts are not Digihouse-shaped."""
import unittest

from lab import apps
from lab.core import Library, SimFS
from lab.preserve import preserve, restore


class Scoreboard(unittest.TestCase):
    def test_keep_set_results_compaction_backup_and_restore(self):
        fs = SimFS()
        lib = Library.create(fs, "/src", "lib-score")
        apps.scoreboard_play(lib, 100)                       # two full sets and 20 rallies of a third
        sets = apps.scoreboard_sets(lib)
        self.assertEqual([sum(s) for s in sets], [40, 40, 20])
        # This activity's policy is NOT 'latest position': it keeps finished set scores.
        lib.compact(apps.RALLIES, 80, apps.rec("SetResults", sets=sets[:2]), "anchor", "keep-set-results", "league rules")
        self.assertEqual(apps.scoreboard_sets(lib), sets)
        for i in range(100, 120):
            lib.append(apps.RALLIES, [(apps.rec("Rally", n=i, won="home"), apps.SAND)])
        final = apps.scoreboard_finish(lib)                  # the result is stone
        bak = Library.create(fs, "/bak", "lib-bak")
        self.assertEqual(preserve(lib, bak)["status"], "complete")
        new, report = restore(bak, SimFS(), "/new", "lib-new", accept_resurrection="operator")
        self.assertEqual(report["status"], "complete")
        self.assertEqual(new.fingerprint(), lib.fingerprint())
        self.assertEqual(apps.scoreboard_sets(new), final)
        self.assertEqual(new.read(apps.RESULTS)[0].code().json()["sets"], final)


if __name__ == "__main__":
    unittest.main()
