"""Compile RmlUi-compatible tokens and register UI resources (no runtime CSS variables)."""
import json
from pathlib import Path
from string import Template
import uuid
ROOT = Path(__file__).resolve().parent.parent
ASSETS = ROOT / 'Assets'
UI = ASSETS / 'UI'
tokens = json.loads((UI / 'theme.tokens.json').read_text())
for source in UI.glob('*.rcss.in'):
    source.with_suffix('').write_text(Template(source.read_text()).substitute(tokens))
manifest = ROOT / 'CoDplutoproject.plutoproject'
lines = manifest.read_text().splitlines()
paths = list(UI.glob('*')) + list((ASSETS / 'Fonts').glob('*')) + list((UI / 'Icons').glob('*.tga')) + list((UI / 'Weapons').glob('*')) + list((UI / 'Artwork').glob('*'))
paths.append(ASSETS / 'Scripts/TacticalUi.cs')
for path in paths:
    if not path.is_file() or path.suffix in ['.plutometa', '.in']: continue
    meta = Path(str(path) + '.plutometa')
    if not meta.exists(): meta.write_text(f'PLUTOASSET\t1\nID\t{uuid.uuid5(uuid.NAMESPACE_URL, "cod/ui/" + path.relative_to(ASSETS).as_posix()).hex}\nIMPORTER_VERSION\t1\n')
    reference = 'project://' + path.relative_to(ASSETS).as_posix()
    previous = next((line for line in lines if line.startswith('ASSET\t' + reference + '\t')), None)
    kind = previous.split('\t')[-1] if previous else 'Texture' if path.suffix in ['.png', '.tga'] else 'Script' if path.suffix == '.cs' else 'Unknown'
    lines = [line for line in lines if not line.startswith('ASSET\t' + reference + '\t')]
    lines.append(f'ASSET\t{reference}\t{path.stat().st_size}\t{kind}')
manifest.write_text('\n'.join(lines) + '\n')
print('Compiled shared tactical theme and registered UI/font assets.')
