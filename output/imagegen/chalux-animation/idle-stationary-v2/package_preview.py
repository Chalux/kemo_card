"""Package generated frames using integer anchor registration and loop ordering.

No repainting, palette replacement, interpolation or rescaling is performed.
"""
from pathlib import Path
import hashlib
import json
import numpy as np
from PIL import Image

root = Path(__file__).resolve().parent
sheet = Image.open(root / "generated-sheet.png").convert("RGBA")
assert sheet.size == (1536, 1024)
size = (432, 560)
padding = 24
sources = []
for i in range(8):
    x, y = (i % 4) * 384, (i // 4) * 512
    source = sheet.crop((x, y, x + 384, y + 512))
    padded = Image.new("RGBA", size)
    padded.paste(source, (padding, padding))
    sources.append(padded)

def registration_features(frame):
    rgba = np.asarray(frame, dtype=np.float32)
    # Alpha-weighted colors prevent transparent backdrop RGB from influencing
    # registration. Only the planted paws serve as the stable root landmark.
    return np.concatenate((rgba[:, :, :3] * rgba[:, :, 3:] / 255, rgba[:, :, 3:]), axis=2)

reference = registration_features(sources[0])
region = np.s_[480:536, 174:402, :]
registered = [sources[0]]
offsets = [[0, 0]]
errors = [0.0]
for source in sources[1:7]:
    features = registration_features(source)
    best = None
    for dy in range(-12, 13):
        for dx in range(-12, 13):
            candidate = np.roll(features, (dy, dx), axis=(0, 1))
            error = float(np.mean((candidate[region] - reference[region]) ** 2))
            score = (error, abs(dx) + abs(dy), dx, dy)
            if best is None or score < best:
                best = score
    error, _, dx, dy = best
    aligned = Image.new("RGBA", size)
    aligned.paste(source, (dx, dy))
    registered.append(aligned)
    offsets.append([dx, dy])
    errors.append(error)

# Use the first frame itself as the closing frame. This guarantees the boundary
# is identical, independent of image generation's approximate final pose.
registered.append(registered[0].copy())
offsets.append([0, 0])
errors.append(0.0)
assert registered[0].tobytes() == registered[-1].tobytes()

atlas = Image.new("RGBA", (size[0] * 4, size[1] * 2))
preview = []
for i, frame in enumerate(registered):
    frame.save(root / "frames" / f"idle-{i + 1:02d}.png")
    atlas.paste(frame, ((i % 4) * size[0], (i // 4) * size[1]))
    background = Image.new("RGBA", size, (38, 43, 54, 255))
    background.alpha_composite(frame)
    preview.append(background.convert("RGB"))
atlas.save(root / "idle-sheet.png")
preview[0].save(root / "idle-preview.gif", save_all=True, append_images=preview[1:], duration=160, loop=0, disposal=2)

manifest = {"animation": "idle", "frameCount": 8, "frameSize": list(size), "columns": 4, "rows": 2, "fps": 6.25, "loop": True, "status": "awaiting-user-confirmation", "rootMotion": False, "firstLastIdentical": True, "firstLastSha256": hashlib.sha256(registered[0].tobytes()).hexdigest(), "integerRegistrationOffsets": offsets, "pawRegistrationErrors": errors, "generatedWith": "built-in image_gen", "processing": "integer crop/padding/anchor registration and first-frame reuse at the loop boundary; no repainting or resampling"}
(root / "manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")
print(json.dumps(manifest, ensure_ascii=False))
