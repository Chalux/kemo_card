"""v14: rebuild attack/defeat as game-spec sequence frames on the approved idle base.

Mechanical work only: slice the v13 idle-based pose sheets, rescale with one
uniform nearest-neighbour factor per action, register every pose to the approved
idle's stance anchor and ground row inside the 464x560 game canvas, retime, and
package frames / atlases / GIFs. No pixel is painted or patched.

The idle is untouched: the neutral frames reuse the shipped idle-01 pixels, so
idle -> action -> idle boundaries stay pixel-identical.

Attack is emitted twice because the generated slash is wider than the game
canvas:
  attack/         body stays exactly where idle stands, animation scaled to fit
  attack-offset/  full idle scale, body placed as close to idle as the canvas allows
"""
from pathlib import Path
import json
import numpy as np
from PIL import Image, ImageDraw, ImageFont

HERE = Path(__file__).resolve().parent
ANIM = HERE.parent
PROJECT = HERE.parents[3]
IDLE_FRAMES = ANIM / 'idle-suit-v9' / 'frames'
V13 = ANIM / 'suit-actions-v13'
GAME = PROJECT / 'Resource/Assets/Characters/chalux'

SIZE = (464, 560)          # shared game canvas (Resource/Assets/Characters/chalux/README.md)
GROUND = 534               # lowest solid row of the approved idle (identical in all 32 idle frames)
MARGIN = 8                 # transparent border kept around every pose when scaling down
TIGHT_MARGIN = 2           # accepted border when the idle-matched scale is otherwise impossible
CONTACT_BAND = 30          # rows above the lowest solid pixel treated as ground contact
MIN_CLUSTER = 40           # ignore 1-5 px fur fragments when locating paw clusters

BASE = Image.open(IDLE_FRAMES / 'idle-01.png').convert('RGBA')
IDLE_CANVAS = Image.new('RGBA', SIZE)
IDLE_CANVAS.paste(BASE, (0, 0))

ATTACK = {
    'file': 'registered-sheet.png',
    'rows': [0, 367, 724, 1086],
    'crops': [(0, 327), (327, 670), (670, 1000), (1000, 1448),
              (0, 329), (329, 666), (666, 1125), (1100, 1448),
              (0, 380), (375, 670), (670, 1060), (1090, 1448)],
    # Stance windows inherited from the v13 packager (here: absolute sheet x).
    'feet': [(95, 185), (400, 490), (750, 835), (1180, 1270),
             (90, 180), (405, 490), (745, 835), (1180, 1270),
             (80, 170), (425, 515), (760, 850), (1180, 1270)],
    'neutralIndex': 0,
    'poses': [1, 2, 4, 5, 7, 9, 10],       # v13 poses 2,3,5,6,8,10,11 (fits the 464 canvas)
    'times': [70, 110, 90, 70, 45, 60, 95, 115, 125],
}
DEFEAT = {
    'file': 'generated-sheet.png',
    'rows': [0, 360, 680, 980, 1254],
    'crops': None,
    # Stance window inherited from v13 (here: crop-local x).
    'feet': [(182, 266), (183, 270), (196, 284), (208, 295), (200, 283), (205, 294),
             (211, 310), (214, 313), (197, 290), (207, 300), (215, 313), (220, 313)],
    'neutralIndex': 0,
    'poses': list(range(1, 12)),           # v13 poses 2..12
    'times': [100, 110, 110, 110, 115, 115, 115, 120, 120, 125, 125, 135],  # 1400 ms, same as shipped
}
SPECS = {'attack': ATTACK, 'defeat': DEFEAT}


def slice_pose(spec, sheet, index):
    """Crop one generated pose exactly as the v13 packager did, plus its stance window."""
    row, col = divmod(index, 4)
    top, bottom = spec['rows'][row], spec['rows'][row + 1]
    if spec['crops'] is not None:
        left, right = spec['crops'][index]
    else:
        left, right = round(col * sheet.width / 4), round((col + 1) * sheet.width / 4)
    raw = np.array(sheet.crop((left, top, right, bottom))).astype(np.int32)
    yy, xx = np.indices(raw.shape[:2])
    gx, gy = xx + left, yy + top
    name = 'attack' if spec is ATTACK else 'defeat'
    if name == 'attack' and index == 6:            # neighbour pose bleeding into cell 7
        raw[(gx > 1090) & (gy > 580)] = 0
    if name == 'attack' and index == 7:            # neighbour pose bleeding into cell 8
        raw[(gx < 1125) & (gy < 560)] = 0
    lo, hi = spec['feet'][index]
    # The defeat uses v13's per-pose stance window to ignore the fur fragments
    # that join the ground band once the tail and the sword hand drop low.
    # The attack needs no hint: its rear fur cluster is the planted support and
    # the measured deviation stays below 0.5 px across all nine frames.
    window = (lo, hi) if name == 'defeat' else None
    return Image.fromarray(raw.astype('uint8')), window


def metrics(im, window=None):
    """Stance anchor, ground contact and bounds of a pose image.

    The ground band is measured from the lowest fur pixel, so a planted sword tip
    (which is neutral, not fur) cannot drag the band down. Inside the band the
    stance is the rear-most fur cluster that overlaps the pose's inherited stance
    window - the planted support: the back paw when standing, the knee/shin when
    kneeling. The contact row is measured inside that cluster, so the support
    lands on the ground line even where the sword tip dips below it.
    """
    a = np.array(im).astype(np.int32)
    solid = a[:, :, 3] > 128
    rows, cols = np.where(solid)
    if not len(rows):
        raise AssertionError('empty pose')
    yy, xx = np.indices(a.shape[:2])
    fur = (a[:, :, 3] > 180) & (a[:, :, 2] > a[:, :, 0] + 45) & (a[:, :, 2] > a[:, :, 1] + 20)
    fur_rows = np.where(fur.any(axis=1))[0]
    band_floor = int(fur_rows.max()) if len(fur_rows) else int(rows.max())
    blue = fur & (yy > band_floor - CONTACT_BAND)
    clusters = []
    cols_any = np.where(blue.any(axis=0))[0]
    if len(cols_any):
        start = cols_any[0]
        for k in range(1, len(cols_any)):
            if cols_any[k] != cols_any[k - 1] + 1:
                clusters.append((start, cols_any[k - 1]))
                start = cols_any[k]
        clusters.append((start, cols_any[-1]))
    clusters = [(s, e) for s, e in clusters if int(blue[:, s:e + 1].sum()) >= MIN_CLUSTER]
    if window is not None:
        hinted = [c for c in clusters if c[1] >= window[0] and c[0] <= window[1]]
        if hinted:
            clusters = hinted
    if clusters:
        rear = clusters[-1]
        sub = blue[:, rear[0]:rear[1] + 1]
        _yy, _xx = np.where(sub)
        anchor = rear[0] + (int(_xx.min()) + int(_xx.max())) / 2
        contacts = []
        for s, e in clusters:
            in_cols = np.where(solid[:, s:e + 1].any(axis=1))[0]
            if len(in_cols):
                contacts.append(int(in_cols.max()))
        contact_bottom = max(contacts)
    else:
        anchor = (int(cols.min()) + int(cols.max())) / 2
        contact_bottom = int(rows.max())
    return {
        'anchor': anchor,
        'contactBottom': contact_bottom,
        'solidBottom': int(rows.max()),
        'clusters': clusters,
        'bounds': [int(cols.min()), int(rows.min()), int(cols.max()) + 1, int(rows.max()) + 1],
    }


def base_scale(spec, sheet, idle_height):
    pose, window = slice_pose(spec, sheet, spec['neutralIndex'])
    m = metrics(pose, window)
    return idle_height / (m['bounds'][3] - m['bounds'][1])


def anchor_window(spec, sheet, scale, margin):
    """Feasible stance-anchor range for one action at a given scale."""
    lo, hi = float('-inf'), float('inf')
    for index in spec['poses']:
        pose, window = slice_pose(spec, sheet, index)
        m = metrics(pose, window)
        left_ext = (m['bounds'][0] - m['anchor']) * scale
        right_ext = (m['bounds'][2] - m['anchor']) * scale
        lo = max(lo, margin - left_ext)
        hi = min(hi, SIZE[0] - margin - right_ext)
    return lo, hi


def max_fitting_scale(spec, sheet, cap, margin):
    if anchor_window(spec, sheet, cap, margin)[0] <= anchor_window(spec, sheet, cap, margin)[1]:
        return cap
    low, high = 0.4, cap
    for _ in range(30):
        mid = (low + high) / 2
        lo, hi = anchor_window(spec, sheet, mid, margin)
        if lo <= hi:
            low = mid
        else:
            high = mid
    return low


def scale_at_fixed_anchor(spec, sheet, anchor, margin, cap):
    """Largest scale keeping every pose inside the canvas at one fixed anchor."""
    scale = cap
    for index in spec['poses']:
        pose, window = slice_pose(spec, sheet, index)
        m = metrics(pose, window)
        left_rel = m['bounds'][0] - m['anchor']
        right_rel = m['bounds'][2] - m['anchor']
        if left_rel < 0:
            scale = min(scale, (anchor - margin) / -left_rel)
        if right_rel > 0:
            scale = min(scale, (SIZE[0] - margin - anchor) / right_rel)
    return scale


def render(spec, sheet, index, scale, anchor):
    """Scale + register one pose. Geometry comes from one measurement of the raw
    pose, so nearest-neighbour rescaling cannot move the anchor or the contact."""
    raw, window = slice_pose(spec, sheet, index)
    src = metrics(raw, window)
    scaled = raw.resize((round(raw.width * scale), round(raw.height * scale)),
                        Image.Resampling.NEAREST)
    sa = np.array(scaled)[:, :, 3] > 128
    srows, scols = np.where(sa)
    rx = scaled.width / raw.width
    # Contact is re-measured on the scaled pose inside the ground clusters, so the
    # stance lands exactly on GROUND whatever the nearest-neighbour rounding does.
    contacts = []
    for s, e in (src['clusters'] or [(int(scols.min()), int(scols.max()))]):
        cs, ce = int(np.floor(s * rx)), int(np.ceil(e * rx)) + 1
        rows_in = np.where(sa[:, cs:ce].any(axis=1))[0]
        if len(rows_in):
            contacts.append(int(rows_in.max()))
    contact_row = max(contacts) if contacts else int(srows.max())
    canvas = Image.new('RGBA', SIZE)
    dx = round(anchor - src['anchor'] * rx)
    dy = GROUND - contact_row
    left, right = dx + int(scols.min()), dx + int(scols.max()) + 1
    top, bottom = dy + int(srows.min()), dy + int(srows.max()) + 1
    assert left >= 0 and right <= SIZE[0] and top >= 0 and bottom <= SIZE[1], \
        f'pose would be clipped: content=({left},{top},{right},{bottom})'
    canvas.paste(scaled, (dx, dy))
    a = np.array(canvas)
    solid = a[:, :, 3] > 128
    rows, cols = np.where(solid)
    bottom, top = int(rows.max()), int(rows.min())
    left, right = int(cols.min()), int(cols.max())
    assert bottom < SIZE[1] and top >= 0 and left >= 0 and right < SIZE[0], 'pose leaves the canvas'
    return canvas, {
        'sourcePose': index + 1,
        'sourceBounds': src['bounds'],
        'sourceAnchor': round(src['anchor'], 1),
        'sourceContactBottom': src['contactBottom'],
        'registration': [dx, dy],
        'outputBounds': [left, top, right + 1, bottom + 1],
        'groundOvershootPx': bottom - GROUND,
        'bodyCenterOffsetFromIdle': round((left + right) / 2 - IDLE_BODY_CENTER, 1),
    }


def gif(frames, times, path, labels=None, font=None):
    rgb = []
    for i, im in enumerate(frames):
        bg = Image.new('RGBA', im.size, '#172337')
        bg.alpha_composite(im)
        if labels:
            ImageDraw.Draw(bg).text((16, 10), labels[i], font=font, fill='#edf2ff')
        rgb.append(bg.convert('RGB'))
    strip = Image.new('RGB', (rgb[0].width * len(rgb), rgb[0].height))
    for i, im in enumerate(rgb):
        strip.paste(im, (i * im.width, 0))
    palette = strip.quantize(colors=256, dither=Image.Dither.NONE)
    quantized = [im.quantize(palette=palette, dither=Image.Dither.NONE) for im in rgb]
    quantized[0].save(path, save_all=True, append_images=quantized[1:], duration=times,
                      loop=0, optimize=False, disposal=2)


_idle_metrics = metrics(IDLE_CANVAS)
IDLE_BODY_CENTER = (_idle_metrics['bounds'][0] + _idle_metrics['bounds'][2]) / 2
IDLE_ANCHOR = _idle_metrics['anchor']
IDLE_HEIGHT = _idle_metrics['bounds'][3] - _idle_metrics['bounds'][1]

manifest = {
    'mode': 'mechanical re-registration of v13 idle-based poses; no new artwork',
    'version': 'v14',
    'status': 'game-spec preview; game assets not replaced',
    'canvas': list(SIZE),
    'groundRow': GROUND,
    'idleStanceAnchorX': IDLE_ANCHOR,
    'idleBodyCenterX': IDLE_BODY_CENTER,
    'idleBodyHeightPx': IDLE_HEIGHT,
    'idleSource': 'idle-suit-v9/frames/idle-01.png (pixels reused unchanged)',
    'poseSource': 'suit-actions-v13 attack/registered-sheet.png and defeat/generated-sheet.png',
    'attackVariants': {},
    'animations': {},
}
all_frames = {}

for name, spec in SPECS.items():
    sheet = Image.open(V13 / name / spec['file']).convert('RGBA')
    natural = base_scale(spec, sheet, IDLE_HEIGHT)
    fit = max_fitting_scale(spec, sheet, natural, MARGIN)
    lo, hi = anchor_window(spec, sheet, fit, MARGIN)
    tight_lo, tight_hi = anchor_window(spec, sheet, natural, TIGHT_MARGIN)
    tight_ok = tight_lo <= tight_hi
    variants = []
    if name == 'attack':
        # A: body stays exactly at the idle stance, animation scaled until the
        #    widest generated pose fits with a comfortable margin.
        scale_a = scale_at_fixed_anchor(spec, sheet, IDLE_ANCHOR, MARGIN, natural)
        # B: idle-matched scale, whole action shifted left only as far as the
        #    widest generated pose requires.
        lo, hi = anchor_window(spec, sheet, natural, TIGHT_MARGIN)
        anchor_b = min(max(IDLE_ANCHOR, lo), hi) if lo <= hi else IDLE_ANCHOR
        scale_b = natural if lo <= hi else scale_at_fixed_anchor(spec, sheet, anchor_b, TIGHT_MARGIN, natural)
        variants = [
            ('attack', scale_a, IDLE_ANCHOR, MARGIN,
             'body stays exactly at the idle stance; animation scaled to fit'),
            ('attack-offset', scale_b, anchor_b, TIGHT_MARGIN,
             'idle-matched scale; whole action offset sideways to fit'),
        ]
    else:
        scale = natural if tight_ok else max_fitting_scale(spec, sheet, natural, MARGIN)
        lo2, hi2 = anchor_window(spec, sheet, scale, TIGHT_MARGIN if scale == natural else MARGIN)
        variants = [('defeat', scale, min(max(IDLE_ANCHOR, lo2), hi2), TIGHT_MARGIN, 'idle scale kept')]

    for dirname, scale, anchor, margin, note in variants:
        frames, records = [IDLE_CANVAS.copy()], []
        for index in spec['poses']:
            frame, record = render(spec, sheet, index, scale, anchor)
            frames.append(frame)
            records.append(record)
        if name == 'attack':
            frames.append(IDLE_CANVAS.copy())
        times = spec['times'][:len(frames)]
        out = HERE / dirname
        frames_dir = out / 'frames'
        frames_dir.mkdir(parents=True, exist_ok=True)
        cols = 4 if len(frames) > 9 else 3
        rows = -(-len(frames) // cols)
        atlas = Image.new('RGBA', (SIZE[0] * cols, SIZE[1] * rows))
        contact = Image.new('RGBA', atlas.size, '#172337')
        for i, im in enumerate(frames):
            assert im.size == SIZE
            im.save(frames_dir / f'{dirname}-{i + 1:02d}.png')
            pos = (i % cols * SIZE[0], i // cols * SIZE[1])
            atlas.paste(im, pos)
            contact.alpha_composite(im, pos)
        atlas.save(out / f'{dirname}-sheet.png')
        contact.save(out / 'qa-contact.png')
        font = ImageFont.truetype('C:/Windows/Fonts/msyh.ttc', 22)
        label = {'attack': '攻击：对齐待机站位，等比缩小',
                 'attack-offset': '攻击：保持等比，整体左移让剑',
                 'defeat': '战败：与待机同比例、同脚底基准'}[dirname]
        gif(frames, times, out / f'{dirname}-preview.gif',
            labels=[label] * len(frames), font=font)
        all_frames[dirname] = frames
        entry = {
            'dir': dirname,
            'note': note,
            'frameCount': len(frames),
            'durationMs': times,
            'totalMs': sum(times),
            'atlas': {'columns': cols, 'rows': rows, 'size': list(atlas.size)},
            'idleMatchedScale': round(natural, 4),
            'scale': round(scale, 4),
            'scaleVsIdleMatched': round(scale / natural, 4),
            'stanceAnchorX': round(anchor, 2),
            'anchorOffsetFromIdle': round(anchor - IDLE_ANCHOR, 1),
            'marginPx': margin,
            'droppedGeneratedPoses': [i + 1 for i in range(12)
                                      if i not in spec['poses'] and i != spec['neutralIndex']],
            'poses': records,
        }
        if name == 'attack':
            manifest['attackVariants'][dirname] = entry
        manifest['animations'][dirname] = entry

# Fit analysis: why the dropped generated poses cannot enter the 464 canvas.
fit_report = {}
sheet = Image.open(V13 / 'attack' / ATTACK['file']).convert('RGBA')
natural_attack = base_scale(ATTACK, sheet, IDLE_HEIGHT)
for index in range(12):
    pose, window = slice_pose(ATTACK, sheet, index)
    m = metrics(pose, window)
    left_ext = (m['bounds'][0] - m['anchor']) * natural_attack
    right_ext = (m['bounds'][2] - m['anchor']) * natural_attack
    fit_report[index + 1] = {
        'spanAtIdleScale': round((m['bounds'][2] - m['bounds'][0]) * natural_attack, 1),
        'needsLeftPx': round(max(0.0, -left_ext), 1),
        'needsRightPx': round(max(0.0, right_ext), 1),
        'fits464AtIdleScale': bool(right_ext <= SIZE[0] - IDLE_ANCHOR - TIGHT_MARGIN
                                   and -left_ext <= IDLE_ANCHOR - TIGHT_MARGIN),
        'used': index in ATTACK['poses'],
    }
manifest['attackFitAnalysis'] = {
    'idleScale': round(natural_attack, 4),
    'availableLeftPx': round(IDLE_ANCHOR, 1),
    'availableRightPx': round(SIZE[0] - IDLE_ANCHOR, 1),
    'poses': fit_report,
}

# Comparison and slot-size previews.
idle_atlas = Image.open(GAME / 'idle.png').convert('RGBA')
all_frames['idle'] = [idle_atlas.crop((i % 8 * SIZE[0], i // 8 * SIZE[1],
                                       i % 8 * SIZE[0] + SIZE[0], i // 8 * SIZE[1] + SIZE[1]))
                      for i in range(32)]


def frame_at(name, tick):
    spec = manifest['animations'][name]
    index, total = spec['frameCount'] - 1, 0
    for i, ms in enumerate(spec['durationMs']):
        total += ms
        if tick < total:
            index = i
            break
    return index


font = ImageFont.truetype('C:/Windows/Fonts/msyh.ttc', 22)
panels = []
for tick in range(0, 1900, 50):
    canvas = Image.new('RGBA', (SIZE[0] * 3, SIZE[1]), '#172337')
    draw = ImageDraw.Draw(canvas)
    for j, name in enumerate(('idle', 'attack', 'attack-offset')):
        index = (tick // 100) % 32 if name == 'idle' else frame_at(name, tick)
        canvas.alpha_composite(all_frames[name][index], (j * SIZE[0], 0))
        draw.text((j * SIZE[0] + 16, 10), {'idle': '待机（游戏现有）', 'attack': '攻击 A：原位缩放',
                                           'attack-offset': '攻击 B：等比左移'}[name],
                  font=font, fill='#edf2ff')
    panels.append(canvas.convert('RGB'))
shared = panels[0].quantize(colors=256, dither=Image.Dither.NONE)
quantized = [im.quantize(palette=shared, dither=Image.Dither.NONE) for im in panels]
quantized[0].save(HERE / 'attack-compare-preview.gif', save_all=True, append_images=quantized[1:],
                  duration=50, loop=0, optimize=False)
panels[12].save(HERE / 'preview-still.png')

panels = []
for tick in range(0, 1900, 50):
    canvas = Image.new('RGBA', (SIZE[0] * 2, SIZE[1]), '#172337')
    draw = ImageDraw.Draw(canvas)
    for j, name in enumerate(('attack', 'defeat')):
        canvas.alpha_composite(all_frames[name][frame_at(name, tick)], (j * SIZE[0], 0))
        draw.text((j * SIZE[0] + 16, 10), '攻击 A' if name == 'attack' else '战败',
                  font=font, fill='#edf2ff')
    panels.append(canvas.convert('RGB'))
shared = panels[0].quantize(colors=256, dither=Image.Dither.NONE)
quantized = [im.quantize(palette=shared, dither=Image.Dither.NONE) for im in panels]
quantized[0].save(HERE / 'two-actions-preview.gif', save_all=True, append_images=quantized[1:],
                  duration=50, loop=0, optimize=False)

SLOT = (104, 126)          # AllyUnitCmp presenter rect inside the 112x160 combat slot
slot_font = ImageFont.truetype('C:/Windows/Fonts/msyh.ttc', 13)
panels = []
for tick in range(0, 1900, 50):
    canvas = Image.new('RGBA', (SLOT[0] * 4, SLOT[1] + 18), '#172337')
    draw = ImageDraw.Draw(canvas)
    for j, name in enumerate(('idle', 'attack', 'attack-offset', 'defeat')):
        index = (tick // 100) % 32 if name == 'idle' else frame_at(name, tick)
        tile = all_frames[name][index].resize(SLOT, Image.Resampling.NEAREST)
        canvas.alpha_composite(tile, (j * SLOT[0], 18))
        draw.text((j * SLOT[0] + 4, 2),
                  {'idle': '待机', 'attack': '攻击A', 'attack-offset': '攻击B', 'defeat': '战败'}[name],
                  font=slot_font, fill='#edf2ff')
    panels.append(canvas.convert('RGB'))
panels = [im.resize((im.width * 2, im.height * 2), Image.Resampling.NEAREST) for im in panels]
shared = panels[0].quantize(colors=256, dither=Image.Dither.NONE)
quantized = [im.quantize(palette=shared, dither=Image.Dither.NONE) for im in panels]
quantized[0].save(HERE / 'slot-size-preview.gif', save_all=True, append_images=quantized[1:],
                  duration=50, loop=0, optimize=False)

(HERE / 'manifest.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding='utf-8')

# Integration-ready SpriteFrames draft: idle/enter/buff keep the shipped atlas
# layout and durations, attack/defeat use the new frame counts and timings.
import re

SHIPPED = (GAME / 'animations.tres').read_text(encoding='utf-8')
shipped_durations = {}
_cursor = SHIPPED.index('animations = [')
for block in re.finditer(r'"name": &"(\w+)"', SHIPPED):
    shipped_durations[block.group(1)] = [round(float(v) * 100)
                                         for v in re.findall(r'"duration": ([\d.]+)',
                                                             SHIPPED[_cursor:block.start()])]
    _cursor = block.end()
LAYOUT = {
    'enter': {'columns': 4, 'frames': 8},
    'idle': {'columns': 8, 'frames': 32},
    'attack': {'columns': 3, 'frames': 9},
    'buff': {'columns': 4, 'frames': 8},
    'defeat': {'columns': 4, 'frames': 12},
}
ORDER = ['enter', 'idle', 'attack', 'buff', 'defeat']
for name in ORDER:
    if name in ('attack', 'defeat'):
        LAYOUT[name]['durations'] = manifest['animations'][
            'attack' if name == 'attack' else 'defeat']['durationMs']
        LAYOUT[name]['loop'] = False
    else:
        LAYOUT[name]['durations'] = shipped_durations[name]
        LAYOUT[name]['loop'] = name == 'idle'
    assert len(LAYOUT[name]['durations']) == LAYOUT[name]['frames'], (name, LAYOUT[name])

total_frames = sum(v['frames'] for v in LAYOUT.values())
lines = [f'[gd_resource type="SpriteFrames" load_steps={total_frames + len(ORDER) + 1} format=3]', '']
for n, name in enumerate(ORDER, 1):
    lines.append(f'[ext_resource type="Texture2D" '
                 f'path="res://Resource/Assets/Characters/chalux/{name}.png" id="{n}_{name}"]')
for n, name in enumerate(ORDER, 1):
    spec = LAYOUT[name]
    for i in range(spec['frames']):
        lines += ['', f'[sub_resource type="AtlasTexture" id="AtlasTexture_{name}_{i + 1:02d}"]',
                  f'atlas = ExtResource("{n}_{name}")',
                  f'region = Rect2({i % spec["columns"] * SIZE[0]}, {i // spec["columns"] * SIZE[1]}, '
                  f'{SIZE[0]}, {SIZE[1]})', 'filter_clip = true']
lines += ['', '[resource]', 'metadata/presentation_offset = Vector2(-32, 0)', 'animations = [']
for n, name in enumerate(ORDER):
    spec = LAYOUT[name]
    lines += ['{', '"frames": [']
    for i, ms in enumerate(spec['durations']):
        lines += ['{', f'"duration": {ms / 100:.2f},',
                  f'"texture": SubResource("AtlasTexture_{name}_{i + 1:02d}")',
                  '},' if i < spec['frames'] - 1 else '}']
    lines += [']', f'"loop": {str(spec["loop"]).lower()},', f'"name": &"{name}"',
              '"speed": 10.0', '},' if n < len(ORDER) - 1 else '}']
lines += [']', '']
(HERE / 'animations-v14-draft.tres').write_text('\n'.join(lines), encoding='utf-8')
manifest['integration'] = {
    'note': 'draft only; game assets untouched',
    'spriteFramesDraft': 'animations-v14-draft.tres',
    'steps': [
        'copy the chosen attack sheet to Resource/Assets/Characters/chalux/attack.png',
        'copy defeat/defeat-sheet.png to Resource/Assets/Characters/chalux/defeat.png',
        'replace animations.tres with animations-v14-draft.tres (or regenerate with this script)',
        'update Resource/Assets/Characters/chalux/README.md frame counts and durations',
    ],
}
(HERE / 'manifest.json').write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding='utf-8')

print(f'idle: anchor={IDLE_ANCHOR} bodyCenter={IDLE_BODY_CENTER} height={IDLE_HEIGHT}')
for dirname, entry in manifest['animations'].items():
    print(f"{dirname}: frames={entry['frameCount']} ms={entry['totalMs']} "
          f"scale={entry['scale']} ({entry['scaleVsIdleMatched'] * 100:.1f}% of idle-matched) "
          f"anchorX={entry['stanceAnchorX']} (idle {IDLE_ANCHOR}) margin={entry['marginPx']}")
    print('   top/bottom/centerOffset:',
          [(r['outputBounds'][1], r['outputBounds'][3], r['bodyCenterOffsetFromIdle']) for r in entry['poses']])
print('attack poses that do not fit 464 at idle scale:',
      [k for k, v in fit_report.items() if not v['fits464AtIdleScale']])
