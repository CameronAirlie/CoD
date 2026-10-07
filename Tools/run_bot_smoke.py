"""Exercise each actual bot prefab in an isolated native runtime scene."""
import json, os, re, shutil, subprocess, sys
from pathlib import Path
root=Path(__file__).resolve().parent.parent
runtime=Path(sys.argv[1]).resolve()
out=root/'Tools/Native/BotSmoke'; out.mkdir(parents=True,exist_ok=True)
flags=subprocess.CREATE_NO_WINDOW if os.name=='nt' else 0
for file in runtime.glob('*.dll'): shutil.copy2(file,out/file.name)
shutil.copy2(runtime/'PlutoGERuntime.exe',out/'PlutoGERuntime.exe')
shutil.copytree(runtime/'Shaders',out/'Shaders',dirs_exist_ok=True)
core=root/'Assets/Managed/PlutoGE.ScriptCore.dll'
project=out/'Smoke.csproj'
project.write_text(f'''<Project Sdk="Microsoft.NET.Sdk"><PropertyGroup><TargetFramework>net8.0</TargetFramework><ImplicitUsings>enable</ImplicitUsings><Nullable>enable</Nullable><EnableDefaultItems>false</EnableDefaultItems><AssemblyName>CoD.Scripts</AssemblyName><OutputPath>Managed/</OutputPath><AppendTargetFrameworkToOutputPath>false</AppendTargetFrameworkToOutputPath></PropertyGroup><ItemGroup><Compile Include="../../../Assets/Scripts/**/*.cs"/><Compile Include="../../../Tests/BotSmokeProbe.cs"/><Reference Include="PlutoGE.ScriptCore"><HintPath>{core.as_posix()}</HintPath><Private>true</Private></Reference></ItemGroup></Project>''')
subprocess.run(['dotnet','build',str(project),'-c','Release'],check=True,creationflags=flags)
for suffix in ['.runtimeconfig.json','.deps.json']:
    shutil.copy2(root/('Assets/Managed/PlutoGE.ScriptCore'+suffix),out/('Managed/PlutoGE.ScriptCore'+suffix))
shutdown_failed=False
for name,owner,mesh in [('Enemy',156,160),('RemotePlayer',1,3),('EnemyPreview',156,160),('RemotePlayerPreview',1,3)]:
    if len(sys.argv)>2 and name not in sys.argv[2:]: continue
    result=out/(name+'-result.txt')
    if result.exists(): result.unlink()
    scene=out/(name+'.plutoscene')
    preview=name.endswith('Preview')
    prefab=name.removesuffix('Preview')
    text=(root/f'Assets/Prefabs/{prefab}.plutoprefab').read_text()
    if preview:
        text=re.sub(r'COMPONENT\t\d+\tScriptComponent\t1\nPROPERTY\tSource\t2\tCoD.Scripts.EnemySoldierBot\t0\n.*?END_COMPONENT\n', '', text, flags=re.S)
    poses=json.loads((root/'Assets/Bots/Soldier/manifest.json').read_text())['weaponPoses']
    for entity,weapon,parent in [(80001,'Rifle',7003),(80002,'Pistol',7004)]:
        offset=','.join(str(v) for v in poses[weapon]['supportGripOffset'])
        text+=f'ENTITY\t{entity}\t{parent}\t1\tIndependent gun grip marker\t{offset}\t0,0,0\t1,1,1\n'
    text+=f'COMPONENT\t{owner}\tScriptComponent\t1\nPROPERTY\tSource\t2\tCoD.Tests.BotSmokeProbe\t0\nPROPERTY\tresultPath\t2\t{result.as_posix()}\t0\nPROPERTY\tsoldierMesh\t9\t{mesh}\t0\nPROPERTY\tsupportTarget\t9\t7001\t0\nPROPERTY\tgun\t9\t7003\t0\nEND_COMPONENT\n'
    text+='ENTITY\t80000\t0\t1\tSmoke Camera\t0,1,-4\t0,180,0\t1,1,1\nCOMPONENT\t80000\tCameraComponent\t1\nPROPERTY\tMainCamera\t4\ttrue\t0\nEND_COMPONENT\n'
    text=text.replace('PROPERTY\tgun\t9\t7003\t0\n', 'PROPERTY\tgun\t9\t7003\t0\nPROPERTY\trifleGrip\t9\t80001\t0\nPROPERTY\tpistolGrip\t9\t80002\t0\n')
    if preview: text=text.replace('PROPERTY\tresultPath\t', 'PROPERTY\tpreviewOnly\t4\ttrue\t0\nPROPERTY\tresultPath\t')
    scene.write_text(text)
    manifest=out/(name+'.plutoproject')
    manifest.write_text(f'PLUTOPROJECT\t1\nNAME\tBot Smoke\nASSET_DIR\t{(root/"Assets").as_posix()}\nSTARTUP_SCENE\t{scene.as_posix()}\nSCRIPT_ASSEMBLY\t{(out/"Managed/CoD.Scripts.dll").as_posix()}\nWINDOW_SIZE\t640\t360\nVSYNC\t0\nGRAPHICS_API\tVulkan\n')
    with (out/(name+'-runtime.log')).open('w') as log:
        try:
            completed=subprocess.run([str(out/'PlutoGERuntime.exe'),'--benchmark-project',str(manifest),str(out/(name+'.csv')),'150','0'],cwd=out,stdout=log,stderr=subprocess.STDOUT,timeout=45,creationflags=flags)
        except subprocess.TimeoutExpired:
            shutdown_failed=True
            completed=None
    message=result.read_text() if result.exists() else 'FAIL: probe did not run; inspect '+name+'-runtime.log'
    print(name+': '+message)
    if completed is None: print(name+': FAIL: native process did not exit within 45 seconds.')
    if (completed is not None and completed.returncode) or not message.startswith('PASS:'): sys.exit(1)
if shutdown_failed: sys.exit(1)
