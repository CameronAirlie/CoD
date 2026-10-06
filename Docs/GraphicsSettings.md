# Graphics settings

The title screen and pause menu include a GRAPHICS button that cycles Low, Medium, High and Ultra. Changes apply immediately and survive scene changes and restarts. Saved preferences live at `%LOCALAPPDATA%/PlutoCombat/graphics.json`; first launch uses High.

`GameGraphicsSettings` owns preference loading, saving and RML binding. The engine owns preset definitions and rendering policy. Add a `graphics-preset` button to custom menu documents before binding them. The game preview uses the same quality in editor Play mode.

Scripts can customise a native preset before applying it:

```csharp
if (GraphicsSettings.TryGetPreset(GraphicsPreset.Low, out var quality))
    GraphicsSettings.TryApplyQuality(quality with { Bloom = true, ShadowDistance = 50 });
```

The built-in menu saves its selected tier. A game that exposes individual overrides should save those values alongside the tier and apply them after loading the native preset. Display resolution and VSync are independent. Low/Medium reduce directional shadow work and skip expensive lighting and atmosphere passes; textures, local shadows, particles and mesh LOD remain authored.

Rebuild the native engine and ScriptCore together when updating the scripting API. The engine design, quality table and tests are documented in `PlutoGE/docs/GraphicsPresets.md`.
