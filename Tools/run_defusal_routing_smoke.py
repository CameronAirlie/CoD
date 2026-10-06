"""Validate Foundry route markers and the real host movement adapter in isolation."""
from pathlib import Path
import os
import re
import shutil
import subprocess
import sys

root = Path(__file__).resolve().parent.parent
runtime = Path(sys.argv[1]).resolve()
out = root / 'Tools/Native/DefusalRouting'
out.mkdir(parents=True, exist_ok=True)
for source in [*runtime.glob('*.dll'), runtime / 'PlutoGERuntime.exe']:
    shutil.copy2(source, out / source.name)
shutil.copytree(runtime / 'Shaders', out / 'Shaders', dirs_exist_ok=True)
project = out / 'Smoke.csproj'
project.write_text(f'''<Project Sdk="Microsoft.NET.Sdk">
<PropertyGroup><TargetFramework>net8.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable>
<EnableDefaultItems>false</EnableDefaultItems><AssemblyName>CoD.Scripts</AssemblyName><OutputPath>Managed/</OutputPath>
<AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath></PropertyGroup>
<ItemGroup><Compile Include="../../../Assets/Scripts/**/*.cs"/><Compile Include="../../../Tests/DefusalRoutingSmokeProbe.cs"/>
<Reference Include="PlutoGE.ScriptCore"><HintPath>{(root / 'Assets/Managed/PlutoGE.ScriptCore.dll').as_posix()}</HintPath><Private>true</Private></Reference></ItemGroup>
</Project>''')
flags = subprocess.CREATE_NO_WINDOW if os.name == 'nt' else 0
build = subprocess.run(['dotnet', 'build', str(project), '-c', 'Release'], capture_output=True, text=True, creationflags=flags)
if build.returncode:
    print(build.stdout + build.stderr)
    sys.exit(build.returncode)
for suffix in ['.runtimeconfig.json', '.deps.json']:
    shutil.copy2(root / ('Assets/Managed/PlutoGE.ScriptCore' + suffix), out / ('Managed/PlutoGE.ScriptCore' + suffix))
result = out / 'result.txt'
result.unlink(missing_ok=True)
scene = out / 'Smoke.plutoscene'
text = (root / 'Assets/Scenes/Foundry.plutoscene').read_text()
text = re.sub(r'^PROPERTY\tserverPort\t.*$', 'PROPERTY\tserverPort\t1\t17978\t0', text, flags=re.M)
text = re.sub(r'^PROPERTY\tminimumParticipants\t.*$', 'PROPERTY\tminimumParticipants\t1\t5\t0', text, flags=re.M)
text += f'COMPONENT\t1\tScriptComponent\t1\nPROPERTY\tSource\t2\tCoD.Scripts.DefusalRoutingSmokeProbe\t0\nPROPERTY\tresultPath\t2\t{result.as_posix()}\t0\nEND_COMPONENT\n'
scene.write_text(text)
manifest = out / 'Smoke.plutoproject'
manifest.write_text(f'PLUTOPROJECT\t1\nNAME\tDefusal Routing Smoke\nASSET_DIR\t{(root / "Assets").as_posix()}\n'
    f'STARTUP_SCENE\t{scene.as_posix()}\nSCRIPT_ASSEMBLY\t{(out / "Managed/CoD.Scripts.dll").as_posix()}\n'
    'WINDOW_SIZE\t640\t360\nVSYNC\t0\nGRAPHICS_API\tVulkan\n')
with (out / 'runtime.log').open('w') as log:
    run = subprocess.run([str(out / 'PlutoGERuntime.exe'), '--benchmark-project', str(manifest),
        str(out / 'smoke.csv'), '35', '0'], cwd=out, stdout=log, stderr=subprocess.STDOUT, timeout=120, creationflags=flags)
message = result.read_text() if result.exists() else 'FAIL: no result; inspect Tools/Native/DefusalRouting/runtime.log'
print(message)
sys.exit(0 if run.returncode == 0 and message.startswith('PASS:') else 1)
