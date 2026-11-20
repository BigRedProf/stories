"""Exhaustive crash injection: run an operation once to count its durable steps,
then re-run it crashing before each step, under each crash mode."""
from .core import Crash, SimFS

MODES = ("lose", "torn", "keep")


def crash_matrix(setup, op, check, stride=1, modes=MODES):
    """setup(fs) -> context; op(context) performs the operation under test;
    check(fs_after_crash, acked, step, mode) asserts the invariants.
    Returns the number of (step, mode) cases exercised."""
    fs = SimFS()
    ctx = setup(fs)
    before = fs.steps
    op(ctx)
    total = fs.steps - before
    cases = 0
    for i in list(range(0, total, stride)) + [total]:
        for mode in modes:
            fs = SimFS()
            ctx = setup(fs)
            fs.crash_at = fs.steps + i
            acked = False
            try:
                op(ctx)
                acked = True
            except Crash:
                pass
            check(fs.after_crash(mode), acked, i, mode)
            cases += 1
    return cases
