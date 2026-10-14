# FPS controls

FPS input is authored in `Assets/Input/fps.plutoinput`. `PlayerController` exposes
an Input Mapping asset field; its default is the FPS asset. `GameInput` shares that
mapping with local gameplay, multiplayer command generation, HUD, and menu hotkeys.
The asset is registered and always included in cooked builds. The vehicle retains
its separate `car.plutoinput` mapping.

| Action | Keyboard / mouse | Controller (standard Xbox labels) |
|---|---|---|
| Move / look | WASD / mouse | Left / right stick |
| Fire / aim | Left / right mouse | RT / LT |
| Jump / sprint / crouch or slide | Space / Shift / C | A / left-stick click / B |
| Reload / interact or hold objective | R / E | X |
| Cycle weapon | Q (1–3 select slots) | Y |
| Throw / cycle throwable | G / T | RB / LB |
| Health / armour | H / J | D-pad up / down |
| Inventory / pause / scoreboard | I or B / Escape / Tab | D-pad right / Start / Back |
| Spectate previous / next | Q / E | LB / RB |
| Spectator team Alpha / Bravo | F1 / F2 | D-pad left / right |
| Warmup loadout Assault / Medic / Defender | 1 / 2 / 3 | D-pad left / up / down |

Bindings can be changed in the input asset editor. Both devices can be used without
switching modes. Stick axes use a 0.15 dead zone; analog movement retains magnitude
and diagonal input is limited to unit length. Mouse look retains pixel deltas and
the saved mouse sensitivity. Stick look uses the player's authored
`controllerLookSpeed` (180 degrees per second by default) and the ADS multiplier.
Gameplay remains blocked while the cursor is released for menus.

Controller bindings open/close inventory and pause menus. The existing menu forms
still use their mouse/keyboard interaction; controller focus navigation is not
implemented by this gameplay migration.

Run `dotnet run --project Tests/Input/Input.Tests.csproj -c Release` for asset,
action coverage, dual-device binding, and cooked inclusion checks. Verify stick
feel, mouse speed, hotplugging, ADS, slide, objective holds, and menu gating in a
running engine with a controller attached.
