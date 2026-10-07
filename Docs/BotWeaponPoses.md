# Third-person weapon holds

Enemy and RemotePlayer use separate Rifle and Pistol upper-body layers over the
existing UAL locomotion. Rifle remains the default. Select `weaponPose` on the
`EnemySoldierBot` component, or call `SetWeaponPose(0)` for rifle / `SetWeaponPose(1)`
for pistol. The script switches the M16/P-12 visuals, layer weights and support
grip offset together. This is a visual hold selector; it does not change damage,
fire rate, or player inventory/network loadout rules.

The rifle has protracted shoulders, a bent support elbow and an upward-facing
support palm under the rear handguard. The pistol uses a closer two-handed grip.
Both firing grips are placed in the palm instead of at the wrist joint. The
firing wrist points toward the receiver with the thumb above the grip, rather
than bending the entire hand downward. The support IK target is parented to the
firing-hand socket so it follows the gun in editor preview without bot scripts.
Its local rotation stores the grip orientation; IK `RotationWeight` is 1 so the
support hand follows the target's world rotation as well as its position.
Rifle/pistol switching applies the corresponding local target pose once.
The authored poses retain at least 4 cm of rifle reach margin and 12 cm of pistol
reach margin throughout their clips. Neither the bind pose nor arm lengths,
skin weights or leg animation is changed. Reloads release support-hand IK.

The editable source is
`Assets/SourceModels/Solider/Rigged/Soldier_Bot_WeaponPoses.blend`; the original
review and animation blend files are preserved. Rebuild from the original:

```powershell
& 'C:/Program Files/Blender Foundation/Blender 5.1/blender.exe' --background Assets/SourceModels/Solider/Rigged/Soldier_Bot_Animations.blend --python Tools/build_bot_weapon_poses.py
python Tools/build_bot_animation_graph.py
python Tools/install_bot_weapon_poses.py
dotnet build CoD.Scripts.csproj -c Release
```

The Blender exporter checks every sampled wrist target against the unchanged
two-bone arm length, with at least 2.5 cm of spare reach and less than 4 mm of
solver error. Measured values and hand-local grip offsets are recorded in
`Assets/Bots/Soldier/manifest.json`. The installer updates both prefabs and
registers new assets without rebuilding unrelated prefab components.

Run `python Tools/run_bot_smoke.py <runtime-directory>` to exercise both real
prefabs in the native runtime. It checks rifle/pistol switching, idle, aim, fire,
jog and sprint, arm reach, wrist/target contact and matching weapon visibility.
It also checks contact against markers parented to the actual weapon, and runs
both prefabs with the bot script removed to cover script-independent preview.
