"""Animate the approved image_gen artwork with a small integer pixel rig.

The generated motion sheet is a pose reference. Approved v5 artwork supplies
all final colors and shading so the breathing test does not restyle the hero.
"""
from pathlib import Path
import hashlib
import json
import numpy as np
from PIL import Image

root = Path(__file__).resolve().parent
art = root.parent / "idle-balanced-v5" / "frames"
base = Image.open(art / "idle-01.png").convert("RGBA")
blink = Image.open(art / "idle-05.png").convert("RGBA")
width, height = base.size
eye_box = (272, 89, 302, 115)
tail_box = (60, 424, 164, 504)
phases = [0, 1, 3, 4, 3, 1, 0, 0]
tail_phases = [0, 0, 0, 0, 0, 0, 0, 0]
duration_ms = 260
rows = np.arange(height)
# Head follows half the chest lift; chest/shoulders peak at four source pixels.
# Displacement fades into the waist; lower body and feet are fixed exactly.
weights = np.interp(rows, [0, 145, 195, 280, 340, height - 1], [0.5, 0.5, 1, 0.55, 0, 0])
frames = []
for index, amplitude in enumerate(phases):
    plate = base.copy()
    if index == 4:
        plate.paste(blink.crop(eye_box), eye_box[:2])
    tail_delta = tail_phases[index]
    if tail_delta:
        tail = np.asarray(base.crop(tail_box)).copy()
        moved = np.zeros_like(tail)
        for y in range(tail.shape[0]):
            delta = round(tail_delta * (y // 16) / max(1, (tail.shape[0] - 1) // 16))
            if delta >= 0:
                moved[y, delta:] = tail[y, :tail.shape[1] - delta]
            else:
                moved[y, :tail.shape[1] + delta] = tail[y, -delta:]
        plate.paste(Image.fromarray(moved), tail_box[:2])
    source = np.asarray(plate)
    lifted_rows = np.clip(rows + np.rint(amplitude * weights).astype(int), 0, height - 1)
    pixels = source[lifted_rows].copy()
    # At peak inhalation, a one-pixel chest lean adds a gentle shoulder sway.
    if amplitude >= 3:
        for y in range(145, 270):
            pixels[y, 1:] = pixels[y, :-1].copy()
            pixels[y, 0] = (0, 0, 0, 0)
    frames.append(Image.fromarray(pixels))
frames[-1] = frames[0].copy()
base_pixels = np.asarray(base)
foot_mask = np.zeros((height, width), dtype=bool)
foot_mask[480:536, 174:402] = True
assert all(np.array_equal(np.asarray(frame)[foot_mask], base_pixels[foot_mask]) for frame in frames)
assert frames[0].tobytes() == frames[-1].tobytes()
assert any(frame.crop((125, 30, 390, 340)).tobytes() != frames[0].crop((125, 30, 390, 340)).tobytes() for frame in frames[1:-1])

atlas = Image.new("RGBA", (width * 4, height * 2))
preview = []
for index, frame in enumerate(frames):
    frame.save(root / "frames" / f"idle-{index + 1:02d}.png")
    atlas.paste(frame, ((index % 4) * width, (index // 4) * height))
    background = Image.new("RGBA", base.size, (38, 43, 54, 255))
    background.alpha_composite(frame)
    preview.append(background.convert("RGB"))
atlas.save(root / "idle-sheet.png")
palette = preview[0].quantize(colors=256, dither=Image.Dither.NONE)
preview = [frame.quantize(palette=palette, dither=Image.Dither.NONE) for frame in preview]
preview[0].save(root / "idle-preview.gif", save_all=True, append_images=preview[1:], duration=duration_ms, loop=0, disposal=2)
assert len(frames) == 8
assert all(np.array_equal(np.asarray(frame)[344:], base_pixels[344:]) for frame in frames)
source_palette = set(map(tuple, base_pixels.reshape(-1, 4)))
source_palette.update(map(tuple, np.asarray(blink).reshape(-1, 4)))
source_palette.add((0, 0, 0, 0))
assert all(set(map(tuple, np.asarray(frame).reshape(-1, 4))).issubset(source_palette) for frame in frames)
gif = Image.open(root / "idle-preview.gif")
gif_baseline = None
for i in range(gif.n_frames):
    gif.seek(i)
    lower = np.asarray(gif.convert("RGB"))[344:].copy()
    if gif_baseline is None:
        gif_baseline = lower
    else:
        assert np.array_equal(lower, gif_baseline)
manifest = {"animation": "idle", "frameCount": len(frames), "frameSize": list(base.size), "columns": 4, "rows": 2, "fps": 1000 / duration_ms, "cycleDurationMs": len(frames) * duration_ms, "status": "breathing-trial-awaiting-user-confirmation", "styleSource": "../idle-balanced-v5/frames/idle-01.png", "motionReference": "generated-motion-reference.png", "rootMotion": False, "footPixelsIdentical": True, "firstLastIdentical": True, "chestLiftMaxPx": 4, "headLiftMaxPx": 2, "shoulderSwayMaxPx": 1, "breathingPhases": phases, "shading": "source colors travel with the integer rig; no regenerated shadows or lighting", "firstLastSha256": hashlib.sha256(frames[0].tobytes()).hexdigest(), "generatedWith": "built-in image_gen artwork and motion reference; integer animation assembly", "processing": "approved v5 pixels mapped by integer breathing rig; no color blending or shading repaint"}
(root / "manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")
print(json.dumps(manifest, ensure_ascii=False))
print("PASS: approved palette preserved, lower body fixed, upper body breathes, exact loop boundary, shared GIF palette stable")
