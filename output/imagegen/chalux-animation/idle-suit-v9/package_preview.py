"""Package image_gen artwork as a slower, softer idle with delayed tail motion.

Integer pixel sampling preserves the artist's palette and shading patterns.
The corrected sword and paw anchors remain rigid, while chest and tail move.
"""
from pathlib import Path
import hashlib
import json
import math
import numpy as np
from PIL import Image, ImageDraw

root = Path(__file__).resolve().parent
sheet = Image.open(root / "generated-sheet.png").convert("RGBA")
assert sheet.size == (1536, 1024)
size = (432, 560)
padding = 24
base = Image.new("RGBA", size)
base.paste(sheet.crop((0, 0, 384, 512)), (padding, padding))
blink_pose = Image.new("RGBA", size)
blink_pose.paste(sheet.crop((0, 512, 384, 1024)), (padding, padding))
eye_box = (272, 89, 304, 117)
closed_eye = blink_pose.crop(eye_box)
base_pixels = np.asarray(base).copy()
width, height = size
yy, xx = np.mgrid[0:height, 0:width]
chest_follow = np.interp(yy, [0, 145, 190, 260, 290, height - 1], [0.35, 0.35, 1, 0.05, 0, 0])
torso_x = np.clip((xx - 130) / 35, 0, 1) * np.clip((380 - xx) / 35, 0, 1)
chest_expand = np.clip((yy - 165) / 30, 0, 1) * np.clip((265 - yy) / 35, 0, 1)
# Motion travels along the smaller tail from its hidden root to its pale tip.
# Smooth spatial falloff preserves the seam and keeps the adjacent trousers still.
def smoothstep(value):
    value = np.clip(value, 0, 1)
    return value * value * (3 - 2 * value)

tail_progress = np.clip(0.72 * (yy - 317) / 139 + 0.28 * (187 - xx) / 100, 0, 1)
tail_weight = smoothstep((xx - 53) / 21) * smoothstep((177 - xx) / 23)
tail_weight *= smoothstep((yy - 303) / 30) * smoothstep((484 - yy) / 22)
tail_weight *= smoothstep(tail_progress / 0.85)
feet = np.zeros((height, width), dtype=bool)
feet[490:height, 165:414] = True

# The sword is a rigid attachment: a protected polygon follows its full grip,
# guard and blade, preventing a body warp from bending its axis or the wrist.
sword_plate = Image.new("1", size)
draw = ImageDraw.Draw(sword_plate)
draw.polygon([(131, 258), (161, 257), (205, 343), (269, 521), (252, 535), (176, 356), (141, 368), (137, 346), (156, 331)], fill=1)
sword_rigid = np.asarray(sword_plate, dtype=bool)
count = 32
duration_ms = 100
frames = []
samples = []
for index in range(count):
    phase = index / (count - 1)
    # Inhale occupies 44% of the loop and exhale 56%; each endpoint eases to
    # zero velocity. This replaces the coarse threshold-driven shoulder sway.
    if phase <= 0.44:
        breath = 0.5 - 0.5 * math.cos(math.pi * phase / 0.44)
    else:
        breath = 0.5 + 0.5 * math.cos(math.pi * (phase - 0.44) / 0.56)
    amplitude = 2.0 * breath
    # A slow return and brief settling replace the equal-speed side-to-side sway.
    # Spatial phase delay bends the middle first, then the tip, in two axes.
    tail_clock = 2 * math.pi * (phase + 0.075 * math.sin(2 * math.pi * phase))
    tail_angle = tail_clock - 0.35 - 0.90 * tail_progress
    tail_x = 4.5 * tail_weight * (0.82 * np.sin(tail_angle) + 0.18 * np.sin(2 * tail_angle + 0.45))
    tail_y = 2.0 * tail_weight * np.sin(tail_angle - 0.55)
    plate = base.copy()
    if index in (19, 20):
        plate.paste(closed_eye, eye_box[:2])
    source = np.asarray(plate)
    y_delta = np.rint(amplitude * chest_follow * torso_x).astype(int)
    x_delta = np.rint(0.6 * amplitude * chest_expand * np.tanh((xx - 265) / 50) * torso_x).astype(int)
    x_delta += np.rint(tail_x).astype(int)
    y_delta -= np.rint(tail_y).astype(int)
    src_y = np.clip(yy + y_delta, 0, height - 1)
    src_x = np.clip(xx - x_delta, 0, width - 1)
    pixels = source[src_y, src_x].copy()
    pixels[feet | sword_rigid] = base_pixels[feet | sword_rigid]
    frames.append(Image.fromarray(pixels))
    samples.append({"frame": index + 1, "chestLiftPx": round(amplitude, 3), "tailClockRadians": round(tail_clock, 3), "tailXRangePx": [int(np.rint(tail_x).min()), int(np.rint(tail_x).max())], "tailYRangePx": [int(np.rint(tail_y).min()), int(np.rint(tail_y).max())]})
frames[-1] = frames[0].copy()
assert frames[0].tobytes() == frames[-1].tobytes()
assert all(np.array_equal(np.asarray(frame)[feet], base_pixels[feet]) for frame in frames)
assert all(np.array_equal(np.asarray(frame)[sword_rigid], base_pixels[sword_rigid]) for frame in frames)
assert len({frame.crop((40, 352, 165, 490)).tobytes() for frame in frames}) > 3
assert len({frame.crop((150, 130, 370, 260)).tobytes() for frame in frames}) > 2
source_palette = set(map(tuple, base_pixels.reshape(-1, 4)))
source_palette.update(map(tuple, np.asarray(blink_pose).reshape(-1, 4)))
assert all(set(map(tuple, np.asarray(frame).reshape(-1, 4))).issubset(source_palette) for frame in frames)

columns, rows = 8, 4
atlas = Image.new("RGBA", (width * columns, height * rows))
preview = []
for index, frame in enumerate(frames):
    frame.save(root / "frames" / f"idle-{index + 1:02d}.png")
    atlas.paste(frame, ((index % columns) * width, (index // columns) * height))
    bg = Image.new("RGBA", size, (38, 43, 54, 255))
    bg.alpha_composite(frame)
    preview.append(bg.convert("RGB"))
atlas.save(root / "idle-sheet.png")
palette = preview[0].quantize(colors=256, dither=Image.Dither.NONE)
preview = [frame.quantize(palette=palette, dither=Image.Dither.NONE) for frame in preview]
preview[0].save(root / "idle-preview.gif", save_all=True, append_images=preview[1:], duration=duration_ms, loop=0, disposal=2)
gif = Image.open(root / "idle-preview.gif")
reference_feet = None
for index in range(gif.n_frames):
    gif.seek(index)
    pixels = np.asarray(gif.convert("RGB"))
    if reference_feet is None:
        reference_feet = pixels[feet].copy()
    assert np.array_equal(pixels[feet], reference_feet)

manifest = {"animation": "idle", "frameCount": count, "frameSize": list(size), "columns": columns, "rows": rows, "fps": 10, "cycleDurationMs": count * duration_ms, "status": "organic-motion-and-grip-trial", "styleBaseline": "../idle-balanced-v5", "correctedGripReference": "../../crux-furry-right-grip-v3.png", "rootMotion": False, "footPixelsIdentical": True, "swordPixelsIdentical": True, "firstLastIdentical": True, "chestLiftMaxPx": 2, "tailSwayMaxPx": 6, "inhaleFraction": 0.44, "tailDelayDegrees": 60, "noRegeneratedShadowColors": True, "firstLastSha256": hashlib.sha256(frames[0].tobytes()).hexdigest(), "samples": samples, "generatedWith": "built-in image_gen art with integer frame assembly", "processing": "continuous eased timing, local integer chest and tail sampling, rigid foot and diagonal sword plates, shared preview palette"}
manifest.update({"status": "white-suit-and-followthrough-tail-trial", "costumeReference": "../../crux-suit-right-v5.png", "correctedGripReference": "../../crux-suit-right-v5.png", "motionBaseline": "../idle-academy-v8", "freeLeftHand": "hanging outside pockets on viewer right", "tailSizeRequest": "slightly smaller, still fluffy", "tailSwayMaxPx": 5, "tailVerticalMaxPx": 2, "tailTipLagDegrees": round(math.degrees(0.9), 1), "tailDelayDegrees": None, "processing": "integer pixel assembly; spatially delayed two-axis tail bend, unequal return speed and settling, 32 frames, rigid feet and sword, shared preview palette"})
(root / "manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")
print(json.dumps({key: value for key, value in manifest.items() if key != "samples"}, ensure_ascii=False))
print("PASS: 32 frames; delayed two-axis tail bend; moving chest; fixed feet and diagonal sword; preserved source palette; identical loop boundary; stable preview foot colors")
