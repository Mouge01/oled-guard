"""Temporary desktop and logind inhibitors. No policy or power-plan edits."""
import os
from gi.repository import Gio, GLib, Gtk


class Power:
    def __init__(self, app, window):
        self.app, self.window = app, window
        self.cookie = 0
        self.fd = None
        self.bus = None
        self.screen_cookie = None

    def begin(self, settings):
        flags = Gtk.ApplicationInhibitFlags(0)
        if settings.sleep:
            flags |= Gtk.ApplicationInhibitFlags.SUSPEND
        if settings.display or settings.lock:
            flags |= Gtk.ApplicationInhibitFlags.IDLE
        if not flags:
            return
        self.cookie = self.app.inhibit(self.window, flags, "Caffeine For OLED protection is active")
        if self.cookie:
            return
        try:
            if settings.sleep:
                system = Gio.bus_get_sync(Gio.BusType.SYSTEM, None)
                reply, descriptors = system.call_with_unix_fd_list_sync(
                    "org.freedesktop.login1", "/org/freedesktop/login1", "org.freedesktop.login1.Manager", "Inhibit",
                    GLib.Variant("(ssss)", ("sleep", "Caffeine For OLED", "OLED protection is active", "block")),
                    GLib.VariantType("(h)"), Gio.DBusCallFlags.NONE, 3000, None, None)
                self.fd = descriptors.get(reply.unpack()[0])
            if settings.display or settings.lock:
                self.bus = Gio.bus_get_sync(Gio.BusType.SESSION, None)
                reply = self.bus.call_sync("org.freedesktop.ScreenSaver", "/org/freedesktop/ScreenSaver",
                    "org.freedesktop.ScreenSaver", "Inhibit", GLib.Variant("(ss)", ("Caffeine For OLED", "OLED protection is active")),
                    GLib.VariantType("(u)"), Gio.DBusCallFlags.NONE, 3000, None)
                self.screen_cookie = reply.unpack()[0]
        except Exception as error:
            self.close()
            raise RuntimeError("Masaüstü güç/boşta kalma isteğini kabul etmedi.") from error

    def close(self):
        if self.cookie:
            self.app.uninhibit(self.cookie)
            self.cookie = 0
        if self.fd is not None:
            os.close(self.fd)
            self.fd = None
        if self.screen_cookie is not None:
            try:
                self.bus.call_sync("org.freedesktop.ScreenSaver", "/org/freedesktop/ScreenSaver",
                    "org.freedesktop.ScreenSaver", "UnInhibit", GLib.Variant("(u)", (self.screen_cookie,)),
                    None, Gio.DBusCallFlags.NONE, 1000, None)
            except GLib.Error:
                pass
            self.screen_cookie = None
