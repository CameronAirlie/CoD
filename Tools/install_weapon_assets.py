"""Install generated weapon prefabs/graphs and wire the playable scenes.

Run from any directory: python Tools/install_weapon_assets.py <project-root>.
Only this tool's generated assets and the three explicit scene bindings are changed.
"""
import json
import re
import sys
import uuid
from pathlib import Path

root = Path(sys.argv[1]).resolve()
assets = root / 'Assets'
manifest = json.loads((assets / 'Weapons/manifest.json').read_text())


def write(path, content):
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(content, encoding='utf-8', newline='\r\n')
    identity = uuid.uuid5(uuid.NAMESPACE_URL, 'cod-weapons/' + path.relative_to(root).as_posix()).hex
    Path(str(path) + '.plutometa').write_text(f'PLUTOASSET\t1\nID\t{identity}\nIMPORTER_VERSION\t1\n', encoding='utf-8')


def property_(name, kind, value):
    return f'PROPERTY\t{name}\t{kind}\t{value}\t0\n'


def component(entity, name, properties):
    return f'COMPONENT\t{entity}\t{name}\t1\n' + ''.join(property_(*p) for p in properties) + 'END_COMPONENT\n'


def rig(kind, entity, parent, owner):
    label = {'ar': 'AR-24 Assault Rifle', 'pistol': 'P-12 Pistol', 'lmg': 'MG-60 Machine Gun'}[kind]
    aim_y = '-0.074' if kind == 'pistol' else '-0.105'
    # Socket offsets use Weapon bone coordinates: its bind pose maps Y to
    # mesh Z and Z to negative mesh Y.
    muzzle = '0,-0.505,-0.005' if kind == 'pistol' else '0,-0.995,-0.014' if kind == 'lmg' else '0,-0.865,-0.014'
    text = f'ENTITY\t{entity}\t{parent}\t1\t{label}\t0.18,-0.15,-0.16\t0,0,0\t1,1,1\nTAGS\t{entity}\t1\tWeapon\n'
    text += component(entity, 'ScriptComponent', [('Source', 2, 'CoD.Scripts.WeaponHandBinding'), ('weaponId', 2, kind),
        ('owner', 9, owner), ('animator', 9, entity), ('muzzleFlash', 9, entity + 2), ('shotAudio', 9, entity),
        ('hipPosition', 3, '0.18,-0.15,-0.16'), ('aimPosition', 3, f'0,{aim_y},-0.10'),
        ('sprintPosition', 3, '0.22,-0.25,-0.08'), ('drawTrigger', 2, 'Draw'), ('sprintParameter', 2, 'Sprint')])
    text += component(entity, 'MeshComponent', [('Static', 4, 'false'), ('Visible', 4, 'true'), ('SubmeshIndex', 1, -1),
        ('MeshAssetReference', 2, f'project://Weapons/{kind}/{kind}.plutomesh'), ('UseGeneratedLods', 4, 'false')])
    text += component(entity, 'AnimationComponent', [('SourceAnimation', 2, f'project://Weapons/{kind}/{kind}.plutoanim'),
        ('AnimationGraph', 2, f'project://Weapons/{kind}/{kind}.plutoanimgraph'), ('CurrentClipIndex', 1, 0),
        ('Time', 0, 0), ('Speed', 0, 1), ('Playing', 4, 'true'), ('Looping', 4, 'true'), ('Autoplay', 4, 'true')])
    text += component(entity, 'SoundEmitterComponent', [('Clip', 2, 'project://Sounds/gun_shot.wav'), ('PlayOnAwake', 4, 'false'),
        ('Looping', 4, 'false'), ('Spatialized', 4, 'false'), ('Volume', 0, .8), ('Pitch', 0, 1)])
    text += f'ENTITY\t{entity + 1}\t{entity}\t1\tWeapon Socket\t0,0,0\t0,0,0\t1,1,1\n'
    text += component(entity + 1, 'SkeletonAttachmentComponent', [('TargetNodeIndex', 1, 1), ('JointName', 2, 'Weapon')])
    text += f'ENTITY\t{entity + 2}\t{entity + 1}\t1\tMuzzle Flash\t{muzzle}\t0,0,0\t1,1,1\nTAGS\t{entity + 2}\t1\tWeapon\n'
    text += component(entity + 2, 'ParticleSystemComponent', [('ParticleSystemAsset', 2, 'project://Particles/MuzzleFlash.plutoparticles')])
    return text


for kind in manifest:
    graph = f'AnimationGraphVersion=4\nDefaultStateId=1\nParameter=1|Fire|Trigger|0|0|0\nParameter=2|Reload|Bool|0|0|0\nParameter=3|Draw|Trigger|0|0|0\nParameter=4|Sprint|Bool|0|0|0\nParameter=5|Speed|Float|0|0|0\nState=1|Idle|{kind}_Idle|0|100|100|1|1|project://Weapons/{kind}/Idle.plutoclip\n'
    for layer, name, index, loop, fade_in, fade_out in [(1, 'Sprint', 4, 1, .12, .12), (2, 'Draw', 1, 0, .02, .06),
                                                       (3, 'Fire', 2, 0, .01, .035), (4, 'Reload', 3, 0, .06, .10)]:
        graph += f'Layer={layer}|{name}|project://Weapons/{kind}/{name}.plutoclip|{kind}_{name}|{index}|0|0|1||{name}|1|{fade_in}|{fade_out}|{loop}|1|1|\n'
    write(assets / f'Weapons/{kind}/{kind}.plutoanimgraph', graph)
    # Standalone prefab: caller must supply its owner and parent camera before use.
    write(assets / f'Prefabs/Weapons/{kind}.plutoprefab', 'SCENE\t1\n' + rig(kind, 1, 0, 0))

for relative in ['Scenes/Main.plutoscene', 'Scenes/Foundry.plutoscene', 'Prefabs/Player.plutoprefab']:
    path = assets / relative
    text = path.read_text(encoding='utf-8')
    # Idempotently remove this tool's previous generated rigs and fields.
    start_marker = 'COMMENT\tGENERATED_WEAPON_LOADOUT_BEGIN\n'
    end_marker = 'COMMENT\tGENERATED_WEAPON_LOADOUT_END\n'
    if start_marker in text:
        start = text.index(start_marker)
        end = text.index(end_marker, start) + len(end_marker)
        text = text[:start] + text[end:]
    text = re.sub(r'^PROPERTY\t(?:assaultRifleRig|pistolRig|machineGunRig)\t.*\n', '', text, flags=re.M)
    controller = 'PROPERTY\tSource\t2\tCoD.Scripts.PlayerController\t0\n'
    start = text.index(controller) + len(controller)
    camera = int(re.search(r'^PROPERTY\tcamera\t9\t(\d+)\t0$', text[start:], re.M).group(1))
    owner = int(re.search(r'^COMPONENT\t(\d+)\tScriptComponent\t1\n' + re.escape(controller), text, re.M).group(1))
    next_id = max(int(value) for value in re.findall(r'^ENTITY\t(\d+)\t', text, re.M)) + 100
    fields = ''.join(property_(name, 9, next_id + slot * 10) for slot, name in enumerate(['assaultRifleRig', 'pistolRig', 'machineGunRig']))
    text = text[:start] + fields + text[start:]
    overlay = re.search(r'^ENTITY\t(\d+)\t' + str(camera) + r'\t1\tWeaponOverlayCamera\t.*$', text, re.M)
    if overlay:
        old_line = overlay.group(0)
        pieces = old_line.split('\t')
        pieces[5] = '0,0,0'
        text = text.replace(old_line, '\t'.join(pieces), 1)
        overlay_id = int(overlay.group(1))
        pattern = r'(COMPONENT\t' + str(overlay_id) + r'\tCameraComponent\t1\n)(.*?)(END_COMPONENT\n)'
        def update_overlay(match):
            fields = re.sub(r'^PROPERTY\tFOV\t.*$', 'PROPERTY\tFOV\t0\t65\t0', match.group(2), flags=re.M)
            fields = re.sub(r'^PROPERTY\tNearPlane\t.*$', 'PROPERTY\tNearPlane\t0\t0.03\t0', fields, flags=re.M)
            return match.group(1) + fields + match.group(3)
        text = re.sub(pattern, update_overlay, text, flags=re.S)
        new_overlay = ''
    else:
        overlay_id = next_id + 40
        new_overlay = f'ENTITY\t{overlay_id}\t{camera}\t1\tWeaponOverlayCamera\t0,0,0\t0,0,0\t1,1,1\n'
        new_overlay += component(overlay_id, 'CameraComponent', [('FOV', 0, 65), ('NearPlane', 0, .03), ('FarPlane', 0, 100),
            ('MainCamera', 4, 'false'), ('RenderType', 6, '1\t2\tBase\tOverlay'), ('OverlayOrder', 1, 1),
            ('RenderTags', 2, 'Weapon'), ('TransparentBackground', 4, 'true')])
    # The base camera must not draw the same viewmodels into world depth.
    camera_pattern = r'(COMPONENT\t' + str(camera) + r'\tCameraComponent\t1\n)(.*?)(END_COMPONENT\n)'
    def filter_base(match):
        fields = match.group(2)
        if 'PROPERTY\tIgnoredTags\t' not in fields:
            fields += property_('IgnoredTags', 2, 'Weapon')
        return match.group(1) + fields + match.group(3)
    text = re.sub(camera_pattern, filter_base, text, flags=re.S)
    text += start_marker + ''.join(rig(kind, next_id + slot * 10, camera, owner) for slot, kind in enumerate(['ar', 'pistol', 'lmg'])) + new_overlay + end_marker
    path.write_text(text, encoding='utf-8', newline='\r\n')

# Explicit registration also makes generated content visible to packaging.
project = root / 'CoDplutoproject.plutoproject'
text = project.read_text(encoding='utf-8')
types = {'.plutomesh': 'Mesh', '.plutoclip': 'Animation Clip', '.plutoanim': 'Animation', '.plutoanimgraph': 'Animation Graph',
         '.plutomaterial': 'Material', '.plutoprefab': 'Prefab', '.blend': 'Model', '.glb': 'Model', '.png': 'Texture'}
paths = list((assets / 'Weapons').rglob('*')) + list((assets / 'Prefabs/Weapons').glob('*')) + list((assets / 'SourceModels/Weapons').glob('*'))
for path in sorted(paths):
    if path.suffix not in types:
        continue
    reference = 'project://' + path.relative_to(assets).as_posix()
    if not re.search(r'^ASSET\t' + re.escape(reference) + r'\t', text, re.M):
        text += f'ASSET\t{reference}\t0\t{types[path.suffix]}\n'
project.write_text(text, encoding='utf-8', newline='\r\n')
print('Installed weapon graphs/prefabs and wired Main, Foundry and Player.')
