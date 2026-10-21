"""Run inside xvfb-run + dbus-run-session. No real brightness writes."""
import os
from pathlib import Path
import tempfile
import time
import unittest
import gi
gi.require_version("Gtk", "3.0")
gi.require_version("Gdk", "3.0")
from gi.repository import Gdk, GLib, Gtk
from Xlib import X, XK, display
from Xlib.ext import xtest
from oled_guard.app import App


def pump(seconds=.15):
    end = time.monotonic() + seconds
    while time.monotonic() < end:
        while Gtk.events_pending():
            Gtk.main_iteration_do(False)
        time.sleep(.005)


class GuiTests(unittest.TestCase):
    @classmethod
    def setUpClass(cls):
        cls.temp = tempfile.TemporaryDirectory()
        os.environ["XDG_STATE_HOME"] = cls.temp.name
        cls.app = App()
        cls.app.register(None)
        cls.app.activate()
        pump()

    @classmethod
    def tearDownClass(cls):
        cls.app.stop()
        if cls.app.keys:
            cls.app.keys.close()
            cls.app.keys = None
        cls.app.window.destroy()
        cls.temp.cleanup()

    def setUp(self):
        self.app.stop()
        self.app.sleep.set_active(False)
        self.app.display.set_active(False)
        self.app.lock.set_active(False)
        self.app.mode.set_active_id("black")

    def test_black_overlay_and_global_space_restore(self):
        self.app.start()
        pump()
        self.assertTrue(self.app.active, self.app.status.get_text())
        overlay = self.app.overlays[0]
        self.assertFalse(overlay.get_accept_focus())
        pixels = Gdk.pixbuf_get_from_window(overlay.get_window(), 5, 5, 8, 8)
        data = pixels.get_pixels()
        stride, channels = pixels.get_rowstride(), pixels.get_n_channels()
        for y in range(8):
            for x in range(8):
                self.assertEqual(data[y*stride+x*channels:y*stride+x*channels+3], b"\0\0\0")
        connection = display.Display()
        code = connection.keysym_to_keycode(XK.string_to_keysym("space"))
        xtest.fake_input(connection, X.KeyPress, code)
        xtest.fake_input(connection, X.KeyRelease, code)
        connection.sync()
        pump(.4)
        connection.close()
        self.assertFalse(self.app.active)
        self.assertEqual(self.app.overlays, [])

    def test_retro_animates(self):
        self.app.mode.set_active_id("retro")
        self.app.start()
        pump(.2)
        self.assertTrue(self.app.active)
        self.assertGreater(len(self.app.overlays[0].trail), 1)
        first = self.app.overlays[0].trail[0]
        pump(.15)
        self.assertNotEqual(first, self.app.overlays[0].trail[-1])

    def test_real_language_widgets_change(self):
        self.app.settings.language = "en"
        self.app.tr.language = "en"
        self.app.build_ui()
        self.assertEqual(self.app.start_button.get_label(), "Start")
        self.app.settings.language = "tr"
        self.app.tr.language = "tr"
        self.app.build_ui()
        self.assertEqual(self.app.start_button.get_label(), "Başlat")


if __name__ == "__main__":
    unittest.main()
