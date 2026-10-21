from pathlib import Path
import zipfile
root = Path(__file__).resolve().parents[1]
output = root / "dist"
output.mkdir(exist_ok=True)
archive = output / "OLED-Guard-0.2-alpha-Windows.zip"
with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED) as z:
    for name in ("OLEDGuard.exe", "OLEDGuard.exe.config"):
        z.write(root / "windows" / name, "OLED-Guard-0.2-alpha-Windows/" + name)
    for name in ("README.md", "README.tr.md", "VALIDATION.md", "LICENSE"):
        z.write(root / name, "OLED-Guard-0.2-alpha-Windows/" + name)
print(archive.name, archive.stat().st_size)
