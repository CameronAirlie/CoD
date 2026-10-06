# First-person hand system

The player owns `FirstPersonHands`, which owns one active `WeaponHandBinding`.
The binding lives on the animated camera-child rig and provides that weapon's
poses, graph parameter names and reload event name. `HandMotion` computes local
sway, bob, aim and sprint offsets independently of PlutoGE. The animation graph
continues to own skeletal animation and blends.

Main, Foundry and the Player prefab use a VAL binding with the existing authored
idle, fire and reload clips. Sprint uses a lowered procedural pose. Bindings can
enable authored sprint and aim animation through optional Boolean parameters;
blank names disable those graph channels. Unmigrated scenes retain the previous
controller presentation path.

## Add a weapon

1. Start with `Assets/SourceModels/FirstPersonHands/SharedHands.blend`, or a rig
   with matching bones. Author hands and weapon together for a correct grip.
2. Import its GLB in PlutoGE and create a graph with an idle state, a fire trigger
   and reload Boolean layer. Give the reload clip a commit event, normally
   `ReloadFinish`. Configure non-looping fire/reload layers and blend-out.
3. Place the rig under the player's camera. Add `WeaponHandBinding` on the same
   entity as its `AnimationComponent`; set its unique `weaponId`, animator,
   local hip/aim/sprint positions and matching graph names. A missing animator
   allows procedural motion but cannot play skeletal animation.
4. Keep additional weapon roots inactive until equipped. All roots must share
   the camera coordinate space. Keep the existing `Weapon` render tag and overlay
   camera configuration to avoid world-camera clipping and duplicate rendering.
5. Call `PlayerController.EquipHands(binding)` from the future weapon selection
   system. It cancels reload before changing presentation. The old root is
   hidden, its animation is reset and its event subscription removed; the new
   root is reset to its own pose. Re-equipping the current binding does nothing.

This API switches **hand presentation**. Magazine state, weapon statistics,
sounds, muzzle flash and server weapon validation still belong to the existing
gameplay implementation. A future multi-weapon inventory must switch those
together and replicate a validated weapon ID; changing a hand binding alone
does not change damage or ammunition. The current multiplayer protocol remains
compatible.

## Lifecycle and animation events

The equipped rig relays events to the controller. Only its configured commit
event can finish an active reload; the controller's timer remains a fallback.
Death cancels reload and hides the rig. Respawn restores it and resets its pose
and animation. Cursor release settles motion and clears movement flags. Disposal
disconnects rig events. Do not switch rigs by setting roots active directly:
use `EquipHands` so cancellation and subscriptions are handled together.

## Validation

```powershell
dotnet build CoD.Scripts.csproj -c Release
dotnet run --project Tests/CoD.GameplayTests.csproj -c Release
```

Tests cover pose convergence, sprint priority, equip motion reset, invalid input,
bounded cursor spikes, stationary smoothing at different frame rates and bindings
in both scenes and the prefab, alongside existing gameplay regressions.

Editor checks still required: hip/ADS grip alignment, firing/reload blending,
sprint cancellation, death/respawn visibility, pause/resume, and switching between
two authored rigs during a reload. Native animation and rendering cannot be
verified by the engine-independent tests.
