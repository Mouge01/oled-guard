from pathlib import Path
import zipfile
root = Path(__file__).resolve().parents[1]
output = root / "dist"
output.mkdir(exist_ok=True)
archive = output / "Caffeine-For-OLED-0.2.1-alpha-Windows.zip"
with zipfile.ZipFile(archive, "w", zipfile.ZIP_DEFLATED) as z:
    for name in ("CaffeineForOLED.exe", "CaffeineForOLED.exe.config"):
        z.write(root / "windows" / name, "Caffeine-For-OLED-0.2.1-alpha-Windows/" + name)
    for name in ("README.md", "README.tr.md", "VALIDATION.md", "LICENSE"):
        z.write(root / name, "Caffeine-For-OLED-0.2.1-alpha-Windows/" + name)
print(archive.name, archive.stat().st_size)
