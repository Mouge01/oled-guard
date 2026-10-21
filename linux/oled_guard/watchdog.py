"""Restore on parent pipe EOF. Runs separately from the GUI."""
from pathlib import Path
import sys
import time
from .core import Brightness, read_journal, restore_journal, state_dir


def main():
    if len(sys.argv) != 2:
        return 2
    journal = Path(sys.argv[1]).resolve()
    if journal.parent != state_dir().resolve() or journal.name != "recovery.json":
        return 2
    read_journal(journal)
    print("READY", flush=True)
    sys.stdin.buffer.read(1)  # Parent crash closes the pipe without needing PID polling.
    for attempt in range(3):
        try:
            restore_journal(Brightness(), journal)
            return 0
        except Exception:
            time.sleep(0.5)
    return 1


if __name__ == "__main__":
    raise SystemExit(main())
