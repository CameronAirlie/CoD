# Original stylized weapon sources

`ar.blend`, `pistol.blend` and `lmg.blend` contain separate weapon/hand models and
five actions each: Idle, Draw, Fire, Reload and Sprint. These models and simple
gloved hands were generated specifically for this project; they do not include
geometry from the existing VAL asset.

The six-joint hierarchy is Root > Weapon > Slide, Magazine, RightHand, LeftHand.
Moving components and hand geometry have skin influences matching those joints.
Each weapon is also exported as a skinned GLB. PNGs provide studio, hip and aim
views, using the game overlay camera's vertical field of view.

Runtime assets are generated under `Assets/Weapons`. See `Docs/Weapons.md` for
controls, architecture, regeneration and verification. Edit the Blender generator
to make persistent procedural changes; save hand-edited variations separately.
