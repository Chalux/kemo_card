"""Copy approved preview artwork into Godot assets and declare its idle frames."""
import json
from pathlib import Path
import shutil
from PIL import Image

source = Path(__file__).resolve().parent
project = source.parents[3]
destination = project / "Resource/Assets/Characters/chalux"
destination.mkdir(parents=True, exist_ok=True)
manifest = json.loads((source / "manifest.json").read_text(encoding="utf-8"))
assert manifest["frameCount"] == 32 and manifest["firstLastIdentical"]
width, height = 464, 560
columns = 8
atlas = Image.new('RGBA', (width * columns, height * 4))
for index in range(32):
    frame = Image.open(source / 'frames' / f'idle-{index + 1:02}.png').convert('RGBA')
    atlas.paste(frame, (index % columns * width, index // columns * height))
atlas.save(destination / 'idle.png')
shutil.copy2(project / "output/imagegen/crux-suit-right-v5.png", destination / "portrait.png")
count = manifest["frameCount"]
lines = [
    f'[gd_resource type="SpriteFrames" load_steps={count + 2} format=3]',
    '',
    '[ext_resource type="Texture2D" path="res://Resource/Assets/Characters/chalux/idle.png" id="1_idle"]',
    '',
]
for index in range(count):
    lines += [
        f'[sub_resource type="AtlasTexture" id="AtlasTexture_idle_{index + 1:02d}"]',
        'atlas = ExtResource("1_idle")',
        f'region = Rect2({index % columns * width}, {index // columns * height}, {width}, {height})',
        'filter_clip = true',
        '',
    ]
# The torso anchor is x=264 in the 432px frame. The fluffy tail occupies the
# left side, so centering the entire transparent canvas pushes the body right.
presentation_offset = (width / 2 - 264, 0)
lines += ['[resource]',
          f'metadata/presentation_offset = Vector2({presentation_offset[0]:g}, {presentation_offset[1]:g})',
          'animations = [{', '"frames": [']
for index in range(count):
    lines += ['{', '"duration": 1.0,',
              f'"texture": SubResource("AtlasTexture_idle_{index + 1:02d}")',
              '},' if index < count - 1 else '}']
lines += ['],', '"loop": true,', '"name": &"idle",',
          f'"speed": {float(manifest["fps"]):.1f}', '}]', '']
(destination / "animations.tres").write_text("\n".join(lines), encoding="utf-8")
print(f"Packaged {count} idle frames at {manifest['fps']} FPS: {destination}")
