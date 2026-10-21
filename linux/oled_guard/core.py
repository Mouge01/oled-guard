"""Hardware transactions and crash recovery; no GTK dependency."""
from dataclasses import asdict, dataclass, fields
import json
import locale
import os
from pathlib import Path
import re
import select
import subprocess
import sys
import tempfile


def state_dir():
    return Path(os.environ.get("XDG_STATE_HOME", str(Path.home() / ".local/state"))) / "oled-guard"


def atomic_json(path, data):
    path = Path(path)
    path.parent.mkdir(mode=0o700, parents=True, exist_ok=True)
    fd, name = tempfile.mkstemp(prefix=".oled-", dir=path.parent)
    try:
        with os.fdopen(fd, "w", encoding="utf-8") as stream:
            json.dump(data, stream, ensure_ascii=False)
            stream.flush()
            os.fsync(stream.fileno())
        os.replace(name, path)
    finally:
        if os.path.exists(name):
            os.unlink(name)


@dataclass
class Settings:
    language: str = "en"
    mode: str = "black"
    sleep: bool = True
    display: bool = True
    lock: bool = False
    monitor: int = -1

    @classmethod
    def load(cls, path):
        language = "tr" if (locale.getlocale()[0] or "").startswith("tr") else "en"
        try:
            data = json.loads(Path(path).read_text(encoding="utf-8"))
            if not isinstance(data, dict):
                raise ValueError("settings must be an object")
            result = cls(**{f.name: data[f.name] for f in fields(cls) if f.name in data})
            if result.mode not in {"black", "retro", "brightness"} or result.language not in {"tr", "en"}:
                raise ValueError("invalid setting")
            if any(type(getattr(result, key)) is not bool for key in ("sleep", "display", "lock")):
                raise ValueError("invalid boolean")
            if type(result.monitor) is not int or result.monitor < -1:
                raise ValueError("invalid monitor")
            return result
        except (OSError, ValueError, TypeError):
            return cls(language=language)

    def save(self, path):
        atomic_json(path, asdict(self))


class Translator:
    def __init__(self, language="en", path=None):
        path = path or Path(__file__).with_name("translations.json")
        if not Path(path).exists():
            path = Path(__file__).resolve().parents[2] / "common/translations.json"
        self.table = json.loads(Path(path).read_text(encoding="utf-8"))
        self.language = language

    def __call__(self, text):
        return self.table.get(text, text) if self.language == "en" else text


class Brightness:
    def __init__(self, root=Path("/sys/class/backlight"), runner=subprocess.run):
        self.root = Path(root)
        self.runner = runner

    def device(self, name):
        if not isinstance(name, str) or not re.fullmatch(r"[A-Za-z0-9_.:-]+", name) or name in {".", ".."}:
            raise ValueError("Invalid backlight device name")
        return self.root / name

    def capture(self):
        result = []
        for device in sorted(self.root.glob("*")):
            level = int((device / "brightness").read_text().strip())
            maximum = int((device / "max_brightness").read_text().strip())
            if maximum > 0 and 0 <= level <= maximum:
                result.append({"name": device.name, "level": level})
        return result

    def set(self, name, level):
        device = self.device(name)
        maximum = int((device / "max_brightness").read_text().strip())
        if type(level) is not int or not 0 <= level <= maximum:
            raise ValueError("Invalid brightness level")
        target = device / "brightness"
        if os.access(target, os.W_OK):
            target.write_text(str(level), encoding="ascii")
        else:
            try:
                self.runner(["brightnessctl", "--class=backlight", "--device=" + name, "--quiet", "set", str(level)],
                            check=True, timeout=4, stdout=subprocess.PIPE, stderr=subprocess.PIPE)
            except (OSError, subprocess.SubprocessError) as error:
                raise RuntimeError("Parlaklık aygıtına yazılamadı. Masaüstünün brightnessctl/seat izinlerini kontrol edin.") from error


def read_journal(path):
    data = json.loads(Path(path).read_text(encoding="utf-8"))
    if not isinstance(data, list) or not data:
        raise ValueError("Kurtarma kaydı geçersiz.")
    for record in data:
        if not isinstance(record, dict) or set(record) != {"name", "level"} or type(record["level"]) is not int:
            raise ValueError("Kurtarma kaydı geçersiz.")
        if not isinstance(record["name"], str) or not re.fullmatch(r"[A-Za-z0-9_.:-]+", record["name"]) or record["name"] in {".", ".."}:
            raise ValueError("Kurtarma kaydı geçersiz.")
    return data


def restore_journal(backend, path):
    if not Path(path).exists():
        return
    errors = []
    for item in read_journal(path):
        try:
            backend.set(item["name"], item["level"])
        except Exception as error:
            errors.append(error)
    if errors:
        raise RuntimeError("Parlaklık geri yüklenemedi; kurtarma kaydı saklandı.") from errors[0]
    Path(path).unlink(missing_ok=True)


class Watchdog:
    def __init__(self):
        self.process = None

    def start(self, journal):
        env = os.environ.copy()
        env["PYTHONPATH"] = str(Path(__file__).resolve().parent.parent) + os.pathsep + env.get("PYTHONPATH", "")
        self.process = subprocess.Popen([sys.executable, "-m", "oled_guard.watchdog", str(journal)],
                                        stdin=subprocess.PIPE, stdout=subprocess.PIPE,
                                        stderr=subprocess.DEVNULL, env=env, start_new_session=True)
        ready, _, _ = select.select([self.process.stdout], [], [], 5)
        if not ready or self.process.stdout.readline() != b"READY\n":
            self.close()
            raise RuntimeError("Parlaklık kurtarma süreci hazır olmadı.")

    def close(self):
        if self.process is not None:
            self.process.stdin.close()
            self.process.stdout.close()
            # On restoration failure the watchdog must remain alive until parent exit.
            self.process = None


class BrightnessTransaction:
    def __init__(self, backend, journal, guard=None):
        self.backend = backend
        self.journal = Path(journal)
        self.guard = guard or Watchdog()
        self.started = False

    def begin(self):
        if self.journal.exists():
            raise RuntimeError("Kurtarma tamamlanmadı. Donanım parlaklığını yeniden değiştirmeden önce aygıtı bağlayıp uygulamayı yeniden açın.")
        originals = self.backend.capture()
        if not originals:
            raise RuntimeError("Parlaklık aygıtı bulunamadı.")
        atomic_json(self.journal, originals)
        self.started = True
        try:
            self.guard.start(self.journal)
            for item in originals:
                self.backend.set(item["name"], 0)
        except Exception:
            try:
                self.restore()
            except Exception:
                pass
            raise

    def restore(self):
        if not self.started:
            return
        restore_journal(self.backend, self.journal)
        self.started = False
        self.guard.close()
