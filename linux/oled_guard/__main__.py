import sys
from . import __version__
if "--version" in sys.argv:
    print("OLED Guard", __version__)
else:
    from .app import main
    raise SystemExit(main())
