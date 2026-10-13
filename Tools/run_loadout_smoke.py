"""Validate Hardpoint loadout spawn behavior in the real runtime. Pass the PlutoGERuntime.exe path."""
from pathlib import Path
import os, shutil, subprocess, sys, time
ROOT=Path(__file__).resolve().parents[1]
runtime=Path(sys.argv[1]).resolve();out=ROOT/'Tools/Native/HardpointLoadouts';out.mkdir(parents=True,exist_ok=True)
project=out/'Smoke.csproj'
project.write_text('''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><EnableDefaultItems>false</EnableDefaultItems><AssemblyName>CoD.Scripts</AssemblyName><OutputPath>Managed/</OutputPath><AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath></PropertyGroup><ItemGroup><Compile Include="../../../Assets/Scripts/**/*.cs"/><Compile Include="../../../Tests/CombatSpawns/LoadoutSmokeProbe.cs"/><Reference Include="PlutoGE.ScriptCore"><HintPath>../../../Assets/Managed/PlutoGE.ScriptCore.dll</HintPath><Private>true</Private></Reference></ItemGroup></Project>''')
subprocess.run(['dotnet','build',str(project),'-c','Release'],check=True)
for suffix in ['.runtimeconfig.json','.deps.json']:shutil.copy2(ROOT/('Assets/Managed/PlutoGE.ScriptCore'+suffix),out/('Managed/PlutoGE.ScriptCore'+suffix))
result=out/'result.txt';result.unlink(missing_ok=True)
scene=(ROOT/'Assets/Scenes/Shipment.plutoscene').read_text()
scene+=f'COMPONENT\t1\tScriptComponent\t1\nPROPERTY\tSource\t2\tCoD.Scripts.HardpointLoadoutSmokeProbe\t0\nPROPERTY\tresultPath\t2\t{result.as_posix()}\t0\nEND_COMPONENT\n'
(out/'Smoke.plutoscene').write_text(scene)
over={'ASSET_DIR':(ROOT/'Assets').as_posix(),'STARTUP_SCENE':(out/'Smoke.plutoscene').as_posix(),'SCRIPT_ASSEMBLY':(out/'Managed/CoD.Scripts.dll').as_posix(),'WINDOW_SIZE':'640\t360','VSYNC':'0','GRAPHICS_API':'Vulkan'}
manifest=ROOT/'HardpointLoadoutSmoke.plutoproject'
assert not manifest.exists(), 'Another loadout smoke run is active.'
text='\n'.join(l for l in (ROOT/'CoDplutoproject.plutoproject').read_text().splitlines() if l.split('\t')[0] not in over)+'\n'+'\n'.join(k+'\t'+v for k,v in over.items())+'\n'
manifest.write_text(text)
try:
 with (out/'navigation.log').open('w') as log:
  proc=subprocess.Popen([str(runtime),'--benchmark-project',str(manifest),str(out/'navigation.csv'),'40','0'],cwd=runtime.parent,stdout=log,stderr=subprocess.STDOUT,creationflags=subprocess.CREATE_NO_WINDOW if os.name=='nt' else 0)
  try:
   for i in range(60):
    if result.exists() or proc.poll() is not None:break
    time.sleep(1)
   message=result.read_text() if result.exists() else 'FAIL: no probe result; inspect Tools/Native/HardpointLoadouts/navigation.log'
   print(message)
  finally:
   if proc.poll() is None:proc.terminate();proc.wait(timeout=10)
finally:manifest.unlink(missing_ok=True)
sys.exit(0 if message.startswith('PASS:') else 1)
