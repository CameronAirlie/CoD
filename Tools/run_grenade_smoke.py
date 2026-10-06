"""Run the grenade probe in an isolated native Vulkan scene and managed assembly."""
from pathlib import Path
import os
import shutil
import subprocess
import sys

root = Path(__file__).resolve().parent.parent
runtime = Path(sys.argv[1]).resolve()
out = root / 'Tools/Native/Grenades'
out.mkdir(parents=True, exist_ok=True)
for file in runtime.glob('*.dll'):
    shutil.copy2(file, out / file.name)
shutil.copy2(runtime / 'PlutoGERuntime.exe', out / 'PlutoGERuntime.exe')
shutil.copytree(runtime / 'Shaders', out / 'Shaders', dirs_exist_ok=True)
core = root / 'Assets/Managed/PlutoGE.ScriptCore.dll'
project = out / 'Smoke.csproj'
project.write_text(f'''<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><TargetFramework>net8.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable>
<EnableDefaultItems>false</EnableDefaultItems><AssemblyName>CoD.Scripts</AssemblyName><OutputPath>Managed/</OutputPath>
<AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath></PropertyGroup>
<ItemGroup><Compile Include="../../../Assets/Scripts/**/*.cs"/><Compile Include="../../../Tests/GrenadeSmokeProbe.cs"/>
<Reference Include="PlutoGE.ScriptCore"><HintPath>{core.as_posix()}</HintPath><Private>true</Private></Reference></ItemGroup>
</Project>''')
flags = subprocess.CREATE_NO_WINDOW if os.name == 'nt' else 0
build = subprocess.run(['dotnet', 'build', str(project), '-c', 'Release'], capture_output=True, text=True, creationflags=flags)
if build.returncode:
    print(build.stdout + build.stderr)
    sys.exit(build.returncode)
for suffix in ['.runtimeconfig.json', '.deps.json']:
    shutil.copy2(root / ('Assets/Managed/PlutoGE.ScriptCore' + suffix), out / ('Managed/PlutoGE.ScriptCore' + suffix))

def component(entity, name, values):
    return f'COMPONENT\t{entity}\t{name}\t1\n' + ''.join(
        f'PROPERTY\t{key}\t{kind}\t{value}\t0\n' for key, kind, value in values) + 'END_COMPONENT\n'

result = out / 'result.txt'
if result.exists():
    result.unlink()
scene_text = 'SCENE\t1\nENTITY\t1\t0\t1\tPlayer\t0,0.7,10\t0,0,0\t1,1,1\n'
scene_text += component(1, 'ScriptComponent', [('Source', 2, 'CoD.Scripts.MultiplayerSession'),
    ('mode', 2, 'Host'), ('fillWithBots', 4, 'false'), ('serverPort', 1, 17977), ('gameMode', 2, 'TeamDeathmatch')])
scene_text += component(1, 'ScriptComponent', [('Source', 2, 'CoD.Scripts.GrenadeSmokeProbe'), ('resultPath', 2, result.as_posix())])
scene_text += 'ENTITY\t2\t0\t1\tCamera\t0,3,12\t-10,0,0\t1,1,1\n'
scene_text += component(2, 'CameraComponent', [('FOV', 0, 78), ('NearPlane', 0, .05), ('FarPlane', 0, 100), ('MainCamera', 4, 'true')])
for identity, name, position, size in [(7, 'Enemy', '3,0.7,0', '1,1.4,1'), (8, 'Friendly', '0,0.7,-3', '1,1.4,1'),
    (9, 'Covered', '-3,0.7,0', '1,1.4,1'), (10, 'Wall', '-1.5,1,0', '.3,4,4'), (11, 'Floor', '0,-.5,0', '40,1,40')]:
    scene_text += f'ENTITY\t{identity}\t0\t1\t{name}\t{position}\t0,0,0\t1,1,1\n'
    scene_text += component(identity, 'ColliderComponent', [('Shape', 6, '0\t5\tBox\tSphere\tCapsule\tTerrain\tMesh'),
        ('Center', 3, '0,0,0'), ('Size', 3, size), ('Is Trigger', 4, 'false')])
scene = out / 'Smoke.plutoscene'
scene.write_text(scene_text)
manifest = out / 'Smoke.plutoproject'
manifest.write_text(f'PLUTOPROJECT\t1\nNAME\tGrenade Smoke\nASSET_DIR\t{(root / "Assets").as_posix()}\n'
    f'STARTUP_SCENE\t{scene.as_posix()}\nSCRIPT_ASSEMBLY\t{(out / "Managed/CoD.Scripts.dll").as_posix()}\n'
    'WINDOW_SIZE\t640\t360\nVSYNC\t0\nGRAPHICS_API\tVulkan\n')
with (out / 'runtime.log').open('w') as log:
    run = subprocess.run([str(out / 'PlutoGERuntime.exe'), '--benchmark-project', str(manifest),
        str(out / 'smoke.csv'), '80', '0'], cwd=out, stdout=log, stderr=subprocess.STDOUT, timeout=120, creationflags=flags)
message = result.read_text() if result.exists() else 'FAIL: no result; inspect Tools/Native/Grenades/runtime.log'
print(message)
sys.exit(0 if run.returncode == 0 and message.startswith('PASS:') else 1)
