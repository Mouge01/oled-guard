# OLED Guard · 0.2 alpha

[Türkçe](README.tr.md) · [Downloads](https://github.com/Mouge01/oled-guard/releases)

Manually started OLED screen protection for **Windows 11**, **Debian**, **Fedora** and **Arch Linux**, with an **English / Turkish** language selector.

Three modes: **Pitch black**, **Old style** moving ribbons, and **hardware brightness 0%**. Temporary sleep / display / idle-lock controls are released when protection stops. Previous brightness is recorded before dimming and a separate process attempts recovery if the application crashes.

## Install

| Platform | Release asset | Install / launch |
| --- | --- | --- |
| Windows 11 | `OLED-Guard-0.2-alpha-Windows.zip` | Extract; run `OLEDGuard.exe`. Keep its `.exe.config` beside it. Exit 0.1 from the tray before opening 0.2. |
| Debian | `oled-guard_0.2.0~alpha-1_all.deb` | `sudo apt install ./oled-guard_0.2.0~alpha-1_all.deb` |
| Fedora | `oled-guard-0.2.0-0.1.alpha.noarch.rpm` | `sudo dnf install ./oled-guard-0.2.0-0.1.alpha.noarch.rpm` |
| Arch | `oled-guard-0.2.0.alpha-1-any.pkg.tar.zst` | `sudo pacman -U ./oled-guard-0.2.0.alpha-1-any.pkg.tar.zst` |

Linux: launch **OLED Guard** from the application menu or run `oled-guard`. Dependencies come from the distribution's package manager. Packages do not install an autostart entry, system service, privilege-changing helper, or permissive device rules. Run the GUI as your normal desktop user.

The Linux packages contain architecture-independent Python code (`all` / `noarch` / `any`). Availability of distribution dependencies and hardware drivers still varies by architecture. The Windows portable build is managed AnyCPU and was tested on Windows x64.

## Use

Select a mode and options, then **Start**. **SPACE** restores the screen; **ESC** is the backup exit on Windows and X11. Windows and X11 also support **Ctrl+Alt+O** to start / stop. Windows has a further **Ctrl+Alt+Backspace** emergency shortcut. Protection only starts manually.

Choose **Language / Dil → English / Türkçe** to switch languages. The choice persists locally. Linux also accepts `oled-guard --language en` and `oled-guard --language tr` while protection is stopped.

After stopping, the application may remain hidden to preserve the previous application's focus. Windows: double-click the tray icon. Linux: launch `oled-guard` again from the menu to reopen settings. `oled-guard --stop` is also available for a user-defined desktop shortcut.

### USES Slayt Yardımcısı

Choose the **USES profile** to use brightness 0% without an overlay. Keep Chrome and the lesson tab open and unminimized. On Windows / X11, return to the lesson and press Ctrl+Alt+O. The application does not modify the extension or web page.

An opaque overlay can affect browser visibility / rendering decisions. Brightness mode avoids covering the browser. **0% brightness is not fully black on every panel.** Hardware mode protects only supported internal backlight/panel devices, not arbitrary external displays. Pure-black overlay mode paints RGB zero, but full physical panel blackout and long-term burn-in prevention are not guaranteed.

## Linux desktop compatibility

| Capability | X11 | Wayland |
| --- | --- | --- |
| Global exit | SPACE / ESC grabs while active | Hardware mode requires an approved GlobalShortcuts portal binding. The compositor may change the proposed SPACE binding; use the key shown in its permission dialog. |
| Black / retro overlay | Non-activating borderless windows, one per selected display | Focused overlay; standard fullscreen window when layer-shell is unavailable. This can change browser focus. |
| Multiple overlay displays | Supported | Requires a compositor supporting GTK layer-shell. Otherwise select one display; startup fails rather than silently leaving displays uncovered. |
| Brightness 0% | sysfs / brightnessctl | Same, with an approved global restore shortcut before dimming |
| Capture exclusion | Unavailable | Unavailable |

Wayland shortcut sessions are closed on stop so the application does not keep intercepting SPACE while idle. Portal support and exact shortcut assignment depend on the desktop. GNOME, KDE and other compositors may provide different capabilities. Wayland behavior has not yet been validated on physical desktop hardware.

Linux power control uses GTK desktop inhibitors, with logind and `org.freedesktop.ScreenSaver` fallbacks. A rejected request causes startup to fail with an error. Display timeout and automatic lock inhibition may be coupled by the desktop. Mandatory lock policies and manual locking still apply. Linux does not inject fake keyboard or mouse input.

Brightness permissions are supplied by the distribution's brightnessctl / desktop seat integration. If the hardware or permission is unavailable, use an overlay; the app does not escalate itself or change system security settings.

## Recovery and privacy

Windows data: `%LocalAppData%\OLEDGuard`. Linux data: `$XDG_STATE_HOME/oled-guard`, or `~/.local/state/oled-guard` by default. Preferences and brightness recovery records stay local. No telemetry, network calls, screen recording, or keystroke logging is implemented in the application.

If a display is disconnected or a driver rejects restoration, the recovery record is retained for another attempt on the next launch. Reconnect the display before restarting hardware brightness mode. Killing both the app and its recovery process, sudden power loss, or broken drivers can prevent immediate recovery. Use normal system brightness controls if necessary.

Windows capture exclusion is best effort and depends on the capture API. Linux does not implement the Windows capture-affinity feature. Keep the normal screen timeout / lock policies appropriate for your environment.

## Development and validation

- Windows: `powershell -File windows/build.ps1 -Test`; then `python tools/package_windows.py`.
- Linux tests: `bash tools/test_linux.sh` with GTK3, PyGObject, PyCairo, python-xlib, Xvfb and D-Bus installed.
- Linux packages: `python3 tools/package_linux.py build`. Requires `dpkg-deb`, `rpmbuild`, `makepkg`, `fakeroot`, `bsdtar` and `zstd`. Run as a non-root user; the script uses a temporary build directory.
- Generated Arch `PKGBUILD` contains the exact SHA-256 of the source release archive. It can be built with `makepkg -si` after downloading the matching source archive.
- CI tests the Linux implementation in Debian, Fedora and Arch containers using an isolated Xvfb display, and builds Windows and all package formats. Tagged alpha releases publish the resulting artifacts and SHA-256 sums.

See [VALIDATION.md](VALIDATION.md) for the exact scope and outstanding physical-hardware checks. Package-format validation and headless X11 tests do not prove Wayland, real OLED or USES end-to-end behavior.

The alpha binaries are not commercially signed. Windows application control and Linux package trust policy may require a trusted distribution/signing workflow; the app never disables those protections.

## License

No open-source license has been selected yet; see [LICENSE](LICENSE).

## API references

[Windows power requests](https://learn.microsoft.com/en-us/windows/win32/api/winbase/nf-winbase-setthreadexecutionstate), [Windows brightness](https://learn.microsoft.com/en-us/windows/win32/wmicoreprov/wmisetbrightness-method-in-class-wmimonitorbrightnessmethods), [capture affinity](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-setwindowdisplayaffinity), [Linux backlight](https://www.kernel.org/doc/html/latest/gpu/backlight.html), [GTK inhibitors](https://gnome.pages.gitlab.gnome.org/gtk/gtk3/method.Application.inhibit.html), [Wayland portal shortcuts](https://flatpak.github.io/xdg-desktop-portal/docs/doc-org.freedesktop.portal.GlobalShortcuts.html).
