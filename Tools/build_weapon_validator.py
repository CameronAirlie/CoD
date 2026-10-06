"""Compile the native asset validator using an existing PlutoGE MSVC build.

Usage: python Tools/build_weapon_validator.py <engine-build-directory> <vcvars64.bat>
Reuses the engine test target's include/library settings; never changes the engine.
"""
import os
import shutil
import subprocess
import sys
import xml.etree.ElementTree as ET
from pathlib import Path

root = Path(__file__).resolve().parent.parent
build = Path(sys.argv[1]).resolve()
vcvars = Path(sys.argv[2]).resolve()
out = root / 'Tools/Native'
out.mkdir(exist_ok=True)
project = build / 'tests/PlutoGEImportedAssetLifetimeTests.vcxproj'
namespace = {'m': 'http://schemas.microsoft.com/developer/msbuild/2003'}
tree = ET.parse(project)
group = next(group for group in tree.findall('m:ItemDefinitionGroup', namespace)
             if 'RelWithDebInfo|x64' in group.get('Condition', ''))
includes = group.find('m:ClCompile/m:AdditionalIncludeDirectories', namespace).text.split(';')
libraries = group.find('m:Link/m:AdditionalDependencies', namespace).text.split(';')
arguments = ['/nologo', '/std:c++20', '/EHsc', '/MD', '/O2', '/DNDEBUG', '/DGLM_ENABLE_EXPERIMENTAL',
             f'/Fo"{out / "verify.obj"}"', f'/Fe"{out / "verify.exe"}"']
arguments += [f'/I"{path}"' for path in includes if not path.startswith('%')]
arguments += [f'"{root / "Tools/verify_weapon_assets.cpp"}"', '/link']
arguments += [f'"{(project.parent / path).resolve()}"' if '\\' in path or '/' in path else path
              for path in libraries if not path.startswith('%')]
response = out / 'verify.rsp'
response.write_text('\n'.join(arguments), encoding='utf-8')
batch = out / 'build-validator.cmd'
batch.write_text(f'@echo off\ncall "{vcvars}" >nul\nif errorlevel 1 exit /b 1\ncl @"{response}"\n', encoding='utf-8')
flags = subprocess.CREATE_NO_WINDOW if os.name == 'nt' else 0
compiled = subprocess.run(['cmd.exe', '/d', '/c', str(batch)], cwd=root, creationflags=flags, capture_output=True, text=True)
print(compiled.stdout, end='')
print(compiled.stderr, end='')
compiled.check_returncode()
for file in (build / 'runtime/RelWithDebInfo').glob('*.dll'):
    shutil.copy2(file, out / file.name)
verified = subprocess.run([str(out / 'verify.exe'), str(root)], cwd=out, creationflags=flags, capture_output=True, text=True)
print(verified.stdout, end='')
print(verified.stderr, end='')
verified.check_returncode()
