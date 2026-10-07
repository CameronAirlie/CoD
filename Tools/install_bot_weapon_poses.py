"""Install weapon-pose wiring without replacing unrelated prefab components."""
import json
import math
import re
import uuid
from pathlib import Path

ROOT = Path(__file__).resolve().parent.parent
ASSETS = ROOT / 'Assets'

def grip_rotation(pose):
    # Manifest stores Blender's w,x,y,z quaternion; entities use Rx * Ry * Rz.
    w, x, y, z = pose['supportRotationQuaternion']
    return [math.degrees(math.atan2(2 * (w*x-y*z), 1-2*(x*x+y*y))),
            math.degrees(math.asin(max(-1, min(1, 2*(x*z+w*y))))),
            math.degrees(math.atan2(2 * (w*z-x*y), 1-2*(y*y+z*z)))]

def install():
    poses = json.loads((ASSETS / 'Bots/Soldier/manifest.json').read_text())['weaponPoses']
    for name, root, socket in [('Enemy', 156, 2537), ('RemotePlayer', 1, 7000)]:
        path = ASSETS / f'Prefabs/{name}.plutoprefab'
        text = path.read_text()
        pattern = r'(COMPONENT\t' + str(root) + r'\tScriptComponent\t1\nPROPERTY\tSource\t2\tCoD.Scripts.EnemySoldierBot\t0\n)(.*?)(END_COMPONENT)'
        def configure(match):
            properties = match[2]
            for field in ['soldierMesh', 'rifleWeapon', 'pistolWeapon', 'supportGripOffset', 'pistolSupportGripOffset', 'supportGripRotation', 'pistolSupportGripRotation']:
                properties = re.sub(r'^PROPERTY\t' + field + r'\t.*\n', '', properties, flags=re.M)
            if not re.search(r'^PROPERTY\tweaponPose\t', properties, re.M):
                properties += 'PROPERTY\tweaponPose\t6\t0\t2\tRifle\tPistol\n'
            for field, value in [('rifleWeapon', 7003), ('pistolWeapon', 7004)]:
                properties += f'PROPERTY\t{field}\t9\t{value}\t0\n'
            for field, weapon in [('supportGripOffset', 'Rifle'), ('pistolSupportGripOffset', 'Pistol')]:
                value = ','.join(f'{v:.9g}' for v in poses[weapon]['supportGripOffset'])
                properties += f'PROPERTY\t{field}\t3\t{value}\t0\n'
            for field, weapon in [('supportGripRotation', 'Rifle'), ('pistolSupportGripRotation', 'Pistol')]:
                value = ','.join(f'{v:.9g}' for v in grip_rotation(poses[weapon]))
                properties += f'PROPERTY\t{field}\t3\t{value}\t0\n'
            return match[1] + properties + match[3]
        text, count = re.subn(pattern, configure, text, flags=re.S)
        if count != 1: raise RuntimeError(f'{name}: expected one bot script')
        # Follow the firing-hand socket even in editor preview (no scripts).
        offset = ','.join(f'{v:.9g}' for v in poses['Rifle']['supportGripOffset'])
        rotation = ','.join(f'{v:.9g}' for v in grip_rotation(poses['Rifle']))
        text = re.sub(r'^ENTITY\t7001\t.*$',
                      f'ENTITY\t7001\t{socket}\t1\tSupport Hand IK Target\t{offset}\t{rotation}\t1,1,1', text, flags=re.M)
        text = re.sub(r'^PROPERTY\tConstraints\.0\.RotationWeight\t.*$',
                      'PROPERTY\tConstraints.0.RotationWeight\t0\t1\t0', text, flags=re.M)
        text = re.sub(r'^(ENTITY\t7002\t[^\t]+\t1\t[^\t]+\t)[^\t]+', r'\g<1>-0.34,0.12,-0.08', text, flags=re.M)
        if not re.search(r'^ENTITY\t7004\t', text, re.M):
            text += f'ENTITY\t7004\t{socket}\t0\tP-12 pistol\t0,0,0\t0,0,0\t1,1,1\n'
            text += 'COMPONENT\t7004\tMeshComponent\t1\nPROPERTY\tStatic\t4\tfalse\t0\nPROPERTY\tVisible\t4\ttrue\t0\nPROPERTY\tSubmeshIndex\t1\t-1\t0\nPROPERTY\tMeshAssetReference\t2\tproject://Bots/Soldier/P12_HandSpace.plutomesh\t0\nPROPERTY\tMeshPositionOffset\t3\t0,0,0\t0\nEND_COMPONENT\n'
        path.write_text(text, newline='\r\n')
    project = ROOT / 'CoDplutoproject.plutoproject'
    registry = project.read_text()
    paths = list((ASSETS / 'Bots/Soldier').glob('Pistol_*.plutoclip'))
    paths += [ASSETS / 'Bots/Soldier/P12_HandSpace.plutomesh', ASSETS / 'SourceModels/Solider/Rigged/Soldier_Bot_WeaponPoses.blend']
    for path in paths:
        reference = 'project://' + path.relative_to(ASSETS).as_posix()
        meta = Path(str(path) + '.plutometa')
        if not meta.exists():
            meta.write_text(f'PLUTOASSET\t1\nID\t{uuid.uuid5(uuid.NAMESPACE_URL, "cod-bots/" + reference).hex}\nIMPORTER_VERSION\t1\n')
        if not any(line.startswith('ASSET\t' + reference + '\t') for line in registry.splitlines()):
            kind = {'.plutoclip': 'Animation Clip', '.plutomesh': 'Mesh', '.blend': 'Model'}[path.suffix]
            registry += f'ASSET\t{reference}\t{path.stat().st_size}\t{kind}\n'
    project.write_text(registry, newline='\r\n')

if __name__ == '__main__':
    install()
    print('Installed reachable rifle/pistol poses and matching weapon visuals.')
