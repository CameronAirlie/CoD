"""Build a test-only managed assembly and run the real native runtime in a hidden window.

Usage: python Tools/run_weapon_smoke.py <PlutoGE runtime directory>
Generated fixtures, runtime DLLs and logs stay under ignored Tools/Native.
"""
import os
import re
import shutil
import subprocess
import sys
from pathlib import Path

root = Path(__file__).resolve().parent.parent
runtime = Path(sys.argv[1]).resolve()
out = root / 'Tools/Native'
out.mkdir(exist_ok=True)
for file in runtime.glob('*.dll'):
    shutil.copy2(file, out / file.name)
shutil.copy2(runtime / 'PlutoGERuntime.exe', out / 'PlutoGERuntime.exe')
shutil.copytree(runtime / 'Shaders', out / 'Shaders', dirs_exist_ok=True)
assembly = out / 'Managed/CoD.Scripts.dll'
project = out / 'Smoke.csproj'
core = root / 'Assets/Managed/PlutoGE.ScriptCore.dll'
project.write_text(f'''<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><TargetFramework>net8.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable>
<EnableDefaultItems>false</EnableDefaultItems><AssemblyName>CoD.Scripts</AssemblyName><OutputPath>Managed/</OutputPath>
<AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath></PropertyGroup>
<ItemGroup><Compile Include="../../Assets/Scripts/**/*.cs"/><Compile Include="../../Tests/WeaponSmokeProbe.cs"/>
<Reference Include="PlutoGE.ScriptCore"><HintPath>{core.as_posix()}</HintPath><Private>true</Private></Reference></ItemGroup>
</Project>''', encoding='utf-8')
flags = subprocess.CREATE_NO_WINDOW if os.name == 'nt' else 0
subprocess.run(['dotnet', 'build', str(project), '-c', 'Release'], check=True, creationflags=flags)
for suffix in ['.runtimeconfig.json', '.deps.json']:
    shutil.copy2(root / ('Assets/Managed/PlutoGE.ScriptCore' + suffix), out / ('Managed/PlutoGE.ScriptCore' + suffix))

def prop(name, kind, value):
    return f'PROPERTY\t{name}\t{kind}\t{value}\t0\n'

def comp(entity, name, fields):
    return f'COMPONENT\t{entity}\t{name}\t1\n' + ''.join(prop(*p) for p in fields) + 'END_COMPONENT\n'

result = out / 'smoke-result.txt'
if result.exists():
    result.unlink()
text = 'SCENE\t1\nENTITY\t1\t0\t1\tPlayer\t0,1.5,0\t0,0,0\t1,1,1\n'
text += comp(1, 'ScriptComponent', [('Source', 2, 'CoD.Scripts.PlayerInventory')])
text += comp(1, 'ScriptComponent', [('Source', 2, 'CoD.Scripts.PlayerController'), ('camera', 9, 2),
    ('weaponModel', 9, 90),
    ('assaultRifleRig', 9, 100), ('pistolRig', 9, 110), ('machineGunRig', 9, 120)])
text += comp(1, 'ScriptComponent', [('Source', 2, 'CoD.Tests.WeaponSmokeProbe'), ('resultPath', 2, result.as_posix()),
    ('rifle', 9, 100), ('pistol', 9, 110), ('machineGun', 9, 120), ('legacyRig', 9, 90)])
text += 'ENTITY\t2\t1\t1\tCamera\t0,1.62,0\t0,0,0\t1,1,1\n'
text += comp(2, 'CameraComponent', [('FOV', 0, 78), ('NearPlane', 0, .05), ('FarPlane', 0, 100),
    ('MainCamera', 4, 'true'), ('IgnoredTags', 2, 'Weapon')])
text += 'ENTITY\t3\t2\t1\tWeaponOverlayCamera\t0,0,0\t0,0,0\t1,1,1\n'
text += comp(3, 'CameraComponent', [('FOV', 0, 65), ('NearPlane', 0, .03), ('FarPlane', 0, 100),
    ('MainCamera', 4, 'false'), ('RenderType', 6, '1\t2\tBase\tOverlay'), ('OverlayOrder', 1, 1),
    ('RenderTags', 2, 'Weapon'), ('TransparentBackground', 4, 'true')])
text += 'ENTITY\t90\t2\t1\tLegacy Fallback Rig\t0,0,0\t0,0,0\t1,1,1\n'
for kind, identity in [('ar', 100), ('pistol', 110), ('lmg', 120)]:
    source = (root / f'Assets/Prefabs/Weapons/{kind}.plutoprefab').read_text()
    for line in source.splitlines()[1:]:
        fields = line.split('\t')
        if fields[0] in ['ENTITY', 'COMPONENT', 'TAGS']:
            fields[1] = str(identity + int(fields[1]) - 1)
        if fields[0] == 'ENTITY':
            fields[2] = str(identity + int(fields[2]) - 1) if int(fields[2]) else '2'
        if fields[0] == 'PROPERTY' and fields[2] == '9':
            fields[3] = '1' if fields[1] == 'owner' else str(identity + int(fields[3]) - 1)
        text += '\t'.join(fields) + '\n'
scene = out / 'WeaponSmoke.plutoscene'
scene.write_text(text, encoding='utf-8')
manifest = out / 'WeaponSmoke.plutoproject'
manifest.write_text(f'PLUTOPROJECT\t1\nNAME\tWeapon Smoke\nASSET_DIR\t{(root / "Assets").as_posix()}\n'
    f'STARTUP_SCENE\t{scene.as_posix()}\nSCRIPT_ASSEMBLY\t{assembly.as_posix()}\n'
    'WINDOW_SIZE\t640\t360\nVSYNC\t0\nGRAPHICS_API\tVulkan\n', encoding='utf-8')
with (out / 'smoke-runtime.log').open('w') as log:
    completed = subprocess.run([str(out / 'PlutoGERuntime.exe'), '--benchmark-project', str(manifest),
        str(out / 'smoke.csv'), '400', '0'], cwd=out, stdout=log, stderr=subprocess.STDOUT,
        timeout=120, creationflags=flags)
message = result.read_text() if result.exists() else 'FAIL: runtime did not complete the weapon probe; inspect Tools/Native/smoke-runtime.log'
print(message)
if completed.returncode != 0 or not message.startswith('PASS:'):
    sys.exit(1)
