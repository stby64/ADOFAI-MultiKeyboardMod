# ADOFAI MultiKeyboardMod

Unity Mod Manager mod for A Dance of Fire and Ice that routes two physical keyboards to separate local multiplayer players.

## Current Behavior

- Archon keyboard (`VID_0416 / PID_9258`) is player 1 by default.
- Corsair keyboard (`VID_1B1C / PID_1BC6`) is player 2 by default.
- Use the in-mod button `2P 키보드 분리 강제 적용` after entering a level.
- The mod forces ADOFAI local multiplayer input to:
  - P1: `keyboardLeft`
  - P2: `keyboardRight`
- Raw Input is used to distinguish physical keyboards.
- Chord/simultaneous key handling is stabilized with per-frame press caching.
- ESC pause open/close handling is patched for the forced split mode.

## Status

Working for official local multiplayer usage with the tested keyboards. Custom level support is a future target and likely requires patching the custom level load/restart flow.

## Build

Run:

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

The build output is written to:

```text
dist\MultiKeyboardProbeClean
```

Copy that folder's contents into:

```text
C:\Program Files (x86)\Steam\steamapps\common\A Dance of Fire and Ice\Mods\MultiKeyboardProbeClean
```

## Notes

This project currently targets the local ADOFAI install path used during development:

```text
C:\Program Files (x86)\Steam\steamapps\common\A Dance of Fire and Ice
```

If the game is installed elsewhere, update `build.ps1`.
