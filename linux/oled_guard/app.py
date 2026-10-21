"""GTK desktop frontend for X11 and Wayland."""
import json
import math
import os
from pathlib import Path
import signal
import sys
import time
import gi
gi.require_version("Gtk", "3.0")
gi.require_version("Gdk", "3.0")
from gi.repository import Gdk, Gio, GLib, Gtk
from . import __version__
from .core import Brightness, BrightnessTransaction, Settings, Translator, restore_journal, state_dir
from .keys import X11Keys, PortalKeys
from .power import Power


def layer_shell():
    try:
        gi.require_version("GtkLayerShell", "0.1")
        from gi.repository import GtkLayerShell
        return GtkLayerShell if GtkLayerShell.is_supported() else None
    except (ValueError, ImportError):
        return None


class Overlay(Gtk.Window):
    def __init__(self, app, monitor_index, retro, focus, shell):
        super().__init__(type=Gtk.WindowType.TOPLEVEL)
        self.app = app
        self.retro = retro
        self.destroyed = False
        self.start_time = time.monotonic()
        self.trail = []
        self.source = None
        self.set_title("OLED Guard")
        self.set_decorated(False)
        self.set_skip_taskbar_hint(True)
        self.set_skip_pager_hint(True)
        self.set_keep_above(True)
        self.set_accept_focus(focus)
        self.set_focus_on_map(focus)
        display = Gdk.Display.get_default()
        monitor = display.get_monitor(monitor_index)
        geometry = monitor.get_geometry()
        if shell is not None:
            shell.init_for_window(self)
            shell.set_layer(self, shell.Layer.OVERLAY)
            shell.set_monitor(self, monitor)
            for edge in (shell.Edge.LEFT, shell.Edge.RIGHT, shell.Edge.TOP, shell.Edge.BOTTOM):
                shell.set_anchor(self, edge, True)
            shell.set_keyboard_mode(self, shell.KeyboardMode.EXCLUSIVE if focus else shell.KeyboardMode.NONE)
        elif app.wayland:
            self.fullscreen_on_monitor(Gdk.Screen.get_default(), monitor_index)
        else:
            self.set_type_hint(Gdk.WindowTypeHint.DOCK)
            self.move(geometry.x, geometry.y)
            self.resize(geometry.width, geometry.height)
        self.canvas = Gtk.DrawingArea()
        self.canvas.connect("draw", self.draw)
        self.add(self.canvas)
        self.connect("key-press-event", self.key)
        self.connect("realize", self.realized)
        self.connect("destroy", self.on_destroy)
        if retro:
            self.source = GLib.timeout_add(50, self.tick)

    def realized(self, window):
        self.get_window().set_cursor(Gdk.Cursor.new_for_display(Gdk.Display.get_default(), Gdk.CursorType.BLANK_CURSOR))

    def key(self, window, event):
        if event.keyval in (Gdk.KEY_space, Gdk.KEY_Escape):
            self.app.stop()
            return True
        return False

    def tick(self):
        if self.destroyed:
            return False
        self.canvas.queue_draw()
        return True

    def draw(self, canvas, context):
        context.set_source_rgb(0, 0, 0)
        context.paint()
        if not self.retro:
            return False
        width, height = canvas.get_allocated_width(), canvas.get_allocated_height()
        t = time.monotonic() - self.start_time
        points = [(width * (.5 + .34 * math.sin(t * .31 + i * 1.7)),
                   height * (.5 + .32 * math.sin(t * .23 + i * 2.2))) for i in range(4)]
        self.trail.append(points)
        self.trail = self.trail[-18:]
        for i, points in enumerate(self.trail):
            brightness = (18 + i * 3) / 255
            context.set_source_rgb(brightness / 3, brightness, min(.4, brightness + .06))
            context.set_line_width(1.5)
            context.move_to(*points[0])
            context.curve_to(*points[1], *points[2], *points[3])
            context.stroke()
        return False

    def on_destroy(self, window):
        self.destroyed = True
        if self.source is not None:
            GLib.source_remove(self.source)
            self.source = None


class App(Gtk.Application):
    def __init__(self):
        super().__init__(application_id="io.github.Mouge01.OLEDGuard", flags=Gio.ApplicationFlags.HANDLES_COMMAND_LINE)
        self.window = None
        self.keys = None
        self.overlays = []
        self.transaction = None
        self.power = None
        self.active = False
        self.busy = False
        self.pending_start = False
        self.recovery_error = False
        self.settings_path = state_dir() / "settings.json"
        self.journal = state_dir() / "recovery.json"
        self.settings = Settings.load(self.settings_path)
        self.tr = Translator(self.settings.language)
        self.backend = Brightness()
        self.wayland = False
        self.shell = None

    def do_activate(self):
        if self.window is not None:
            if not self.active:
                self.window.show_all()
                self.window.present()
            return
        self.window = Gtk.ApplicationWindow(application=self, title="OLED Guard · 0.2 alpha")
        self.window.set_default_size(690, 720)
        self.window.connect("delete-event", self.quit_requested)
        display = Gdk.Display.get_default()
        self.wayland = "Wayland" in display.__gtype__.name or os.environ.get("XDG_SESSION_TYPE") == "wayland"
        self.shell = layer_shell() if self.wayland else None
        self.install_style()
        self.build_ui()
        try:
            restore_journal(self.backend, self.journal)
        except Exception as error:
            self.recovery_error = True
            self.set_status(str(error))
        try:
            self.keys = PortalKeys(self.shortcut, self.portal_completed) if self.wayland else X11Keys(self.shortcut)
        except Exception as error:
            self.set_status(str(error))
        display.connect("monitor-added", self.monitors_changed)
        display.connect("monitor-removed", self.monitors_changed)
        self.window.show_all()

    def do_command_line(self, command):
        args = command.get_arguments()[1:]
        self.activate()
        if "--language" in args:
            index = args.index("--language")
            if index + 1 < len(args) and args[index + 1] in {"tr", "en"}:
                self.settings.language = args[index + 1]
                self.tr.language = self.settings.language
                self.build_ui()
                self.window.show_all()
        if "--stop" in args:
            self.stop()
        elif "--start" in args:
            self.start()
        return 0

    def install_style(self):
        css = Gtk.CssProvider()
        css.load_from_data(b"window, .background {background:#0e141b; color:#e7eef4;} label {color:#dbe6ef;} button {background:#19212a; color:#74eac5; border:1px solid #344552; padding:9px;} combobox button {padding:5px;} #start {background:#74eac5; color:#0c1e1d; font-weight:bold;} #brand {font-size:28px; font-weight:bold;} #status {color:#9eafbf;} checkbutton {padding:4px;} entry {color:#e7eef4;}")
        Gtk.StyleContext.add_provider_for_screen(Gdk.Screen.get_default(), css, Gtk.STYLE_PROVIDER_PRIORITY_APPLICATION)

    def label(self, text):
        label = Gtk.Label(label=self.tr(text), xalign=0)
        label.set_line_wrap(True)
        return label

    def build_ui(self):
        if self.window.get_child():
            child = self.window.get_child()
            self.window.remove(child)
            child.destroy()
        scroll = Gtk.ScrolledWindow()
        scroll.set_policy(Gtk.PolicyType.NEVER, Gtk.PolicyType.AUTOMATIC)
        self.window.add(scroll)
        box = Gtk.Box(orientation=Gtk.Orientation.VERTICAL, spacing=11, margin=24)
        scroll.add(box)
        brand = self.label("OLED GUARD")
        brand.set_name("brand")
        box.pack_start(brand, False, False, 0)
        languages = Gtk.ComboBoxText()
        languages.append("tr", "Türkçe")
        languages.append("en", "English")
        languages.set_active_id(self.settings.language)
        languages.connect("changed", self.language_changed)
        box.pack_start(languages, False, False, 0)
        box.pack_start(self.label("Ekranın dinlensin. Arka plandaki işin devam etsin."), False, False, 0)
        box.pack_start(self.label("SPACE  ile geri dön     ·     ESC  yedek çıkış"), False, False, 3)
        self.mode = Gtk.ComboBoxText()
        for key, text in (("black", "Pitch black  /  Tam siyah"), ("retro", "Old style  /  XP esintili hareketli çizgiler"), ("brightness", "Parlaklık %0  /  Örtü olmadan")):
            self.mode.append(key, self.tr(text))
        self.mode.set_active_id(self.settings.mode)
        box.pack_start(self.mode, False, False, 0)
        box.pack_start(self.label("Donanım parlaklığı sysfs/brightnessctl üzerinden denetlenir. %0 her panelde tam siyah değildir."), False, False, 0)
        self.monitor = Gtk.ComboBoxText()
        self.monitor.append("-1", self.tr("Tüm ekranlar"))
        display = Gdk.Display.get_default()
        count = display.get_n_monitors()
        for i in range(count):
            self.monitor.append(str(i), display.get_monitor(i).get_model() or "Display " + str(i + 1))
        if self.settings.monitor >= count:
            self.settings.monitor = -1
        self.monitor.set_active_id(str(self.settings.monitor))
        box.pack_start(self.monitor, False, False, 0)
        self.sleep = Gtk.CheckButton(label=self.tr("Uykuya geçmeyi engelle"))
        self.display = Gtk.CheckButton(label=self.tr("Ekran kapanmasını engelle"))
        self.lock = Gtk.CheckButton(label=self.tr("Otomatik kilidi engellemeyi dene"))
        for widget, active in ((self.sleep, self.settings.sleep), (self.display, self.settings.display), (self.lock, self.settings.lock)):
            widget.set_active(active)
            box.pack_start(widget, False, False, 0)
        box.pack_start(self.label("Linux masaüstleri ekran kapanması ve otomatik kilit engellemeyi birlikte uygulayabilir."), False, False, 0)
        profile = Gtk.Button(label=self.tr("USES profili: örtüsüz parlaklık %0"))
        profile.connect("clicked", self.uses_profile)
        box.pack_start(profile, False, False, 0)
        box.pack_start(self.label("Siyah örtü Chrome zamanlayıcılarını etkileyebilir. USES için örtüsüz parlaklık modunu tercih edin."), False, False, 0)
        if self.wayland:
            box.pack_start(self.label("Wayland: kısayol izni gerekiyor. Örtüsüz mod, global çıkış kısayolu hazır olmadan başlamaz."), False, False, 0)
            box.pack_start(self.label("Wayland örtüsü odağı alır; çoklu ekran örtüsü yalnızca desteklenen layer-shell masaüstlerinde kullanılabilir."), False, False, 0)
        else:
            box.pack_start(self.label("X11: SPACE / ESC global çıkış · Ctrl+Alt+O başlat / durdur"), False, False, 0)
        box.pack_start(self.label("Ekran yakalama dışlama Linux sürümünde desteklenmez."), False, False, 0)
        row = Gtk.Box(spacing=8)
        self.start_button = Gtk.Button(label=self.tr("Başlat"))
        self.start_button.set_name("start")
        self.start_button.connect("clicked", lambda button: self.start())
        stop = Gtk.Button(label=self.tr("Durdur"))
        stop.connect("clicked", lambda button: self.stop())
        row.pack_start(self.start_button, True, True, 0)
        row.pack_start(stop, True, True, 0)
        box.pack_start(row, False, False, 0)
        self.status = self.label("Hazır. Koruma yalnızca siz başlattığınızda çalışır.")
        self.status.set_name("status")
        box.pack_start(self.status, False, False, 0)
        box.pack_start(self.label("0.2 ALPHA · Linux · X11 / Wayland"), False, False, 0)

    def read_settings(self):
        return Settings(language=self.settings.language, mode=self.mode.get_active_id(), sleep=self.sleep.get_active(),
                        display=self.display.get_active(), lock=self.lock.get_active(), monitor=int(self.monitor.get_active_id()))

    def set_status(self, text):
        self.status.set_text(self.tr(text))

    def language_changed(self, combo):
        if self.active or self.busy:
            self.set_status("Korumayı durdurup dili değiştirebilirsiniz.")
            return
        self.settings = self.read_settings()
        self.settings.language = combo.get_active_id()
        self.tr.language = self.settings.language
        self.settings.save(self.settings_path)
        self.build_ui()
        self.window.show_all()

    def uses_profile(self, button):
        if not self.active:
            self.mode.set_active_id("brightness")
            self.sleep.set_active(True)
            self.display.set_active(True)
            self.lock.set_active(False)

    def shortcut(self, action):
        if action == "stop" or self.active:
            self.stop()
        else:
            self.start()
        return False

    def portal_completed(self, success):
        pending = self.pending_start
        self.pending_start = False
        if success and pending:
            self.start()
        else:
            self.set_status("Kısayollar hazır." if success else "Kısayol izni verilmedi veya masaüstü bu portalı desteklemiyor.")
            if self.keys and not success:
                self.keys.disarm()

    def start(self):
        if self.active or self.busy or self.pending_start:
            return
        self.settings = self.read_settings()
        if self.settings.mode == "brightness" and self.wayland and self.keys and not self.keys.ready:
            self.pending_start = True
            self.keys.configure()
            return
        self.busy = True
        try:
            if self.settings.mode == "brightness" and self.recovery_error:
                raise RuntimeError("Kurtarma tamamlanmadı. Donanım parlaklığını yeniden değiştirmeden önce aygıtı bağlayıp uygulamayı yeniden açın.")
            if not self.wayland or self.settings.mode == "brightness":
                if self.keys is None:
                    raise RuntimeError("Global çıkış kısayolu hazır değil. Önce Wayland kısayol iznini verin.")
                self.keys.arm()
            self.settings.save(self.settings_path)
            self.power = Power(self, self.window)
            self.power.begin(self.settings)
            if self.settings.mode == "brightness":
                self.transaction = BrightnessTransaction(self.backend, self.journal)
                self.transaction.begin()
            else:
                display = Gdk.Display.get_default()
                monitors = list(range(display.get_n_monitors())) if self.settings.monitor == -1 else [self.settings.monitor]
                if self.wayland and self.shell is None and len(monitors) > 1:
                    raise RuntimeError("Bu Wayland masaüstü tüm ekranlarda güvenilir örtü sunmuyor. Tek ekran seçin veya parlaklık modunu kullanın.")
                for index in monitors:
                    overlay = Overlay(self, index, self.settings.mode == "retro", self.wayland, self.shell)
                    self.overlays.append(overlay)
                    overlay.show_all()
            self.active = True
            self.set_status("Koruma etkin · SPACE ile dön")
            self.window.hide()
        except Exception as error:
            self.stop()
            self.set_status(str(error))
            self.window.show_all()
        finally:
            self.busy = False

    def stop(self):
        self.pending_start = False
        self.active = False
        errors = []
        for overlay in self.overlays:
            overlay.destroy()
        self.overlays.clear()
        try:
            if self.transaction:
                self.transaction.restore()
                self.transaction = None
        except Exception as error:
            errors.append(str(error))
            self.recovery_error = True
        finally:
            if self.power:
                self.power.close()
                self.power = None
            if self.keys:
                self.keys.disarm()
        self.set_status(errors[0] if errors else "Koruma durduruldu.")
        if errors:
            self.window.show_all()

    def monitors_changed(self, display, monitor):
        if self.window:
            self.stop()
            self.settings.monitor = -1
            self.build_ui()
            self.set_status("Ekran listesi yenilendi; korumayı yeniden başlatabilirsiniz.")

    def quit_requested(self, window, event):
        self.quit()
        return True

    def do_shutdown(self):
        if self.window:
            self.stop()
        if self.keys:
            self.keys.close()
        Gtk.Application.do_shutdown(self)


def main(args=None):
    app = App()
    GLib.unix_signal_add(GLib.PRIORITY_DEFAULT, signal.SIGTERM, lambda: (app.quit(), False)[1])
    GLib.unix_signal_add(GLib.PRIORITY_DEFAULT, signal.SIGINT, lambda: (app.quit(), False)[1])
    return app.run(args or sys.argv)
