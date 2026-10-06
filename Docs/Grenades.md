# Grenades

Press **F** to throw a frag grenade along the camera's aiming direction. Each
player starts with two grenades, restored on respawn or a new round. G retains
its existing armour-plate binding. Throwing is blocked while dead, using an
objective, outside live match play, or while the cursor is unlocked.

Frag tuning: three-second fuse, 28 m/s forward velocity plus 7 m/s lift,
20 m/s² gravity, 0.45 bounce restitution and 0.8-second throw cooldown.
Explosions deal 120 damage within two metres, falling linearly to zero at
seven metres. A ray to the target's collider aim point tests cover. Team damage
and self damage are disabled, matching the current gunfire rules. Offline
grenades damage entities tagged Enemy through their existing TakeDamage method.

## Architecture

- `GrenadeRules.cs` contains immutable grenade definitions, damage falloff,
  per-life inventory and fixed-step projectile rules. It has no engine dependency.
- `MultiplayerSession.Grenades.cs` adapts those rules to input, physics, networking,
  particles and existing hit, death and scoring paths. It is automatically active
  wherever the existing MultiplayerSession component is attached.
- The host accepts only a throw direction. It derives launch position from the
  participant, checks life/phase/objective state, spends its own inventory and
  simulates a swept ray at 120 Hz. A radius margin handles grenade wall contact;
  this is a ray approximation rather than a native sphere cast.
- Clients receive complete projectile frames at 20 Hz, explosion events and their
  own inventory updates. They never decide hits, damage, fuse duration or ammo.
  Host projectile visuals update every frame; client visuals use the received
  positions. Effects and projectiles are destroyed on expiry/session shutdown.
- Launches use server-side participant positions, which currently inherit the
  game's existing client-authored player transform replication.

Networking uses **protocol 13**; rebuild both peers. Channels 16–19 are reserved
for grenade throws, projectile frames, explosions and inventory respectively.
Already thrown grenades continue after the thrower's death, but expire without
damage when play ends. Disconnecting removes that player's active grenades.
Late joiners receive active projectiles in the next frame.

To add a grenade type, add another GrenadeDefinition and a host-approved identity
selection in the throw adapter. Keep all tuning on the host; do not transmit damage
or fuse values in client requests. Smoke/flash gameplay needs separate effect rules.

## Assets and verification

`Tools/create_grenade_assets.py` reproducibly creates and registers the frag prefab,
three explosion layers (flash, sparks and expanding smoke), materials and a soft
procedural particle texture. No player-scene references need manual wiring.

```powershell
python Tools/create_grenade_assets.py
dotnet build CoD.Scripts.csproj -c Release --no-restore
dotnet run --project Tests/CoD.GameplayTests.csproj -c Release
python Tools/run_grenade_smoke.py C:/Users/Cameron.Airlie/dev/PlutoGE/out/build/msvc-nvidia/runtime/RelWithDebInfo
```

The independent tests check inventory limits, cooldown/reset, falloff, frame-rate
independence, fuse expiry, bounce and invalid input. The isolated native Vulkan
smoke scene checks host throwing, native grenade meshes, explosion particles,
enemy damage, friendly immunity, cover, eliminations/scoring and effect cleanup.
It compiles test-only partial methods into Tools/Native/Grenades; shipped scripts
contain no smoke probe. A two-peer playtest remains the final multiplayer check.
