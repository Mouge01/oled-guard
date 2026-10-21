#!/usr/bin/env python3
"""Build real DEB, RPM and makepkg packages using native packaging tools."""
import argparse
import hashlib
import os
from pathlib import Path
import shutil
import subprocess
import tarfile
import tempfile

ROOT = Path(__file__).resolve().parents[1]
VERSION = "0.2.0-alpha"
SOURCE = "oled-guard-" + VERSION + "-source.tar.gz"


def run(command, **kwargs):
    subprocess.run(command, check=True, **kwargs)


def stage(dest):
    dest = Path(dest)
    library = dest / "usr/lib/oled-guard"
    package = library / "oled_guard"
    package.mkdir(parents=True, exist_ok=True)
    for file in (ROOT / "linux/oled_guard").glob("*.py"):
        shutil.copy2(file, package / file.name)
        os.chmod(package / file.name, 0o644)
    shutil.copy2(ROOT / "common/translations.json", package / "translations.json")
    (library / "launcher.py").write_text("from oled_guard import __main__\n", encoding="utf-8")
    executable = dest / "usr/bin/oled-guard"
    executable.parent.mkdir(parents=True, exist_ok=True)
    executable.write_text('#!/bin/sh\nexec /usr/bin/python3 /usr/lib/oled-guard/launcher.py "$@"\n', encoding="utf-8")
    os.chmod(executable, 0o755)
    for filename, location in (
        ("io.github.Mouge01.OLEDGuard.desktop", "usr/share/applications"),
        ("io.github.Mouge01.OLEDGuard.svg", "usr/share/icons/hicolor/scalable/apps"),
    ):
        target = dest / location
        target.mkdir(parents=True, exist_ok=True)
        shutil.copy2(ROOT / "packaging" / filename, target / filename)
        os.chmod(target / filename, 0o644)
    docs = dest / "usr/share/doc/oled-guard"
    docs.mkdir(parents=True, exist_ok=True)
    for name in ("README.md", "README.tr.md", "LICENSE"):
        shutil.copy2(ROOT / name, docs / name)
    for directory in [dest, *[p for p in dest.rglob("*") if p.is_dir()]]:
        os.chmod(directory, 0o755)
    for file in dest.rglob("*"):
        if file.is_file() and file != executable:
            os.chmod(file, 0o644)


def source_archive(output):
    output.mkdir(parents=True, exist_ok=True)
    archive = output / SOURCE
    def normalized(info):
        info.uid = info.gid = 0
        info.uname = info.gname = "root"
        info.mode = 0o755 if info.isdir() else 0o644
        return info
    with tarfile.open(archive, "w:gz", format=tarfile.PAX_FORMAT) as tar:
        for file in sorted(ROOT.rglob("*")):
            relative = file.relative_to(ROOT)
            if any(part in {".git", "dist", "build", "__pycache__"} for part in relative.parts):
                continue
            if file.is_file() and file.suffix not in {".exe", ".pyc", ".png", ".log"} and not file.name.endswith(".png.txt"):
                tar.add(file, arcname=Path("oled-guard-" + VERSION) / relative, recursive=False, filter=normalized)
    return archive


def build_deb(work, output):
    tree = work / "deb"
    stage(tree)
    control = tree / "DEBIAN"
    control.mkdir()
    (control / "control").write_text("""Package: oled-guard
Version: 0.2.0~alpha-1
Section: utils
Priority: optional
Architecture: all
Maintainer: Mouge01 <172084785+Mouge01@users.noreply.github.com>
Depends: python3 (>= 3.9), python3-gi, python3-gi-cairo, gir1.2-gtk-3.0, python3-xlib, brightnessctl
Recommends: gir1.2-gtklayershell-0.1
Homepage: https://github.com/Mouge01/oled-guard
Description: Manual OLED screen protection with brightness recovery
 Black and animated overlays, hardware brightness recovery, temporary power
 inhibition, and Turkish / English UI. Wayland capabilities depend on the desktop.
""", encoding="utf-8")
    os.chmod(control, 0o755)
    os.chmod(control / "control", 0o644)
    run(["dpkg-deb", "--root-owner-group", "--build", str(tree), str(output / "oled-guard_0.2.0~alpha-1_all.deb")])


def build_rpm(work, output, archive):
    top = work / "rpm"
    for name in ("BUILD", "BUILDROOT", "RPMS", "SOURCES", "SPECS", "SRPMS"):
        (top / name).mkdir(parents=True)
    shutil.copy2(archive, top / "SOURCES" / SOURCE)
    spec = top / "SPECS/oled-guard.spec"
    shutil.copy2(ROOT / "packaging/oled-guard.spec", spec)
    run(["rpmbuild", "-bb", "--nodeps", "--define", "_topdir " + str(top), str(spec)])
    for package in (top / "RPMS").rglob("*.rpm"):
        shutil.copy2(package, output / package.name)


def build_arch(work, output, archive):
    directory = work / "arch"
    directory.mkdir()
    shutil.copy2(archive, directory / SOURCE)
    checksum = hashlib.sha256(archive.read_bytes()).hexdigest()
    template = (ROOT / "packaging/PKGBUILD.in").read_text(encoding="utf-8")
    pkgbuild = template.replace("@SHA256@", checksum)
    (directory / "PKGBUILD").write_text(pkgbuild, encoding="utf-8")
    environment = os.environ.copy()
    environment["PKGDEST"] = str(output)
    environment["PACKAGER"] = "Mouge01 <172084785+Mouge01@users.noreply.github.com>"
    configuration = directory / "makepkg.conf"
    configuration.write_text(Path("/etc/makepkg.conf").read_text() + "\nPKGEXT='.pkg.tar.zst'\n", encoding="utf-8")
    run(["makepkg", "--config", str(configuration), "--nodeps", "--noconfirm", "--nocheck"], cwd=directory, env=environment)
    shutil.copy2(directory / "PKGBUILD", output / "PKGBUILD")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("action", choices=("stage", "build"))
    parser.add_argument("--dest", type=Path)
    parser.add_argument("--format", choices=("deb", "rpm", "arch", "all"), default="all")
    parser.add_argument("--output", type=Path, default=ROOT / "dist")
    args = parser.parse_args()
    if args.action == "stage":
        if args.dest is None:
            parser.error("stage requires --dest")
        stage(args.dest)
        return
    output = args.output.resolve()
    archive = source_archive(output)
    with tempfile.TemporaryDirectory(prefix="oled-packaging-") as temporary:
        work = Path(temporary)
        if args.format in {"deb", "all"}:
            build_deb(work, output)
        if args.format in {"rpm", "all"}:
            build_rpm(work, output, archive)
        if args.format in {"arch", "all"}:
            build_arch(work, output, archive)
    for package in sorted(output.iterdir()):
        if package.is_file():
            print(package.name, package.stat().st_size)


if __name__ == "__main__":
    main()
