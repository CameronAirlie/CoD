# Player settings

Borderless fullscreen applies to the standalone game. The native script bridge ignores fullscreen changes during play-in-editor, keeping the editor window mode independent of saved game preferences.

Open SETTINGS from the title or pause menu. Five tabs organize preferences: Display (VSync, borderless fullscreen, and field of view), Graphics (quality), Audio (volumes), Interface (scale), and Controls (mouse sensitivity and controller aim assist). Changes apply immediately; BACK closes the page and RESTORE DEFAULTS resets all preferences.

PlayerPreferences is a versioned, validated model. PlayerSettings owns persistence and application; GameGraphicsSettings binds document-scoped controls. Preferences are stored in LocalApplicationData/PlutoCombat/settings.json with temporary-file replacement. Existing graphics.json preferences migrate on first load. Unsupported display changes keep the previous preference and display a status message. Scene hosts reapply graphics quality on scene creation.

Sound offers master, weapons/effects, and combat feedback gains on existing game cues. New cues should use EffectsGain or FeedbackGain; continuous emitters need their own authored-volume binding. Mouse sensitivity multiplies the controller's authored value and preserves the ADS multiplier. Interface size uses the engine's `UISettings.TrySetInterfaceScale` and RCSS `dp` units. Screen UI roots remain viewport-sized and corner widgets use ordinary edge anchors. No element transform list is required. Fixed UI sizes, padding, text, and slider geometry use `dp`; media breakpoints use physical `px`. Crosshair arm geometry remains `px`, spread remains `vh`, and directional cues retain viewport coordinates. World-space UI uses a separate engine context with dp ratio 1, so nameplates and objective markers are unaffected. Keep `.rcss.in` sources and generated outputs synchronized with `Tools/build_ui_theme.py`.

Engine and game ScriptCore assemblies must be updated together. The preference is reapplied on scene creation and immediately when its slider changes. The engine owns the live scale; the game owns persistence. See [the engine RML authoring guide](C:/Users/cam/dev/PlutoGE/docs/RMLUI_AUTHORING.md) for markup, units, controls, and engine-specific extensions.

Add preferences to PlayerPreferences and its validation, apply them in PlayerSettings, and add tab markup and bindings to the presenter. Keep title and pause settings markup synchronized. Run dotnet run --project Tests/Settings/Settings.Tests.csproj after building CoD.Scripts.csproj. In-game layout and native display behavior require a running engine.

Field of view is a saved base vertical camera FOV, default 90 degrees, with a slider from 80 to 110 in one-degree steps. PlayerController applies it on creation and uses it for normal camera transitions. Authored ADS zoom and the sprint FOV boost remain active. Old settings files without this field use 90; invalid values are clamped or replaced with the default.

Controller aim assist is a saved strength from 0 to 1, shown as 0–100% in five-percent steps. The default is 50%; 0% disables it. Older settings files use 50%, finite out-of-range values clamp, and non-finite values fall back to 50%. Restore Defaults resets this preference too. The setting is adjustable with mouse or controller in both title and pause menus.

Assist is gentle ADS friction within four degrees and 35 world units of a living, visible enemy. The strongest slowdown is 25% at 100% strength (12.5% at the default), fading smoothly to zero at the cone edge. It scales only active gamepad stick motion while the controller ADS binding is held. Any simultaneous mouse look suppresses it. Mouse sensitivity and shot rays are unchanged; there is no snap, automatic tracking, or stationary-stick movement. Multiplayer targets use participant team/life state and child-hitbox ownership; offline targets must be living combat actors with the enemy tag. Cover, smoke and flash blindness block assist. Network authority still owns hit detection and damage.

`Tests/Input/Input.Tests.csproj` includes aim-assist math, input-device separation, team eligibility, preference migration/validation/roundtrip, and synchronized Controls markup checks. In-engine target visibility and controller feel still require a hardware playtest.

Controller menu navigation now comes from PlutoGE.ScriptCore; the project facade preserves existing callers and GameInput supplies remapped menu frames. Prompt classes follow Input.UsingController (last meaningful input), rather than a connected-but-idle gamepad. Use Input.ActiveDevice or Input.ActiveGamepad for project labels and device-specific presentation.

Developers can edit the meaning of Low/Medium/High/Ultra in the engine editor's
**View > Quality Settings** pane. Shadow ceilings/method policy and allowed effect
groups are project definitions, saved with **File > Save Project** (Ctrl+S) and
included in exports. CoD still saves the player's selected tier in settings.json;
applying that tier resolves the current project definition. Reapply a tier after
editing its definition to refresh an existing preview. See the engine's
`docs/GraphicsPresets.md` for effect scope and limits.

Control prompts resolve action bindings from GameInput's configured input asset.
Use `GameInput.Prompt("Interact")` for dynamic plain labels, or add a stable ID and
`data-input-prompt="{MenuAccept} SELECT"` to an RML leaf. UI controllers call
GameInput.RefreshPrompts on the document; the engine formats only the active
device's bindings, escapes RML text and restores labels after DOM recreation.
No literal keys/buttons or duplicate KBM/controller spans are needed. An unbound
action on the active device displays UNBOUND. Configure/reload the input asset to
pick up asset edits; binding changes within the active map refresh automatically.
