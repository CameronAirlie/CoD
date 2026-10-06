# CoD agent guidance

For RML/RCSS or UI-controller work, read `Docs/PlayerSettings.md` and the PlutoGE engine's `docs/RMLUI_AUTHORING.md` / `docs/RMLUI_REFERENCE.md` (engine source at `C:/Users/cam/dev/PlutoGE`). Screen sizing uses engine `UISettings.TrySetInterfaceScale` and dp units. Keep HUD roots viewport-sized, corner widgets edge-anchored, crosshair geometry in physical/viewport units, and world UI independent. Do not reintroduce manual scale-transform lists.

Edit `.rcss.in` templates together with generated `.rcss` files, or run `Tools/build_ui_theme.py`. New scripts/UI resources need project asset registration. Rebuild the engine, ScriptCore, and `CoD.Scripts.csproj` together after engine API changes. Run `Tests/Settings/Settings.Tests.csproj` for preference and UI wiring checks. Preserve unrelated staged scene, asset, and build edits.
