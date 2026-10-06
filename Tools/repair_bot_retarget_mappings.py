"""Clear soldier-only source overrides to allow UAL humanoid retargeting."""
from pathlib import Path
import struct
ROOT=Path(__file__).resolve().parent.parent

def repair(path):
    data=path.read_bytes()
    marker=struct.pack('<QBQ',22,0,4)+b'Hips'
    if marker not in data:
        if struct.pack('<QBQ',22,0,0) in data:
            return # Already repaired.
        raise ValueError(f'Missing soldier mapping block: {path}')
    if data.count(marker)!=1: raise ValueError('Ambiguous mapping block')
    start=data.index(marker); pos=start+8; output=bytearray(data[start:pos])
    for bone in range(22):
        kind=data[pos]; pos+=1
        if kind!=bone: raise ValueError('Unexpected humanoid slot')
        size=struct.unpack_from('<Q',data,pos)[0]; pos+=8
        name=data[pos:pos+size].decode(); pos+=size
        if not name: raise ValueError('Unexpected empty override')
        output+=struct.pack('<BQ',kind,0)
        output+=data[pos:pos+21]; pos+=21 # joint, rotation, translation flag, scale
    path.write_bytes(data[:start]+output+data[pos:])

if __name__=='__main__':
    for name in ('Soldier.plutomesh','Soldier_Game.plutomesh'):
        repair(ROOT/'Assets/Bots/Soldier'/name)
