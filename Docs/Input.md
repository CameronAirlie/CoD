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

Menus use a Call of Duty-style controller flow alongside the existing native
mouse/keyboard controls:

| Menu action | Controller |
|---|---|
| Navigate cards/options | Left stick or D-pad |
| Select / equip / buy / use | A |
| Back / close / cancel a move | B |
| Settings tabs | LB / RB |
| Adjust selected slider/toggle/preset | Left / right |
| Resume from pause | Start |
| Pick up a supply slot to reorder | X; move to a slot and press A to place |
| Open inventory in gameplay | D-pad right; inside inventory it navigates |

Navigation skips hidden tabs, unavailable purchases and unowned equipment, keeps
selection visible by scrolling, and uses a border/fill highlight separate from
native keyboard focus. A held stick/D-pad waits 350 ms before repeating every
100 ms, using an unscaled clock so paused menus remain usable. Stick activation
uses 0.55 with a 0.30 release threshold. Buttons must be released when a menu or
sub-menu opens, and after dismissal before gameplay resumes; accepting a menu
cannot also jump, reload, fire or throw a grenade. Restart, leave-match and quit
use a confirmation with Cancel selected first.

On the title screen, select the callsign/address field and press A to open the
controller keyboard. A types the selected key, X deletes, Y inserts a space,
LB/RB move the caret, Start commits and B cancels. CAPS, CLEAR, DONE and CANCEL
are also selectable buttons. Callsigns retain the 20-character limit; host
addresses retain the 253-character limit and support hostname/IPv4/IPv6 syntax.
Normal native text entry is still available with keyboard/mouse. Prompts use
standard Xbox button labels (the same physical positions on other controllers).

All `Menu*` bindings live in the FPS input asset. Keep these actions when authoring
an alternate player mapping. Controller navigation filters controller bindings
from that map; keyboard/mouse navigation remains with RmlUi to avoid duplicate
Enter/arrow handling. The shared `ControllerMenu` invokes the same actions as
mouse clicks without requiring changes to the native RmlUi bridge.

Run `dotnet run --project Tests/Input/Input.Tests.csproj -c Release` for asset,
action coverage, dual-device bindings, cooked inclusion, navigation/availability,
repeat timing, slider limits, text editing and input-release guards. The suite
also runs the actual controller/keyboard/confirmation code with simulated native
input and DOM callbacks. Run `Tests/Settings/Settings.Tests.csproj` for preference
and RML settings checks. Native interface-scale tests cover Vulkan/OpenGL; menu
render captures were inspected at 0.75, 1 and 1.25 scale. Hardware feel, hotplugging
and scene transitions still need a running game with a controller attached.
