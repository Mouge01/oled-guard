import json
import os
from pathlib import Path
import subprocess
import sys
import tempfile
import unittest
from oled_guard.core import Brightness, BrightnessTransaction, Settings, Translator, atomic_json, restore_journal


class Backend:
    def __init__(self, path):
        self.levels = {"panelA": 77, "panelB": 24}
        self.path = path
        self.guard_ready = False
        self.calls = []
        self.failure = lambda name, level: False

    def capture(self):
        return [dict(name=name, level=level) for name, level in self.levels.items()]

    def set(self, name, level):
        self.calls.append((name, level))
        if level == 0 and not (self.path.exists() and self.guard_ready):
            raise AssertionError("Brightness changed before recovery was ready")
        if self.failure(name, level):
            raise OSError("simulated hardware failure")
        self.levels[name] = level


class Guard:
    def __init__(self, backend, fail=False):
        self.backend, self.fail = backend, fail
        self.closed = False

    def start(self, path):
        if self.fail:
            raise OSError("watchdog failed")
        self.backend.guard_ready = True

    def close(self):
        self.closed = True


class CoreTests(unittest.TestCase):
    def setUp(self):
        self.temp = tempfile.TemporaryDirectory()
        self.path = Path(self.temp.name) / "recovery.json"
        self.backend = Backend(self.path)
        self.guard = Guard(self.backend)
        self.tx = BrightnessTransaction(self.backend, self.path, self.guard)

    def tearDown(self):
        self.temp.cleanup()

    def test_save_before_dim_and_exact_restore(self):
        self.tx.begin()
        self.assertEqual(self.backend.levels, {"panelA": 0, "panelB": 0})
        self.assertEqual(json.loads(self.path.read_text())[0]["level"], 77)
        self.tx.restore()
        self.assertEqual(self.backend.levels, {"panelA": 77, "panelB": 24})
        self.assertFalse(self.path.exists())
        self.assertTrue(self.guard.closed)
        self.tx.restore()

    def test_failed_dim_rolls_back_both_panels(self):
        self.backend.failure = lambda name, level: name == "panelB" and level == 0
        with self.assertRaises(OSError):
            self.tx.begin()
        self.assertEqual(self.backend.levels, {"panelA": 77, "panelB": 24})
        self.assertFalse(self.path.exists())

    def test_failed_watchdog_never_dims(self):
        self.guard.fail = True
        with self.assertRaises(OSError):
            self.tx.begin()
        self.assertFalse(any(level == 0 for _, level in self.backend.calls))

    def test_restore_failure_keeps_record_and_other_panel_restores(self):
        self.tx.begin()
        self.backend.failure = lambda name, level: name == "panelA" and level == 77
        with self.assertRaises(RuntimeError):
            self.tx.restore()
        self.assertTrue(self.path.exists())
        self.assertFalse(self.guard.closed)
        self.assertEqual(self.backend.levels["panelB"], 24)
        self.backend.failure = lambda name, level: False
        self.tx.restore()
        self.assertFalse(self.path.exists())

    def test_old_recovery_is_not_overwritten(self):
        atomic_json(self.path, [{"name": "panelA", "level": 60}])
        with self.assertRaises(RuntimeError):
            self.tx.begin()
        self.assertEqual(json.loads(self.path.read_text())[0]["level"], 60)

    def test_unsupported_hardware_has_no_writes(self):
        self.backend.levels.clear()
        with self.assertRaises(RuntimeError):
            self.tx.begin()
        self.assertFalse(self.path.exists())
        self.assertEqual(self.backend.calls, [])

    def test_malformed_journal_preserved(self):
        for contents in ('{broken', '[]', '[{"name":"../../etc/passwd","level":1}]', '[{"name":"x","level":true}]'):
            self.path.write_text(contents)
            with self.assertRaises(ValueError):
                restore_journal(self.backend, self.path)
            self.assertTrue(self.path.exists())

    def test_real_sysfs_adapter_with_temporary_files(self):
        device = Path(self.temp.name) / "fake_backlight"
        device.mkdir()
        (device / "brightness").write_text("72")
        (device / "max_brightness").write_text("255")
        backend = Brightness(Path(self.temp.name))
        self.assertEqual(backend.capture()[0]["level"], 72)
        backend.set("fake_backlight", 0)
        self.assertEqual((device / "brightness").read_text(), "0")
        backend.set("fake_backlight", 72)
        self.assertEqual((device / "brightness").read_text(), "72")
        with self.assertRaises(ValueError):
            backend.set("../escape", 5)
        with self.assertRaises(ValueError):
            backend.set("fake_backlight", 256)

    def test_settings_and_locale_persist(self):
        settings = Settings(language="tr", mode="retro", sleep=False, display=True, lock=True, monitor=1)
        settings.save(self.path)
        self.assertEqual(Settings.load(self.path), settings)
        self.assertEqual(os.stat(self.path).st_mode & 0o777, 0o600)

    def test_invalid_settings_fall_back(self):
        for data in ({"mode": "unsafe"}, {"sleep": "false"}, {"monitor": -3}, []):
            self.path.write_text(json.dumps(data))
            self.assertEqual(Settings.load(self.path).mode, "black")

    def test_translations_switch(self):
        tr = Translator("en")
        self.assertEqual(tr("Korumayı başlat"), "Start protection")
        tr.language = "tr"
        self.assertEqual(tr("Korumayı başlat"), "Korumayı başlat")


if __name__ == "__main__":
    unittest.main()
