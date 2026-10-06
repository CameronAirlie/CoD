# Shared first-person hands

`SharedHands.blend` is an authoring template derived from this project's existing
`animation_low-poly_val.glb`. It retains its armature, weighted arms, gloves,
sleeves and eight original actions. VAL geometry and the environment are removed.
The template carries the source asset's licensing requirements; it is not a new
independently licensed model.

The playable VAL continues to use its existing imported mesh and clips. The
template is for authoring additional weapons, not a replacement runtime import.
Preserve bone names, bind pose and vertex weights. Keep weapon geometry separate
from the hand meshes, attach it to the appropriate existing weapon bones, and
animate the hands and weapon together. Export the armature, hands and new weapon
as glTF/GLB, then import through PlutoGE to generate native meshes and clips.

Regenerate the template from the project root:

```powershell
& 'C:/Program Files/Blender Foundation/Blender 5.1/blender.exe' --background --python Tools/inspect_hand_rig.py -- D:/PlutoProjects/CoD --create-template
```

Use this command only when intentionally replacing the template; save your
authored variations under different filenames.
