import sys
from . import __version__
if "--version" in sys.argv:
    print("Caffeine For OLED", __version__)
else:
    from .app import main
    raise SystemExit(main())
