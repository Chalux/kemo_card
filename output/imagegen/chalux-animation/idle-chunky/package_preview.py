"""Slice the generated atlas without repainting or resampling its pixels."""
from pathlib import Path
import json
from PIL import Image

root = Path(__file__).resolve().parent
sheet = Image.open(root / "idle-sheet.png").convert("RGBA")
assert sheet.size == (1536, 1024)
frames = []
checks = []
for index in range(8):
    col, row = index % 4, index // 4
    # The generated first row extends 12px below its nominal row boundary.
    # Split in the actual empty gutter, preserving paw pixels and removing
    # previous-row bleed from the second row; keep a common floor anchor.
    top, bottom = (0, 524) if row == 0 else (524, 1024)
    source = sheet.crop((col * 384, top, (col + 1) * 384, bottom))
    bounds = source.getchannel("A").point(lambda a: 255 if a >= 128 else 0).getbbox()
    frame = Image.new("RGBA", (384, 544), (0, 0, 0, 0))
    frame.alpha_composite(source, (0, 528 - bounds[3]))
    frame.save(root / "frames" / f"idle-{index + 1:02d}.png")
    backdrop = Image.new("RGBA", frame.size, (38, 43, 54, 255))
    backdrop.alpha_composite(frame)
    frames.append(backdrop.convert("RGB"))
    checks.append({"frame": index + 1, "opaque_bounds": frame.getchannel("A").point(lambda a: 255 if a >= 128 else 0).getbbox()})
frames[0].save(root / "idle-preview.gif", save_all=True, append_images=frames[1:], duration=160, loop=0, disposal=2)
manifest = {"animation": "idle", "sourceColumns": 4, "sourceRows": 2, "frameSize": [384, 544], "frameCount": 8, "fps": 6.25, "loop": True, "status": "awaiting-style-approval", "generatedWith": "built-in image_gen", "processing": "slice at row gutter and pad to a common floor; no repainting or resampling", "checks": checks}
(root / "manifest.json").write_text(json.dumps(manifest, ensure_ascii=False, indent=2), encoding="utf-8")
print(json.dumps({"frames": len(frames), "preview": str(root / "idle-preview.gif"), "frameSize": [384, 544]}))
