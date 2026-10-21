"""X11 key grabs and compositor-approved Wayland shortcuts."""
import os
import select
import threading
import uuid
from gi.repository import Gio, GLib


class X11Keys:
    def __init__(self, callback):
        from Xlib import X, XK, display
        self.X, self.XK = X, XK
        self.display = display.Display()
        self.root = self.display.screen().root
        self.callback = callback
        self.active = False
        self.closed = False
        self.lock = threading.RLock()
        self.exit_codes = [self.display.keysym_to_keycode(XK.string_to_keysym(k)) for k in ("space", "Escape")]
        self.toggle_code = self.display.keysym_to_keycode(XK.string_to_keysym("o"))
        self.modifiers = [X.ControlMask | X.Mod1Mask | extra for extra in (0, X.LockMask, X.Mod2Mask, X.LockMask | X.Mod2Mask)]
        self.errors = []
        self.display.set_error_handler(lambda error, request: self.errors.append(error))
        for modifiers in self.modifiers:
            self.root.grab_key(self.toggle_code, modifiers, False, X.GrabModeAsync, X.GrabModeAsync)
        self.display.sync()
        # A conflicting toggle shortcut does not make the exit shortcut unsafe.
        self.toggle_available = not self.errors
        self.errors.clear()
        self.thread = threading.Thread(target=self.run, daemon=True)
        self.thread.start()

    def arm(self):
        with self.lock:
            self.errors.clear()
            for code in self.exit_codes:
                self.root.grab_key(code, self.X.AnyModifier, False, self.X.GrabModeAsync, self.X.GrabModeAsync)
            self.display.sync()
            if self.errors:
                self.disarm()
                raise RuntimeError("Global SPACE / ESC shortcut is already in use")
            self.active = True

    def disarm(self):
        with self.lock:
            self.active = False
            for code in self.exit_codes:
                self.root.ungrab_key(code, self.X.AnyModifier)
            self.display.flush()

    def run(self):
        try:
            while not self.closed:
                if not select.select([self.display], [], [], .1)[0]:
                    continue
                with self.lock:
                    while self.display.pending_events():
                        event = self.display.next_event()
                        # Act on release, consuming the complete SPACE/ESC pair.
                        if event.type == self.X.KeyRelease:
                            action = "stop" if self.active and event.detail in self.exit_codes else "toggle" if event.detail == self.toggle_code else None
                            if action:
                                GLib.idle_add(self.callback, action)
        except (OSError, ConnectionError):
            if not self.closed:
                GLib.idle_add(self.callback, "stop")

    def close(self):
        self.disarm()
        self.closed = True
        self.thread.join(timeout=.5)
        self.display.close()


class PortalKeys:
    """Only compositor-approved bindings are used; no input injection on Wayland."""
    SERVICE = "org.freedesktop.portal.Desktop"
    PATH = "/org/freedesktop/portal/desktop"
    INTERFACE = "org.freedesktop.portal.GlobalShortcuts"

    def __init__(self, callback, completed):
        self.bus = Gio.bus_get_sync(Gio.BusType.SESSION, None)
        self.callback = callback
        self.completed = completed
        self.session = None
        self.ready = False
        self.pending = False
        self.subscriptions = []

    def request(self, method, parameters, then):
        token = "oled_" + uuid.uuid4().hex
        args = list(parameters)
        args[-1]["handle_token"] = GLib.Variant("s", token)
        sender = self.bus.get_unique_name()[1:].replace(".", "_")
        expected = "/org/freedesktop/portal/desktop/request/" + sender + "/" + token
        subscription = None

        def response(connection, sender_name, path, interface, signal, values):
            self.bus.signal_unsubscribe(subscription)
            if subscription in self.subscriptions:
                self.subscriptions.remove(subscription)
            code, results = values.unpack()
            if code != 0:
                self.pending = False
                self.completed(False)
                return
            then(results)

        subscription = self.bus.signal_subscribe(self.SERVICE, "org.freedesktop.portal.Request", "Response", expected,
                                                 None, Gio.DBusSignalFlags.NONE, response)
        self.subscriptions.append(subscription)
        signature = "(a{sv})" if method == "CreateSession" else "(oa(sa{sv})sa{sv})"
        try:
            self.bus.call_sync(self.SERVICE, self.PATH, self.INTERFACE, method, GLib.Variant(signature, tuple(args)),
                               GLib.VariantType("(o)"), Gio.DBusCallFlags.NONE, 3000, None)
        except GLib.Error:
            self.bus.signal_unsubscribe(subscription)
            self.subscriptions.remove(subscription)
            self.pending = False
            self.completed(False)

    def configure(self):
        if self.pending or self.ready:
            return
        self.pending = True

        def created(result):
            self.session = result["session_handle"]
            shortcuts = [
                ("restore", {"description": GLib.Variant("s", "Caffeine For OLED: restore display"), "preferred_trigger": GLib.Variant("s", "space")}),
                ("toggle", {"description": GLib.Variant("s", "Caffeine For OLED: start / stop"), "preferred_trigger": GLib.Variant("s", "CTRL+ALT+o")}),
            ]
            self.request("BindShortcuts", [self.session, shortcuts, "", {}], bound)

        def bound(result):
            self.pending = False
            shortcuts = result.get("shortcuts", [])
            self.ready = any(item[0] == "restore" for item in shortcuts)
            self.subscriptions.append(self.bus.signal_subscribe(self.SERVICE, self.INTERFACE, "Activated", self.PATH,
                None, Gio.DBusSignalFlags.NONE, self.activated))
            self.subscriptions.append(self.bus.signal_subscribe(self.SERVICE, self.INTERFACE, "Deactivated", self.PATH,
                None, Gio.DBusSignalFlags.NONE, self.activated))
            self.completed(self.ready)

        self.request("CreateSession", [{"session_handle_token": GLib.Variant("s", "oled_" + uuid.uuid4().hex)}], created)

    def activated(self, connection, sender, path, interface, signal, values):
        session, key, _, _ = values.unpack()
        if session != self.session:
            return
        if key == "restore" and signal == "Deactivated":
            self.callback("stop")
        elif key == "toggle" and signal == "Activated":
            self.callback("toggle")

    def arm(self):
        if not self.ready:
            raise RuntimeError("Global çıkış kısayolu hazır değil. Önce Wayland kısayol iznini verin.")

    def disarm(self):
        self.close()

    def close(self):
        for subscription in self.subscriptions:
            self.bus.signal_unsubscribe(subscription)
        self.subscriptions.clear()
        if self.session:
            try:
                self.bus.call_sync(self.SERVICE, self.session, "org.freedesktop.portal.Session", "Close", None,
                                   None, Gio.DBusCallFlags.NONE, 1000, None)
            except GLib.Error:
                pass
        self.session = None
        self.ready = False
        self.pending = False
