# Weapon loadouts

Main, Foundry and the Player prefab now carry three original stylized weapons.
Each uses its own six-bone hand/weapon rig, native mesh, animation graph, draw,
idle, fire, reload and sprint clips. Editable Blender files and GLB exports are
in `Assets/SourceModels/Weapons`; runtime assets are in `Assets/Weapons`.

| Slot | Weapon | Trigger | Magazine | RPM | Damage | Range | Reload animation |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 1 | AR-24 assault rifle | Automatic | 30 | 660 | 28 | 180 | 2.1 seconds |
| 2 | P-12 pistol | Semi-automatic | 12 | 360 | 34 | 90 | 1.5 seconds |
| 3 | MG-60 machine gun | Automatic | 60 | 780 | 23 | 200 | 3.4 seconds |

During play, **1 / 2 / 3** select slots and **Q** cycles them. **R** reloads; hold
right mouse to aim. **H / G** still use health kits and armour plates. During
preparation/results, 1 / 2 / 3 select Assault, Medic or Defender as before.
All three classes carry all three weapons; their supplies differ and their next
spawn defaults to rifle, pistol or machine gun respectively.

Each weapon preserves its own loaded magazine when holstered. Reserve ammunition
is a generic shared inventory supply, consumed only when a reload commits.
Switching cancels unfinished reloads, preserves the outgoing magazine and blocks
firing/reloading until the incoming draw finishes. Old rig events cannot complete
the new weapon's reload. Reload clips commit at 86% of their visual duration;
the configured full duration remains a fallback if no event arrives. Death hides
the equipped rig and blocks switching; round/spawn reset refills all magazines.

## Architecture

- `WeaponCatalog`: immutable weapon identities and combat tuning used by both
  local gameplay and the host, including cooldown constraints across switches.
- `WeaponLoadout`: engine-independent slot selection and per-slot magazines.
- `PlayerController`: input, selected combat statistics, shared inventory
  transfers and the public `SelectWeaponSlot(int)` selection API.
- `FirstPersonHands` / `WeaponHandBinding`: one visible rig, local pose motion,
  animation parameters, active-rig event routing, shot audio and muzzle flash.
- `PlayerHud`: equipped name and slot controls, with preparation-phase hints.

Bindings register with their serialized owner from `OnCreate`. Their roots start
active so PlutoGE creates their managed instances; registration immediately hides
unselected roots, and selection begins once all three have registered. This
avoids depending on player/child script startup order. The weapon overlay camera
shares the main camera's position, uses a 65-degree vertical FOV and a 0.03 near
plane; the base camera excludes the `Weapon` tag.

## Multiplayer

Protocol **12** includes weapon identity in each shot request and grenade replication. The host rejects
unknown identities and uses its own catalog damage, range and firing rate.
Changing identities also retains the outgoing firing interval and applies the
incoming draw cooldown. The legacy VAL identity is accepted only in scenes
without the new loadout configuration. All peers need the rebuilt assembly.

As in the existing demo, magazine/reserve quantities and reload input are local;
this change does not add server ammunition accounting. Existing third-person
soldier models and bot weapons remain their authored assets. Weapon-specific
first-person visuals and animations belong to the local player; third-person
weapon swapping and a two-process network playtest are future validation/work.

## Authoring and regeneration

`Tools/create_weapons.py` creates the models and keys bone transforms in Blender.
`Tools/plutoge_blender_export.py` writes the engine's LPGM v5 and LPGC v5 formats
from Blender geometry and sampled animation. It preserves named joint channels,
skin influences, inverse bind matrices, materials and reload commit events.
The same scene is exported to GLB for a standard import workflow.

```powershell
& 'C:/Program Files/Blender Foundation/Blender 5.1/blender.exe' --background --python Tools/create_weapons.py -- D:/PlutoProjects/CoD
python Tools/install_weapon_assets.py D:/PlutoProjects/CoD
```

Regeneration replaces generated Blender files and assets. Keep manually authored
variants under separate names. The installer is idempotent and touches only its
generated rigs, their scene references and the weapon camera configuration.
It registers assets for packaging. Existing packaged builds must be re-exported.

To add a slot, extend `WeaponCatalog`, the controller's serialized rig references
and registration list, the installer and the HUD labels. Supply matching catalog
and animation durations. To replace a slot's model, retain its catalog identity
and bind a compatible graph with `Idle`, `Draw`, `Fire`, `Reload` and `Sprint`.

## Verification

```powershell
dotnet build CoD.Scripts.csproj -c Release
dotnet run --project Tests/CoD.GameplayTests.csproj -c Release
python Tools/run_weapon_smoke.py C:/Users/Cameron.Airlie/dev/PlutoGE/out/build/msvc-nvidia/runtime/RelWithDebInfo
```

The smoke runner builds a separate test assembly containing `WeaponSmokeProbe`,
leaving the shipped assembly unchanged, and runs the real runtime in a hidden
window. It verifies managed startup, visibility, initial/equip draw locks,
firing, magazine preservation, cancellation, stale events, reserve transfer,
death/respawn and reset. Its logs/result are under ignored `Tools/Native`.

`Tools/verify_weapon_assets.cpp` additionally uses the engine's actual native
mesh/clip/graph readers and evaluates five skinning poses per weapon. Blender
studio and hip/aim renders are saved beside the source files for visual review.
Compile and run that validator against the existing engine build with:

```powershell
python Tools/build_weapon_validator.py C:/Users/Cameron.Airlie/dev/PlutoGE/out/build/msvc-nvidia 'C:/Program Files/Microsoft Visual Studio/18/Community/VC/Auxiliary/Build/vcvars64.bat'
```

Final arena lighting, sound balance and host/client interaction need an editor
and two-process playtest.

Muzzle flashes use the shared `Particles/MuzzleFlash.plutoparticles` asset at
each weapon socket. Each shot emits one stationary, local-space billboard with
a 60 ms fade and an emissive, alpha-blended material. The weapon overlay camera
renders these particles before the shared temporal resolve; the base camera's
Weapon exclusion keeps them from being drawn twice. The native weapon smoke
test checks muzzle emission for the rifle, pistol and machine gun.
