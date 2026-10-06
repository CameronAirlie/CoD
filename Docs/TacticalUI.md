# Tactical interface

The supplied tactical FPS style sheets guide the palette, typography, outlines,
spacing and information hierarchy. The project retains its own names, weapons
and functional controls. The title screen, pause menu, inventory, gameplay HUD,
objective markers and player nameplates now share one visual system.

## Architecture

- `Assets/UI/theme.tokens.json` owns the reference palette: near-black background,
  charcoal panels, thin grey borders, amber actions, white text and blue/red teams.
- `theme.rcss.in` owns reusable panels, headings, buttons, tabs, text fields,
  key prompts, icons, stats, metadata and progress bars, including interaction states.
- Screen-specific `*.rcss.in` files own composition. They reference shared tokens
  through Python Template substitution, rather than unsupported runtime CSS variables.
  The generated `*.rcss` files are checked in so the game has no Python dependency.
- RML documents own semantic layout and retain existing controller element IDs.
- Existing controller scripts remain responsible for their own presentation state.
  Inventory weapon cards read actual `WeaponCatalog` stats and use `SelectWeaponSlot`;
  they subscribe/unsubscribe to `WeaponChanged`. Reordering and supply usage remain
  owned by InventoryMenu. The HUD's compass helpers live in the engine-independent
  `TacticalUi` class and update text only when the displayed heading changes.

Run `python Tools/build_ui_theme.py` after changing tokens or a stylesheet template.
Avoid editing generated RCSS files directly. Screen resource metadata and project
registration are updated by this tool. New screens should link theme.rcss before
their own stylesheet and compose the existing `ui-*` classes.

## Content and engine compatibility

The title artwork is an original background inspired by the reference's operator
composition; it contains no baked interface. All labels and controls remain native
RmlUi elements. The menu is intentionally limited to the working game modes,
host/join actions and connection fields. No inactive campaign, store, attachment
or settings controls were added. The HUD preserves ADS crosshair hiding, hit
feedback, deaths, grenade inventory, scoreboards and objective presentation.

Fonts are bundled locally with their licenses from the primary Google Fonts sources:
[Rajdhani](https://github.com/google/fonts/tree/main/ofl/rajdhani) and
[Inter](https://github.com/google/fonts/tree/main/ofl/inter).
The shared stylesheet declares fonts in comments because PlutoGE scans these
declarations itself, while RmlUi does not implement CSS @font-face parsing.
Heading/display aliases use Rajdhani Bold/Regular; body text uses Inter.

The Vulkan RmlUi texture loader requires uncompressed TGA, so runtime portraits,
icons and menu artwork use that format. PNG copies are retained for previewing.
RmlUi image references use `UI/Weapons/...` and `UI/Artwork/...`, matching its
image URL resolution from the project's asset root. UI uses native border
syntax without CSS `solid`, explicit block elements and native font effects.

`Tools/render_ui_weapons.py` produces transparent portraits from the existing
editable Blender rigs with hands hidden; it leaves the models unchanged.
`Tools/create_ui_icons.py` creates matching outline icons from vector paths.
`Tools/convert_ui_art.py` converts the checked-in background to native TGA.

## Validation and previews

```powershell
python Tools/build_ui_theme.py
dotnet build CoD.Scripts.csproj -c Release --no-restore
dotnet run --project Tests/CoD.GameplayTests.csproj -c Release
python Tools/build_ui_preview.py C:/Users/Cameron.Airlie/dev/PlutoGE/out/build/msvc-nvidia 'C:/Program Files/Microsoft Visual Studio/18/Community/VC/Auxiliary/Build/vcvars64.bat'
python Tools/run_ui_smoke.py C:/Users/Cameron.Airlie/dev/PlutoGE/out/build/msvc-nvidia/runtime/RelWithDebInfo
```

The preview tool uses the engine's actual RmlUi library and FreeType fonts,
checks control bounds and native mouse click delivery at 960×540, 1280×720 and
1920×1080, and rejects stylesheet errors. It renders native geometry to PNG using
a small CPU triangle renderer. Images are saved under ignored `Tools/Native/UI`.
They show isolated UI with representative data, not a screenshot of active combat.
The smoke runner loads the real title scene and shipped script assembly in Vulkan
and checks font registration and startup diagnostics. Gameplay tests cover compass
axes, invalid vectors and heading wraparound. Final combat readability and inventory
interactions should still be checked in a live multiplayer playtest.

## Generated background provenance

Built-in imagegen was used. Final assets:
`Assets/UI/Artwork/menu-background.png` and `menu-background.tga`.

Final prompt:

> Use case: stylized-concept. Asset type: original main-menu background for a tactical FPS game, 16:9 wide composition. Scene: a dark abandoned industrial street, worn concrete and steel, subtle haze, a few distant amber lights. Subject: one modern tactical operator viewed from behind and slightly to the side, helmet, dark olive body armour, small backpack, rifle held lowered; three-quarter body visible. Style: cinematic realistic game key art, restrained and immersive, close to the soldier menu artwork in tactical FPS UI style guides. Composition: operator around 65% across the frame; leftmost 40% very dark, quiet and low detail for a menu; lower right also subdued for a small briefing panel. Palette: near-black charcoal #090B0D, desaturated olive and steel, sparse amber #FFB31A highlights. No text, no HUD, no logos, no watermarks, no faction symbols, no neon, no explosions. Render only background artwork; interface will be native code.
