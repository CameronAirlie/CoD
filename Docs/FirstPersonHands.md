# First-person hand system

The player owns `FirstPersonHands`, which owns one active `WeaponHandBinding`.
The binding lives on the animated camera-child rig and provides that weapon's
poses, graph parameter names and reload event name. `HandMotion` computes local
sway, bob, aim and sprint offsets independently of PlutoGE. The animation graph
continues to own skeletal animation and blends.

Main, Foundry and the Player prefab now use three generated weapon rigs with
their own draw, idle, fire, reload and sprint animations. See [Weapons](Weapons.md)
for loadout controls and gameplay integration. The existing VAL asset remains
available for legacy scenes. Blank parameter names disable optional channels.

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
4. Set the binding's owner to the player and its catalog identity. Generated
   roots start active so their scripts can register, then hide until selected.
   All roots must share the camera coordinate space. Keep the `Weapon` render
   tag and overlay camera configuration to avoid duplicate rendering.
5. Select through `PlayerController.SelectWeaponSlot(slot)`, which synchronizes
   gameplay and presentation and cancels reload before changing rigs. The old
   root is hidden and disconnected; the new root resets to its own pose.

`FirstPersonHands` owns presentation only. `WeaponLoadout` preserves magazines,
`WeaponCatalog` supplies shared local/host tuning, and the controller coordinates
selection. Shot requests now carry weapon identity with protocol 11. Rig bindings
own their shot audio and muzzle-flash references.

## Lifecycle and animation events

The equipped rig relays events to the controller. Only its configured commit
event can finish an active reload; the controller's timer remains a fallback.
Death cancels reload and hides the rig. Respawn restores it and resets its pose
and animation. Cursor release settles motion and clears movement flags. Disposal
disconnects rig events. Do not switch rigs by setting roots active directly:
use `SelectWeaponSlot` so cancellation and subscriptions are handled together.

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
