"""Blender conversion of checked-in menu artwork to the uncompressed RmlUi TGA format."""
from pathlib import Path
import bpy
root = Path(__file__).resolve().parent.parent
image = bpy.data.images.load(str(root / 'Assets/UI/Artwork/menu-background.png'))
_ = image.pixels[0]  # Decode before changing the image's source path.
image.file_format = 'TARGA_RAW'
image.filepath_raw = str(root / 'Assets/UI/Artwork/menu-background.tga')
image.save()
