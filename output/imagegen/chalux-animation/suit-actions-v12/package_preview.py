"""Extract AI-generated poses and register whole images for review; no painting."""
from pathlib import Path
import json
import numpy as np
from PIL import Image, ImageDraw, ImageFont

HERE = Path(__file__).resolve().parent
SIZE = (800, 592)
BASE = Image.new('RGBA', SIZE)
BASE.paste(Image.open(HERE.parent/'idle-suit-v9/frames/idle-01.png').convert('RGBA'), (100, 0))
SPECS = {
    'attack': {'rows': [0, 317, 629, 943, 1254], 'scale': 1.65,
               'times': [70, 70, 80, 80, 90, 40, 40, 40, 50, 60, 60, 70, 70, 70, 70, 120]},
    'defeat': {'rows': [0, 362, 674, 974, 1238], 'scale': 1.50,
               'times': [80, 100, 100, 80, 60, 60, 100, 100, 100, 90, 90, 90, 100, 100, 100, 300]},
}

def solid(im):
    a = np.array(im)
    yy, xx = np.where(a[:, :, 3] > 128)
    return [int(xx.min()), int(yy.min()), int(xx.max())+1, int(yy.max())+1]

def palette_gif(frames, times, path):
    rgb = []
    for im in frames:
        bg = Image.new('RGBA', im.size, '#172337')
        bg.alpha_composite(im)
        rgb.append(bg.convert('RGB'))
    # A shared palette avoids quantization flicker between frames.
    strip = Image.new('RGB', (rgb[0].width*len(rgb), rgb[0].height))
    for i, im in enumerate(rgb):
        strip.paste(im, (i*im.width, 0))
    pal = strip.quantize(colors=256, dither=Image.Dither.NONE)
    q = [im.quantize(palette=pal, dither=Image.Dither.NONE) for im in rgb]
    q[0].save(path, save_all=True, append_images=q[1:], duration=times,
              loop=0, optimize=False, disposal=2)

all_frames = {}
manifest = {'mode': 'built-in image_gen', 'status': 'preview-only',
            'frameSize': SIZE, 'bodyAnchor': [364, 280],
            'gameAssetsReplaced': False, 'animations': {}}
for name, spec in SPECS.items():
    sheet = Image.open(HERE/name/'generated-sheet.png').convert('RGBA')
    frames, records = [], []
    for i in range(16):
        row, col = divmod(i, 4)
        nominal_left = round(col*sheet.width/4)
        left, right = nominal_left, round((col+1)*sheet.width/4)
        top, bottom = spec['rows'][row:row+2]
        # The horizontal sword crosses nominal cells into empty gutters.
        if name == 'attack':
            if i == 2: right = 894
            if i == 3: left = 894
            if i == 6: right = 916
            if i == 7: left = 916
            if i == 8: right = 341
            if i == 9: right = 670
        raw = sheet.crop((left, top, right, bottom))
        a = np.array(raw).astype(int)
        yy, xx = np.indices(a.shape[:2])
        gx, gy = xx+left, yy+top
        # Remove only fragments belonging to neighboring poses in the gutters.
        if name == 'attack' and i == 9:
            a[((gx < 341) & (gy < 790)) | ((gx >= 650) & (gy >= 790))] = 0
        if name == 'attack' and i == 8:
            a[(gx >= 329) & (gy >= 780)] = 0
        if name == 'attack' and i == 10:
            a[(gx < 670) & (gy < 790)] = 0
        raw = Image.fromarray(a.astype('uint8'))
        last_y = int(np.where(a[:, :, 3] > 128)[0].max())
        blue = ((a[:, :, 3] > 128) & (a[:, :, 2] > a[:, :, 0]+45)
                & (a[:, :, 2] > a[:, :, 1]+20) & (yy > last_y-14))
        local_x = gx-nominal_left
        blue &= (local_x < 180) if name == 'attack' else (local_x > 195)
        fy, fx = np.where(blue)
        foot_center = (int(fx.min())+int(fx.max()))/2
        foot_mask = ((a[:, :, 3] > 128) & (yy > last_y-28)
                     & (xx >= fx.min()-5) & (xx <= fx.max()+5))
        foot_bottom = int(np.where(foot_mask)[0].max())
        scale = spec['scale']
        scaled = raw.resize((round(raw.width*scale), round(raw.height*scale)), Image.Resampling.NEAREST)
        dx = round((300 if name == 'attack' else 437)-foot_center*scale)
        dy = round(535-(foot_bottom+1)*scale)+1
        im = Image.new('RGBA', SIZE)
        im.paste(scaled, (dx, dy))
        frames.append(im)
        records.append({'sourceFrame': i+1, 'sourceRect': [left, top, right-left, bottom-top],
                        'scale': scale, 'registration': [dx, dy],
                        'sourceFootCenter': foot_center, 'sourceFootBottom': foot_bottom})
    if name == 'defeat':
        tween_sheet = Image.open(HERE/name/'generated-transition.png').convert('RGBA')
        tweens, tween_records = [], []
        # These two generated poses fill the crouch-to-kneel gap. Later poses
        # already overlap the original kneeling frame and are not inserted.
        for i in (1, 2):
            left = round(i*tween_sheet.width/4)
            right = round((i+1)*tween_sheet.width/4)
            raw = tween_sheet.crop((left, 0, right, 480))
            a = np.array(raw).astype(int)
            yy, xx = np.indices(a.shape[:2])
            blue = ((a[:, :, 3] > 128) & (a[:, :, 2] > a[:, :, 0]+45)
                    & (a[:, :, 2] > a[:, :, 1]+20) & (yy > 410) & (xx > 220))
            fy, fx = np.where(blue)
            center = (int(fx.min())+int(fx.max()))/2
            foot_mask = ((a[:, :, 3] > 128) & (yy > 410)
                         & (xx >= fx.min()-5) & (xx <= fx.max()+5))
            bottom = int(np.where(foot_mask)[0].max())
            scale = 1.13
            scaled = raw.resize((round(raw.width*scale), round(raw.height*scale)), Image.Resampling.NEAREST)
            dx, dy = round(437-center*scale), round(535-(bottom+1)*scale)+1
            im = Image.new('RGBA', SIZE)
            im.paste(scaled, (dx, dy))
            tweens.append(im)
            tween_records.append({'sourceFile': 'generated-transition.png', 'sourceFrame': i+1,
                                  'sourceRect': [left, 0, right-left, 480], 'scale': scale,
                                  'registration': [dx, dy], 'sourceFootCenter': center,
                                  'sourceFootBottom': bottom})
        frames[4:4] = tweens
        records[4:4] = tween_records
        # Source row 3 starts with two redundant poses whose heads jump upward.
        # Keep the bow progressing through the already-generated poses instead.
        order = [0, 1, 2, 3, 4, 5, 6, 7, 8, 12, 13, 9, 14, 15, 16, 17]
        frames = [frames[i] for i in order]
        records = [records[i] for i in order]
    # Approved idle is the starting pose and attack's return pose.
    frames[0] = BASE.copy()
    if name == 'attack': frames[-1] = BASE.copy()
    folder = HERE/name/'frames'
    folder.mkdir(exist_ok=True)
    atlas = Image.new('RGBA', (SIZE[0]*4, SIZE[1]*((len(frames)+3)//4)))
    contact = Image.new('RGBA', atlas.size, '#172337')
    for i, im in enumerate(frames):
        bounds = solid(im)
        assert bounds[0] > 0 and bounds[1] > 0 and bounds[2] < SIZE[0] and bounds[3] < SIZE[1], (name, i, bounds)
        records[i]['outputBounds'] = bounds
        im.save(folder/f'{name}-{i+1:02}.png')
        pos = (i%4*SIZE[0], i//4*SIZE[1])
        atlas.paste(im, pos)
        contact.alpha_composite(im, pos)
    atlas.save(HERE/name/f'{name}-sheet.png')
    contact.resize((1600, atlas.height//2), Image.Resampling.NEAREST).save(HERE/name/'qa-contact.png')
    palette_gif(frames, spec['times'][:-1]+[900], HERE/name/f'{name}-preview.gif')
    all_frames[name] = frames
    manifest['animations'][name] = {'frameCount': len(frames), 'durationMs': spec['times'],
                                    'totalMs': sum(spec['times']), 'loop': False, 'extraction': records}
assert all_frames['attack'][0].tobytes() == all_frames['attack'][-1].tobytes()
(HERE/'manifest.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding='utf-8')

font = ImageFont.truetype('C:/Windows/Fonts/msyh.ttc', 22)
panels = []
for tick in range(58):
    canvas = Image.new('RGBA', (1200, 490), '#172337')
    draw = ImageDraw.Draw(canvas)
    for j, name in enumerate(SPECS):
        elapsed, idx, total = tick*50, len(all_frames[name])-1, 0
        for i, ms in enumerate(SPECS[name]['times']):
            total += ms
            if elapsed < total:
                idx = i
                break
        draw.text((j*600+24, 8), '横砍' if name == 'attack' else '战败：下蹲 → 跪地 → 低头', font=font, fill='#edf2ff')
        im = all_frames[name][idx].resize((600, 444), Image.Resampling.NEAREST)
        canvas.alpha_composite(im, (j*600, 42))
    panels.append(canvas)
palette_gif(panels, [50]*len(panels), HERE/'two-actions-preview.gif')
panels[14].save(HERE/'preview-still.png')
print(json.dumps({k: {'frames': len(v), 'durationMs': sum(SPECS[k]['times'])} for k, v in all_frames.items()}))
