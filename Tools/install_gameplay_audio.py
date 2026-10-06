import struct, uuid, sys, re
from pathlib import Path
root = Path(__file__).resolve().parents[1]
assets = root / 'Assets'
sys.path.insert(0, str(root / 'Build/audio_tools'))
import soundfile as sf
def write(p, s):
    p.parent.mkdir(parents=True, exist_ok=True)
    p.write_text(s, encoding='utf-8', newline='\r\n')
    if p.suffix == '.plutoproject': return
    meta = Path(str(p) + '.plutometa')
    if not meta.exists():
        meta.write_text('PLUTOASSET\t1\nID\t' + uuid.uuid5(uuid.NAMESPACE_URL, 'cod-audio/' + p.relative_to(root).as_posix()).hex + '\nIMPORTER_VERSION\t1\n')

def patch_reload(path):
    data = path.read_bytes(); offset = 8
    def read(fmt):
        nonlocal offset
        value = struct.unpack_from('<' + fmt, data, offset); offset += struct.calcsize('<' + fmt); return value
    def string():
        nonlocal offset
        n, = read('Q'); offset += n
    magic, version = struct.unpack_from('<II', data)
    assert magic == 0x4347504c and version == 5
    string(); duration, count, channels = read('fiQ')
    for _ in range(channels):
        read('iii'); string()
        for _ in range(2):
            present, = read('B')
            if present: offset += 64
        _, _, times = read('IIQ'); offset += times * 4
        values, = read('Q'); offset += values * 16
    event_offset = offset
    existing = []
    event_count, = read('Q')
    for _ in range(event_count):
        start = offset
        time, = read('f')
        n, = read('Q'); name = data[offset:offset+n].decode(); offset += n
        string(); read('fi')
        if name not in ('ReloadMagOut', 'ReloadMagIn', 'ReloadRack', 'ReloadFinish'):
            existing.append((time, data[start:offset]))
    # Replace only the event tail; preserve baked animation channels byte-for-byte.
    events = [(duration*.18, 'ReloadMagOut'), (duration*.74, 'ReloadMagIn'), (duration*.80, 'ReloadRack'), (duration*.86, 'ReloadFinish')]
    encoded_events = existing[:]
    for time, name in events:
        encoded = name.encode(); encoded_events.append((time, struct.pack('<fQ', time, len(encoded)) + encoded + struct.pack('<Qfi', 0, 0., 0)))
    tail = struct.pack('<Q', len(encoded_events)) + b''.join(event for _, event in sorted(encoded_events))
    path.write_bytes(data[:event_offset] + tail)

for kind in ('ar', 'pistol', 'lmg'): patch_reload(assets / f'Weapons/{kind}/Reload.plutoclip')
patch_reload(assets / 'SourceModels/animation_low-poly_val/LVA4_Armature_wpn_val_reload.plutoclip')
# The pinned engine decodes RIFF/WAVE, so keep original pack assets and derive PCM copies.
selected = ['Firearms/Fire/assault_rifle_fire_ufx_1', 'Firearms/Fire/pistol_fire_ufx_1', 'Firearms/Fire/machinegun_heavy_fire_ufx_1', 'Firearms/Misc/pistol_universal_empty_barrel_ufx_1', 'Firearms/Misc/rifle_universal_empty_barrel_ufx_1', 'Misc/item_pickup_ufx_1', 'Misc/medkit_use_ufx_1', 'Misc/item_equip_ufx_1', 'Melee/Throw/throw_heavy_ufx_1', 'Explosions/grenade_ufx_1', 'Impact & Break/Concrete/impact_concrete_ufx_1']
selected += [f'Firearms/Reloading/{weapon}_reloading_mag_{stage}_ufx_1' for weapon in ('pistol', 'assault_rifle') for stage in ('in', 'out')]
selected += [f'Firearms/Racking/{weapon}_racking_ufx_1' for weapon in ('pistol', 'assault_rifle')]
selected += [f'Player/Footsteps/Concrete/footstep_concrete_ufx_{i}' for i in range(1, 5)]
selected += [f'Impact & Break/Body/impact_body_ufx_{i}' for i in range(1, 3)]
selected += ['Misc/inventory_open_ufx_1', 'Misc/inventory_close_ufx_1', 'Misc/new_objective_ufx_1']
for name in selected:
    source = assets / 'Sounds/SFX Library/Sound Effects' / (name + '.ogg')
    if not source.exists(): source = source.with_suffix('.wav')
    target = assets / 'Sounds/Gameplay' / (name + '.wav')
    target.parent.mkdir(parents=True, exist_ok=True)
    samples, rate = sf.read(source, dtype='float32')
    sf.write(target, samples, rate, subtype='PCM_16')
    meta = Path(str(target) + '.plutometa')
    if not meta.exists(): meta.write_text('PLUTOASSET\t1\nID\t' + uuid.uuid5(uuid.NAMESPACE_URL, name).hex + '\nIMPORTER_VERSION\t1\n')
write(assets / 'Prefabs/Audio/GameplayCue.plutoprefab', 'SCENE\t1\nENTITY\t1\t0\t1\tGameplay Audio\t0,0,0\t0,0,0\t1,1,1\nCOMPONENT\t1\tSoundEmitterComponent\t1\nPROPERTY\tPlayOnAwake\t4\tfalse\t0\nPROPERTY\tLooping\t4\tfalse\t0\nPROPERTY\tSpatialized\t4\tfalse\t0\nPROPERTY\tVolume\t0\t1\t0\nPROPERTY\tPitch\t0\t1\t0\nEND_COMPONENT\n')
project = root / 'CoDplutoproject.plutoproject'
s = project.read_text(encoding='utf-8')
for p, kind in [(assets/'Scripts/GameplaySounds.cs', 'Script'), (assets/'Prefabs/Audio/GameplayCue.plutoprefab', 'Prefab')] + [(p, 'Audio') for p in (assets/'Sounds/Gameplay').rglob('*.wav')]:
    ref = 'project://' + p.relative_to(assets).as_posix()
    if f'ASSET\t{ref}\t' not in s: s += f'ASSET\t{ref}\t{p.stat().st_size}\t{kind}\n'
    else: s = re.sub(r'(ASSET\t' + re.escape(ref) + r'\t)\d+', lambda m: m[1] + str(p.stat().st_size), s)
    if kind == 'Script':
        meta = Path(str(p) + '.plutometa')
        if not meta.exists(): meta.write_text('PLUTOASSET\t1\nID\t' + uuid.uuid5(uuid.NAMESPACE_URL, ref).hex + '\nIMPORTER_VERSION\t1\n')
for p in [assets / f'Weapons/{kind}/Reload.plutoclip' for kind in ('ar', 'pistol', 'lmg')] + [assets / 'SourceModels/animation_low-poly_val/LVA4_Armature_wpn_val_reload.plutoclip']:
    ref = 'project://' + p.relative_to(assets).as_posix()
    s = re.sub(r'(ASSET\t' + re.escape(ref) + r'\t)\d+', lambda m: m[1] + str(p.stat().st_size), s)
write(project, s)
print('Installed reload keyframe events, cue prefab and audio asset registration.')
