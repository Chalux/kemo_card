"""Mechanical extraction/scale/registration of built-in image_gen poses.

Preview assets only: does not write the game's existing SpriteFrames resource.
"""
from pathlib import Path
import json
import numpy as np
from PIL import Image, ImageDraw, ImageFont

HERE = Path(__file__).resolve().parent
SIZE = (640, 560)
BASE = Image.new('RGBA', SIZE)
BASE.paste(Image.open(HERE.parent / 'idle-suit-v9/frames/idle-01.png').convert('RGBA'), (0, 0))
SPECS = {
    'attack': {'rows': [0, 365, 730, 1086], 'step': 362, 'scale': 1.43,
               'times': [80, 80, 90, 90, 100, 60, 50, 60, 70, 90, 90, 120]},
    'defeat': {'rows': [0, 388, 716, 1024], 'step': 384, 'scale': 1.38,
               'times': [100, 100, 110, 100, 120, 130, 140, 130, 120, 100, 100, 300]},
}

def save_gif(frames, times, path):
    rgb = []
    for im in frames:
        bg = Image.new('RGBA', SIZE, '#172337')
        bg.alpha_composite(im)
        rgb.append(bg.convert('RGB'))
    contact = Image.new('RGB', (SIZE[0]*12, SIZE[1]))
    for i, im in enumerate(rgb):
        contact.paste(im, (i*SIZE[0], 0))
    pal = contact.quantize(colors=256, dither=Image.Dither.NONE)
    quantized = [im.quantize(palette=pal, dither=Image.Dither.NONE) for im in rgb]
    preview_times = times[:-1] + [max(900, times[-1])]
    quantized[0].save(path, save_all=True, append_images=quantized[1:],
                      duration=preview_times, loop=0, optimize=False, disposal=2)

all_frames = {}
manifest = {'mode': 'built-in image_gen', 'status': 'preview-only-awaiting-review',
            'frameSize': SIZE, 'bodyAnchor': [264, 280], 'gameAssetsReplaced': False,
            'animations': {}}
for name, spec in SPECS.items():
    sheet = Image.open(HERE/name/'generated-sheet.png').convert('RGBA')
    frames = []
    records = []
    for i in range(12):
        row, col = divmod(i, 4)
        x = col*spec['step']
        top, bottom = spec['rows'][row:row+2]
        width = spec['step']
        # Frame 6 anticipation and frame 7 strike extend into the empty gutter.
        # Stop before the neighboring tail, keeping the full blade and arc.
        if name == 'attack' and i == 5:
            width = 390
        if name == 'attack' and i == 6:
            width = 400
        raw = sheet.crop((x, top, x+width, bottom))
        a = np.array(raw).astype(int)
        # Remove only neighboring pose fragments in the extraction gutters.
        if name == 'attack' and i == 6:
            a[:200, :28] = 0
        if name == 'attack' and i == 7:
            a[:, :44] = 0
        raw = Image.fromarray(a.astype('uint8'))
        dx = -10
        if name == 'attack':
            yy, xx = np.indices(a.shape[:2])
            blue = ((a[:,:,3] > 128) & (a[:,:,2] > a[:,:,0]+45)
                    & (a[:,:,2] > a[:,:,1]+20) & (yy > 326) & (xx < 195))
            fy, fx = np.where(blue)
            left_center = (int(fx.min())+int(fx.max()))/2
            dx = round(202-left_center*spec['scale'])
        # A single scale per animation preserves body/limb proportions; the
        # defeat pose becomes shorter through knee/spine motion, not scaling.
        scaled = raw.resize((round(raw.width*spec['scale']), round(raw.height*spec['scale'])),
                            Image.Resampling.NEAREST)
        solid_y, solid_x = np.where(np.array(scaled)[:,:,3] > 128)
        dy = 535-int(solid_y.max())
        im = Image.new('RGBA', SIZE)
        im.paste(scaled, (dx, dy))
        frames.append(im)
        records.append({'sourceFrame': i+1, 'sourceRect': [x, top, width, bottom-top],
                        'scale': spec['scale'], 'registration': [dx, dy]})
    frames[0] = BASE.copy()
    if name == 'attack':
        frames[-1] = BASE.copy()
        assert frames[0].tobytes() == frames[-1].tobytes()
    folder = HERE/name/'frames'
    folder.mkdir(exist_ok=True)
    atlas = Image.new('RGBA', (SIZE[0]*4, SIZE[1]*3))
    contact = Image.new('RGBA', atlas.size, '#172337')
    for i, im in enumerate(frames):
        im.save(folder/f'{name}-{i+1:02}.png')
        pos = (i%4*SIZE[0], i//4*SIZE[1])
        atlas.paste(im, pos)
        contact.alpha_composite(im, pos)
    atlas.save(HERE/name/f'{name}-sheet.png')
    contact.resize((1280, 840), Image.Resampling.NEAREST).save(HERE/name/'qa-contact.png')
    save_gif(frames, spec['times'], HERE/name/f'{name}-preview.gif')
    all_frames[name] = frames
    manifest['animations'][name] = {'frameCount': 12, 'durationMs': spec['times'],
                                    'totalMs': sum(spec['times']), 'loop': False, 'extraction': records}
(HERE/'manifest.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding='utf-8')

font = ImageFont.truetype('C:/Windows/Fonts/msyh.ttc', 20)
panels = []
for tick in range(56):
    canvas = Image.new('RGBA', (960, 465), '#172337')
    draw = ImageDraw.Draw(canvas)
    for j, name in enumerate(SPECS):
        elapsed = tick*50
        idx = 11
        total = 0
        for i, ms in enumerate(SPECS[name]['times']):
            total += ms
            if elapsed < total:
                idx = i
                break
        draw.text((j*480+18, 8), '攻击 v2' if name=='attack' else '战败 v2', font=font, fill='#edf2ff')
        im = all_frames[name][idx].resize((480,420), Image.Resampling.NEAREST)
        canvas.alpha_composite(im, (j*480, 40))
    panels.append(canvas.convert('RGB'))
contact = Image.new('RGB', (960*12,465))
for i in range(12):
    contact.paste(panels[i*3], (960*i,0))
palette = contact.quantize(colors=256, dither=Image.Dither.NONE)
q = [im.quantize(palette=palette,dither=Image.Dither.NONE) for im in panels]
q[0].save(HERE/'two-actions-preview.gif', save_all=True, append_images=q[1:],
           duration=50, loop=0, optimize=False, disposal=2)
panels[13].save(HERE/'preview-still.png')
print('Preview packaged: attack 12 frames, defeat 12 frames; game assets unchanged.')
