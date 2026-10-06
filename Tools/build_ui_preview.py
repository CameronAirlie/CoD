"""Compile native RmlUi UI verifier using existing engine libraries; no engine edits."""
from pathlib import Path
import os
import subprocess
import sys
import xml.etree.ElementTree as ET

root = Path(__file__).resolve().parent.parent
build = Path(sys.argv[1]).resolve()
vcvars = Path(sys.argv[2]).resolve()
out = root / 'Tools/Native/UI'
out.mkdir(parents=True, exist_ok=True)
project = build / 'tests/PlutoGERmlUiDocumentLayoutTests.vcxproj'
ns = {'m': 'http://schemas.microsoft.com/developer/msbuild/2003'}
tree = ET.parse(project)
group = next(g for g in tree.findall('m:ItemDefinitionGroup', ns) if 'RelWithDebInfo|x64' in g.get('Condition', ''))
includes = group.find('m:ClCompile/m:AdditionalIncludeDirectories', ns).text.split(';')
libraries = group.find('m:Link/m:AdditionalDependencies', ns).text.split(';')
arguments = ['/nologo', '/std:c++20', '/EHsc', '/MD', '/O2', '/DNDEBUG', '/DRMLUI_STATIC_LIB', f'/Fo"{out / "preview.obj"}"', f'/Fe"{out / "preview.exe"}"']
arguments += [f'/I"{p}"' for p in includes if not p.startswith('%')]
arguments += [f'/I"{build / "_deps/stb_image-src"}"', f'"{root / "Tools/preview_tactical_ui.cpp"}"', '/link']
arguments += [f'"{(project.parent / p).resolve()}"' if '\\' in p or '/' in p else p for p in libraries if not p.startswith('%')]
response = out / 'preview.rsp'
response.write_text('\n'.join(arguments))
batch = out / 'build.cmd'
batch.write_text(f'@echo off\ncall "{vcvars}" >nul\nif errorlevel 1 exit /b 1\ncl @"{response}"\n')
flags = subprocess.CREATE_NO_WINDOW if os.name == 'nt' else 0
compiled = subprocess.run(['cmd.exe', '/d', '/c', str(batch)], cwd=root, creationflags=flags, capture_output=True, text=True)
print(compiled.stdout + compiled.stderr)
compiled.check_returncode()
run = subprocess.run([str(out / 'preview.exe'), str(root)], cwd=out, creationflags=flags, capture_output=True, text=True)
(out / 'preview.log').write_text(run.stdout + run.stderr)
print(run.stdout + run.stderr)
run.check_returncode()
