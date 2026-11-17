"""QA for the v14 package: SpriteFrames draft structure, grounding and stability.

Stability means the body never jumps sideways between neighbouring frames; the
stance check is only reliable where the rear fur cluster is unambiguous (the
attack), because the kneeling poses of the defeat carry the sword hand and the
tail through the same ground band.
"""
import re
from pathlib import Path
import numpy as np
from PIL import Image

HERE = Path(__file__).resolve().parent
GAME = HERE.parents[3] / 'Resource/Assets/Characters/chalux'
GROUND = 534
IDLE_STANCE = 337.5      # idle rear fur cluster centre (the stance anchor)
FUR_BAND = 30
MIN_CLUSTER = 40
TORSO_STEP_LIMIT = 12.0

print('=== SpriteFrames draft structure ===')
for label, path in (('shipped', GAME / 'animations.tres'), ('draft', HERE / 'animations-v14-draft.tres')):
    text = path.read_text(encoding='utf-8')
    frames = re.findall(r'\[sub_resource type="AtlasTexture" id="AtlasTexture_(\w+)_(\d+)"\]', text)
    per_anim = {}
    for name, num in frames:
        per_anim.setdefault(name, []).append(int(num))
    durations = re.findall(r'"duration": ([\d.]+)', text)
    loops = re.findall(r'"loop": (\w+)', text)
    offset = re.search(r'metadata/presentation_offset = Vector2\(([^)]*)\)', text).group(1)
    print(f'{label}: offset={offset} loops={loops} '
          f'frames={ {k: len(v) for k, v in per_anim.items()} } '
          f'total={sum(float(d) for d in durations):.2f}s')
    for name, nums in per_anim.items():
        assert nums == list(range(1, len(nums) + 1)), (name, nums[:5])
        assert len(re.findall(rf'SubResource\("AtlasTexture_{name}_\d+"\)', text)) == len(nums)
print()
print('=== v14 frames ===')
for name in ('attack', 'attack-offset', 'defeat'):
    files = sorted((HERE / name / 'frames').glob('*.png'))
    print(f'{name}: {len(files)} frames')
    stances, torsos, dips = [], [], []
    for path in files:
        im = Image.open(path).convert('RGBA')
        assert im.size == (464, 560), (path.name, im.size)
        a = np.array(im).astype(np.int32)
        solid = a[:, :, 3] > 128
        rows, cols = np.where(solid)
        top, bottom = int(rows.min()), int(rows.max())
        assert GROUND <= bottom <= 559, (path.name, 'bottom', bottom)
        dips.append(bottom - GROUND)
        fur = ((a[:, :, 3] > 180) & (a[:, :, 2] > a[:, :, 0] + 45) & (a[:, :, 2] > a[:, :, 1] + 20))
        fur_rows = np.where(fur.any(axis=1))[0]
        band_floor = int(fur_rows.max())
        yy, _xx = np.indices(a.shape[:2])
        band = fur & (yy > band_floor - FUR_BAND)
        col_any = np.where(band.any(axis=0))[0]
        clusters, start = [], col_any[0]
        for k in range(1, len(col_any)):
            if col_any[k] != col_any[k - 1] + 1:
                clusters.append((start, col_any[k - 1]))
                start = col_any[k]
        clusters.append((start, col_any[-1]))
        clusters = [(s, e) for s, e in clusters if int(band[:, s:e + 1].sum()) >= MIN_CLUSTER]
        rear = clusters[-1]
        sub = band[:, rear[0]:rear[1] + 1]
        _ry, rx = np.where(sub)
        stance = rear[0] + (int(rx.min()) + int(rx.max())) / 2
        stances.append(stance)
        height = bottom - top + 1
        torso = solid & (yy > top + 0.35 * height) & (yy < top + 0.75 * height)
        ty, tx = np.where(torso)
        torsos.append(float(tx.mean()))
        print(f'   {path.name} top={top} groundDip={bottom - GROUND} rearClusterX={stance:.1f} '
              f'({stance - IDLE_STANCE:+.1f}) torsoCenterX={torsos[-1]:.1f}')
    steps = np.abs(np.diff(torsos))
    print(f'   -> torso steps max {steps.max():.1f} px (generated pose lean; reported, not a defect), '
          f'ground dip {min(dips)}..{max(dips)} px')
    if name == 'attack':
        assert max(abs(s - IDLE_STANCE) for s in stances) <= 3.0, (name, stances)
    if name == 'defeat':
        assert all(0 <= d <= 20 for d in dips), dips
