"""Load the actual title scene and shipped UI assembly in an isolated Vulkan runtime."""
from pathlib import Path
import os
import shutil
import subprocess
import sys

root = Path(__file__).resolve().parent.parent
runtime = Path(sys.argv[1]).resolve()
out = root / 'Tools/Native/UI/Runtime'
out.mkdir(parents=True, exist_ok=True)
for path in runtime.glob('*.dll'):
    shutil.copy2(path, out / path.name)
shutil.copy2(runtime / 'PlutoGERuntime.exe', out / 'PlutoGERuntime.exe')
shutil.copytree(runtime / 'Shaders', out / 'Shaders', dirs_exist_ok=True)
manifest = out / 'UiSmoke.plutoproject'
manifest.write_text(f'PLUTOPROJECT\t1\nNAME\tTactical UI Smoke\nASSET_DIR\t{(root / "Assets").as_posix()}\n'
    'STARTUP_SCENE\tproject://Scenes/Title.plutoscene\n'
    f'SCRIPT_ASSEMBLY\t{(root / "Assets/Managed/CoD.Scripts.dll").as_posix()}\n'
    'WINDOW_SIZE\t1280\t720\nVSYNC\t0\nGRAPHICS_API\tVulkan\n')
flags = subprocess.CREATE_NO_WINDOW if os.name == 'nt' else 0
with (out / 'runtime.log').open('w') as log:
    run = subprocess.run([str(out / 'PlutoGERuntime.exe'), '--benchmark-project', str(manifest),
        str(out / 'ui.csv'), '90', '0'], cwd=out, creationflags=flags, stdout=log, stderr=subprocess.STDOUT, timeout=120)
log = (out / 'runtime.log').read_text(errors='replace')
errors = [line for line in log.splitlines() if any(text in line for text in ['Syntax error', 'Invalid at-rule', 'Font not found', 'Managed exception', 'Could not load', 'requires an RmlWidget'])]
fonts = all(f"Loaded font face '{family}'" in log for family in ['TacticalBody', 'TacticalDisplay', 'TacticalHeading'])
if run.returncode or errors or not fonts:
    print(log[-7000:])
    sys.exit(1)
print('PASS: actual Vulkan title scene, shipped assembly, shared font registration and UI asset loading.')
