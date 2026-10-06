# Player settings

Borderless fullscreen applies to the standalone game. The native script bridge ignores fullscreen changes during play-in-editor, keeping the editor window mode independent of saved game preferences.

Open SETTINGS from the title or pause menu. Five tabs organize preferences: Display (VSync, borderless fullscreen, and field of view), Graphics (quality), Audio (volumes), Interface (scale), and Controls (mouse sensitivity). Changes apply immediately; BACK closes the page and RESTORE DEFAULTS resets all preferences.

PlayerPreferences is a versioned, validated model. PlayerSettings owns persistence and application; GameGraphicsSettings binds document-scoped controls. Preferences are stored in LocalApplicationData/PlutoCombat/settings.json with temporary-file replacement. Existing graphics.json preferences migrate on first load. Unsupported display changes keep the previous preference and display a status message. Scene hosts reapply graphics quality on scene creation.

Sound offers master, weapons/effects, and combat feedback gains on existing game cues. New cues should use EffectsGain or FeedbackGain; continuous emitters need their own authored-volume binding. Mouse sensitivity multiplies the controller's authored value and preserves the ADS multiplier. Interface size uses the engine's `UISettings.TrySetInterfaceScale` and RCSS `dp` units. Screen UI roots remain viewport-sized and corner widgets use ordinary edge anchors. No element transform list is required. Fixed UI sizes, padding, text, and slider geometry use `dp`; media breakpoints use physical `px`. Crosshair arm geometry remains `px`, spread remains `vh`, and directional cues retain viewport coordinates. World-space UI uses a separate engine context with dp ratio 1, so nameplates and objective markers are unaffected. Keep `.rcss.in` sources and generated outputs synchronized with `Tools/build_ui_theme.py`.

Engine and game ScriptCore assemblies must be updated together. The preference is reapplied on scene creation and immediately when its slider changes. The engine owns the live scale; the game owns persistence. See [the engine RML authoring guide](C:/Users/cam/dev/PlutoGE/docs/RMLUI_AUTHORING.md) for markup, units, controls, and engine-specific extensions.

Add preferences to PlayerPreferences and its validation, apply them in PlayerSettings, and add tab markup and bindings to the presenter. Keep title and pause settings markup synchronized. Run dotnet run --project Tests/Settings/Settings.Tests.csproj after building CoD.Scripts.csproj. In-game layout and native display behavior require a running engine.

Field of view is a saved base vertical camera FOV, default 90 degrees, with a slider from 80 to 110 in one-degree steps. PlayerController applies it on creation and uses it for normal camera transitions. Authored ADS zoom and the sprint FOV boost remain active. Old settings files without this field use 90; invalid values are clamped or replaced with the default.
