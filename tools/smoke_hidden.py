"""Launch the tray app on a non-input desktop and check startup/liveness only."""

import json
import sys
import time
from pathlib import Path

SKILL = Path.home() / ".codex" / "skills" / "windows-hidden-desktop-gui" / "scripts"
sys.path.insert(0, str(SKILL))

from hidden_desktop import HiddenDesktop, current_desktop_name  # noqa: E402


root = Path(__file__).resolve().parents[1]
exe = root / "src" / "GHubTray.App" / "bin" / "Release" / "net10.0-windows10.0.19041.0" / "win-x64" / "GHubTray.App.exe"
before = current_desktop_name()

with HiddenDesktop() as desktop:
    process = desktop.spawn(exe, cwd=exe.parent)
    try:
        time.sleep(3)
        result = {
            "caller_before": before,
            "caller_during": current_desktop_name(),
            "desktop": desktop.name,
            "pid": process.pid,
            "alive_after_3s": process.alive(),
            "windows": [window.__dict__ for window in desktop.windows(pid=process.pid)],
        }
    finally:
        if process.alive():
            process.terminate(0)
            process.wait(3000)
        result["alive_after_cleanup"] = process.alive()
        process.close()

result["caller_after"] = current_desktop_name()
result["desktop_closed"] = desktop.close_evidence
print(json.dumps(result, ensure_ascii=False, indent=2))
