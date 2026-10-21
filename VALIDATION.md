# 0.2 alpha validation

Local development environment: Windows 11 x64 and Ubuntu 26.04 in WSL2.

- Windows: 19 tests cover language switching, brightness snapshot/rollback/retry, crash recovery, power flags, input ABI, pure-black non-activating overlays, global SPACE swallowing, animation and a real watchdog process. Hardware brightness is not changed by these tests.
- Linux: 11 core tests cover exact brightness restoration, rollback, watchdog refusal, failed recovery retention, invalid journals, invalid settings, preferences, English/Turkish selection and a sysfs adapter operating only on temporary files.
- Linux X11: 3 GTK integration tests on isolated Xvfb check a pure-black overlay, focus policy, real X11 SPACE grab/restore, moving retro drawing and actual language widgets.
- Debian/RPM/Arch packages are built using dpkg-deb, rpmbuild and makepkg and inspected using their package-manager tools. The GTK launcher is checked after staging/extraction.
- CI contains native Debian, Fedora and Arch container test jobs and Windows build tests. Headless Windows CI skips the three interactive desktop tests (overlay, SPACE input, animation); local Windows runs include them.

Not yet validated: physical Linux OLED hardware, physical multi-monitor setups, GNOME/KDE/wlroots Wayland behavior, compositor shortcut permission dialogs, real desktop sleep/lock behavior, external monitor brightness, long-term panel burn-in, and uninterrupted USES lesson transitions.

The original Windows 0.1 alpha was reported working by the user. That report does not establish Linux or Wayland hardware behavior. A successful package build or isolated X11 test is not reported as a physical desktop test.
