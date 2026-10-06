"""Small consistent outline icons, supersampled and saved as native RmlUi TGA assets."""
from pathlib import Path
import math
import struct

out = Path(__file__).resolve().parent.parent / 'Assets/UI/Icons'
out.mkdir(parents=True, exist_ok=True)
paths = {
    'ammo': [[(5,25),(5,10),(8,5),(11,10),(11,25),(5,25)], [(15,25),(15,10),(18,5),(21,10),(21,25),(15,25)], [(25,25),(25,10),(28,5),(31,10),(31,25),(25,25)]],
    'health': [[(13,5),(21,5),(21,13),(29,13),(29,21),(21,21),(21,29),(13,29),(13,21),(5,21),(5,13),(13,13),(13,5)]],
    'armour': [[(17,4),(28,9),(26,22),(17,30),(8,22),(6,9),(17,4)]],
    'grenade': [[(13,10),(13,5),(21,5),(21,10)], [(12,11),(23,11),(28,18),(27,26),(22,30),(12,30),(7,26),(6,18),(12,11)], [(21,5),(27,7),(28,12)]],
    'objective': [[(17,2),(32,17),(17,32),(2,17),(17,2)], [(17,11),(17,20)], [(17,24),(17,25)]],
    'arrow': [[(5,17),(29,17)], [(22,10),(29,17),(22,24)]],
}

def distance(px, py, a, b):
    dx, dy = b[0]-a[0], b[1]-a[1]
    t = max(0, min(1, ((px-a[0])*dx+(py-a[1])*dy)/(dx*dx+dy*dy or 1)))
    return math.hypot(px-a[0]-t*dx, py-a[1]-t*dy)

for name, strokes in paths.items():
    segments = [(a,b) for stroke in strokes for a,b in zip(stroke,stroke[1:])]
    pixels = bytearray()
    for y in range(36):
        for x in range(36):
            coverage = sum(min(distance(x+(sx+.5)/3, y+(sy+.5)/3, a,b) for a,b in segments) < .65 for sy in range(3) for sx in range(3))
            pixels.extend((245,244,242,round(coverage/9*255)))
    header = struct.pack('<BBBHHBHHHHBB',0,0,2,0,0,0,0,0,36,36,32,40)
    (out / f'{name}.tga').write_bytes(header + pixels)
print('Created six tactical outline icons.')
