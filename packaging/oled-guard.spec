Name:           oled-guard
Version:        0.2.0
Release:        0.1.alpha%{?dist}
Summary:        Manual OLED screen protection with brightness recovery
License:        LicenseRef-Proprietary
URL:            https://github.com/Mouge01/oled-guard
Source0:        oled-guard-0.2.0-alpha-source.tar.gz
BuildArch:      noarch
BuildRequires:  python3
Requires:       python3 >= 3.9
Requires:       python3-gobject
Requires:       python3-cairo
Requires:       gtk3
Requires:       python3-xlib
Requires:       brightnessctl
Recommends:     gtk-layer-shell
AutoReqProv:    no

%description
Manually started OLED protector with black and animated overlays, hardware
brightness recovery, temporary power inhibition, and Turkish / English UI.
X11 provides global SPACE exit. Wayland hardware dimming requires an approved
global shortcut; overlay coverage depends on the compositor.

%prep
%setup -q -n oled-guard-0.2.0-alpha

%build

%install
python3 tools/package_linux.py stage --dest %{buildroot}

%files
%{_bindir}/oled-guard
/usr/lib/oled-guard/
%{_datadir}/applications/io.github.Mouge01.OLEDGuard.desktop
%{_datadir}/icons/hicolor/scalable/apps/io.github.Mouge01.OLEDGuard.svg
%doc %{_datadir}/doc/oled-guard/README.md
%doc %{_datadir}/doc/oled-guard/README.tr.md
%license %{_datadir}/doc/oled-guard/LICENSE

%changelog
* Thu Oct 22 2026 Mouge01 - 0.2.0-0.1.alpha
- Linux frontend and English / Turkish language selection.
