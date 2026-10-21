#!/bin/bash
set -euo pipefail
cd "$(dirname "$0")/.."
if [[ "${OLED_GUARD_TEST_INSTALLED:-0}" == 1 ]]; then
  export PYTHONPATH=/usr/lib/oled-guard
else
  export PYTHONPATH="$PWD/linux${PYTHONPATH:+:$PYTHONPATH}"
fi
export PYTHONWARNINGS=ignore::DeprecationWarning
python3 -m unittest discover -s linux/tests -p test_core.py -v
oled_test_dir=$(mktemp -d -t oled-xvfb-XXXXXXXX)
export DISPLAY=
for oled_display_number in {99..150}; do
  if [[ ! -e "/tmp/.X${oled_display_number}-lock" && ! -e "/tmp/.X11-unix/X${oled_display_number}" ]]; then
    export DISPLAY=":$oled_display_number"
    break
  fi
done
[[ -n "$DISPLAY" ]]
Xvfb "$DISPLAY" -screen 0 1280x900x24 -nolisten tcp >"$oled_test_dir/server.log" 2>&1 &
oled_xvfb_pid=$!
trap 'kill "$oled_xvfb_pid" 2>/dev/null || true; rm -f "$oled_test_dir/display" "$oled_test_dir/server.log"; rmdir "$oled_test_dir"' EXIT
for oled_wait in {1..50}; do
  if python3 -c 'from Xlib.display import Display; Display().close()' 2>/dev/null; then break; fi
  sleep 0.1
done
if ! python3 -c 'from Xlib.display import Display; Display().close()' 2>/dev/null; then
  cat "$oled_test_dir/server.log"
  exit 1
fi
export GDK_BACKEND=x11 XDG_SESSION_TYPE=x11 NO_AT_BRIDGE=1
dbus-run-session -- python3 -m unittest discover -s linux/tests -p test_gui.py -v
