"""Mechanically slice generated key poses, stabilize plates, and package Godot frames.

No artwork is painted here. The approved idle supplies unchanged pixels; new
poses come from built-in image_gen. The generator spaced columns unevenly,
so extraction uses measured column origins and foot anchors.
"""
from pathlib import Path
import json
import shutil
import numpy as np
from PIL import Image, ImageDraw, ImageFont

HERE = Path(__file__).resolve().parent
PROJECT = HERE.parents[3]
IDLE = HERE.parent / 'idle-suit-v9'
DEST = PROJECT / 'Resource/Assets/Characters/chalux'
SIZE = (464, 560)
BASE = Image.new('RGBA', SIZE)
BASE.paste(Image.open(IDLE / 'frames/idle-01.png').convert('RGBA'), (0, 0))

# Eight timeline slots; repeated key poses give deliberate anticipation/impact
# holds, rather than using generated poses whose weapons intersect neighbors.
SPECS = {
    'enter': ([0, 1, 2, 3, 4, 5, 6, 7], [100, 100, 120, 120, 140, 120, 120, 140]),
    'attack': ([0, 1, 2, 2, 3, 3, 6, 7], [80, 120, 100, 80, 60, 80, 120, 140]),
    'buff': ([0, 1, 2, 3, 3, 5, 6, 7], [100, 140, 140, 140, 180, 140, 140, 140]),
    'defeat': ([0, 1, 2, 3, 4, 5, 6, 6], [100, 140, 140, 180, 180, 180, 180, 300]),
}

def pose(name, index):
    sheet = Image.open(HERE / name / 'generated-sheet.png').convert('RGBA')
    origin = [0, 352, 736, 1088][index % 4]
    raw = sheet.crop((origin, index // 4 * 512,
                      origin + 432, index // 4 * 512 + 512))
    a = np.array(raw)
    # Transparent RGB from imagegen is preserved by PNG but excluded from QA.
    # Remove neighboring feet/tails in overlapping extraction windows.
    if index % 4 > 0:
        a[:, :40 if name == 'attack' else 32] = 0
    if index % 4 < 3:
        a[265:, 396:] = 0
    yy, xx = np.indices(a.shape[:2])
    blue = ((a[:, :, 3] > 128)
            & (a[:, :, 2].astype(int) > a[:, :, 0].astype(int) + 30)
            & (a[:, :, 2].astype(int) > a[:, :, 1].astype(int) + 15))
    feet = blue & (yy > 455) & (xx > 90) & (xx < 230)
    fy, fx = np.where(feet)
    dx, dy = 24, 24
    if name != 'defeat' or index < 3:
        if len(fx):
            dx += 178 - round((int(fx.min()) + int(fx.max())) / 2)
    if name == 'defeat':
        solid_y, _ = np.where(a[:, :, 3] > 128)
        dy += 511 - int(solid_y.max())
    result = Image.new('RGBA', SIZE)
    result.paste(Image.fromarray(a), (dx, dy))
    return result

def plate(name, image):
    if name == 'buff':
        # Only replace the free arm and its explicit particles. Head, sword,
        # torso, trousers, tail and paws retain the approved idle colors.
        mask = Image.new('L', SIZE)
        ImageDraw.Draw(mask).polygon([(305, 158), (350, 158), (463, 158),
                                     (463, 410), (330, 410), (305, 305)], fill=255)
        return Image.composite(image, BASE, mask)
    if name == 'enter':
        mask = Image.new('L', SIZE)
        draw = ImageDraw.Draw(mask)
        draw.rectangle((0, 0, 463, 177), fill=255)
        draw.polygon([(0, 295), (180, 295), (192, 420),
                      (160, 490), (0, 490)], fill=255)
        return Image.composite(image, BASE, mask)
    if name == 'attack':
        # Locked ground contact: the sword is above this strip in all selected
        # poses, so the standing paws can remain pixel-identical to idle.
        image.paste(BASE.crop((0, 496, 464, 560)), (0, 496))
    return image

def preview(frames, times, path):
    rgb = []
    for im in frames:
        bg = Image.new('RGBA', im.size, '#172337')
        bg.alpha_composite(im)
        rgb.append(bg.convert('RGB'))
    contact = Image.new('RGB', (SIZE[0] * len(rgb), SIZE[1]))
    for i, im in enumerate(rgb):
        contact.paste(im, (i * SIZE[0], 0))
    palette = contact.quantize(colors=256, dither=Image.Dither.NONE)
    imgs = [im.quantize(palette=palette, dither=Image.Dither.NONE) for im in rgb]
    imgs[0].save(path, save_all=True, append_images=imgs[1:], duration=times,
                 loop=0, disposal=2, optimize=False)

all_frames = {}
manifest = {'mode': 'built-in image_gen', 'frameSize': SIZE,
            'bodyAnchor': [264, 280], 'presentationOffset': [-32, 0],
            'rootMotion': False, 'idlePixelsPreserved': True, 'animations': {}}
DEST.mkdir(parents=True, exist_ok=True)
for name, (indices, times) in SPECS.items():
    frames = [plate(name, pose(name, i)) for i in indices]
    if name == 'buff':
        # Reuse the approved delayed tail sway rather than regenerate its shades.
        tail_mask = Image.new('L', SIZE)
        ImageDraw.Draw(tail_mask).polygon([(0, 305), (183, 305), (194, 414),
                                          (160, 490), (0, 490)], fill=255)
        for i, sample in enumerate([1, 5, 9, 13, 17, 21, 25, 1]):
            tail = Image.new('RGBA', SIZE)
            tail.paste(Image.open(IDLE / 'frames' / f'idle-{sample:02}.png').convert('RGBA'), (0, 0))
            frames[i] = Image.composite(tail, frames[i], tail_mask)
    if name != 'enter':
        frames[0] = BASE.copy()
    if name in ('enter', 'attack', 'buff'):
        frames[-1] = BASE.copy()
    if name == 'enter':
        # In-place reveal; pixel coordinates never interpolate or scale.
        for i, opacity in enumerate((0.35, 0.65, 0.85)):
            alpha = frames[i].getchannel('A').point(lambda v: round(v * opacity))
            frames[i].putalpha(alpha)
    if name == 'defeat':
        frames[-1] = frames[-2].copy()
    folder = HERE / name / 'frames'
    folder.mkdir(exist_ok=True)
    atlas = Image.new('RGBA', (SIZE[0] * 4, SIZE[1] * 2))
    for i, im in enumerate(frames):
        im.save(folder / f'{name}-{i + 1:02}.png')
        atlas.paste(im, (i % 4 * SIZE[0], i // 4 * SIZE[1]))
    atlas.save(HERE / name / f'{name}-sheet.png')
    shutil.copy2(HERE / name / f'{name}-sheet.png', DEST / f'{name}.png')
    preview(frames, times, HERE / name / f'{name}-preview.gif')
    all_frames[name] = frames
    manifest['animations'][name] = {'frames': len(frames), 'sourceIndicesZeroBased': indices,
                                    'durationMs': times, 'totalMs': sum(times), 'loop': False}
    assert all(im.size == SIZE for im in frames)
    if name in ('attack', 'buff'):
        assert frames[0].tobytes() == frames[-1].tobytes() == BASE.tobytes()
    if name == 'defeat':
        assert frames[-1].tobytes() == frames[-2].tobytes()

idle_frames = []
idle_atlas = Image.new('RGBA', (SIZE[0] * 8, SIZE[1] * 4))
for i in range(32):
    original = Image.open(IDLE / 'frames' / f'idle-{i + 1:02}.png').convert('RGBA')
    im = Image.new('RGBA', SIZE)
    im.paste(original, (0, 0))
    assert im.crop((0, 0, 432, 560)).tobytes() == original.tobytes()
    idle_frames.append(im)
    idle_atlas.paste(im, (i % 8 * SIZE[0], i // 8 * SIZE[1]))
idle_atlas.save(DEST / 'idle.png')
shutil.copy2(PROJECT / 'output/imagegen/crux-suit-right-v5.png', DEST / 'portrait.png')
manifest['animations']['idle'] = {'frames': 32, 'durationMs': [100] * 32, 'totalMs': 3200, 'loop': True}
(HERE / 'manifest.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding='utf-8')

# A fixed shared canvas makes scale and body centering identical across states.
names = ['enter', 'idle', 'attack', 'buff', 'defeat']
count = sum(manifest['animations'][n]['frames'] for n in names)
lines = [f'[gd_resource type="SpriteFrames" load_steps={count + len(names) + 1} format=3]', '']
for n, name in enumerate(names, 1):
    lines += [f'[ext_resource type="Texture2D" path="res://Resource/Assets/Characters/chalux/{name}.png" id="{n}_{name}"]']
for n, name in enumerate(names, 1):
    cols = 8 if name == 'idle' else 4
    for i in range(manifest['animations'][name]['frames']):
        lines += ['', f'[sub_resource type="AtlasTexture" id="AtlasTexture_{name}_{i + 1:02}"]',
                  f'atlas = ExtResource("{n}_{name}")',
                  f'region = Rect2({i % cols * SIZE[0]}, {i // cols * SIZE[1]}, {SIZE[0]}, {SIZE[1]})',
                  'filter_clip = true']
lines += ['', '[resource]', 'metadata/presentation_offset = Vector2(-32, 0)', 'animations = [']
for n, name in enumerate(names):
    spec = manifest['animations'][name]
    lines += ['{', '"frames": [']
    for i, ms in enumerate(spec['durationMs']):
        lines += ['{', f'"duration": {ms / 100:.2f},',
                  f'"texture": SubResource("AtlasTexture_{name}_{i + 1:02}")',
                  '},' if i < spec['frames'] - 1 else '}']
    lines += ['],', f'"loop": {str(spec["loop"]).lower()},', f'"name": &"{name}",',
              '"speed": 10.0', '},' if n < len(names) - 1 else '}']
lines += [']', '']
(DEST / 'animations.tres').write_text('\n'.join(lines), encoding='utf-8')

# Export a synchronized four-panel contact preview; post-action holds make the
# non-looping game states reviewable in a repeating GIF.
font_path = Path('C:/Windows/Fonts/msyh.ttc')
font = ImageFont.truetype(str(font_path), 20)
labels = {'enter': '入场', 'attack': '攻击', 'buff': 'Buff / 治疗', 'defeat': '战败'}
panels = []
for tick in range(44):
    canvas = Image.new('RGBA', (656, 860), '#172337')
    draw = ImageDraw.Draw(canvas)
    for j, name in enumerate(SPECS):
        time = tick * 50
        total = 0
        index = 7
        for i, ms in enumerate(SPECS[name][1]):
            total += ms
            if time < total:
                index = i
                break
        im = all_frames[name][index].resize((325, 392), Image.Resampling.NEAREST)
        x, y = j % 2 * 328, j // 2 * 430
        draw.text((x + 14, y + 8), labels[name], font=font, fill='#edf2ff')
        canvas.alpha_composite(im, (x, y + 34))
    panels.append(canvas.convert('RGB'))
palette_contact = Image.new('RGB', (656 * 8, 860))
for i in range(8):
    palette_contact.paste(panels[i * 5], (656 * i, 0))
palette = palette_contact.quantize(colors=256, dither=Image.Dither.NONE)
quantized = [im.quantize(palette=palette, dither=Image.Dither.NONE) for im in panels]
quantized[0].save(HERE / 'four-actions-preview.gif', save_all=True,
                  append_images=quantized[1:], duration=50, loop=0, optimize=False)
panels[10].save(HERE / 'preview-still.png')
print(json.dumps(manifest, ensure_ascii=False))
