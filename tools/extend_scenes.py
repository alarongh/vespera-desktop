"""Normalize two new transparent animation sheets and extend the overlay pack."""

import argparse
import json
import sys
from pathlib import Path

from PIL import Image

from import_originals import load_row, load_strip, save_frames


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--extractor", type=Path, required=True)
    parser.add_argument("--dance", type=Path, required=True)
    parser.add_argument("--smoke", type=Path, required=True)
    parser.add_argument("--artwork", type=Path, required=True)
    args = parser.parse_args()
    sys.path.insert(0, str(args.extractor))
    import extract_strip_frames as extract

    with Image.open(args.dance) as original:
        sheet = extract.load_native_alpha_image(original, args.dance)
    width, height = sheet.size
    middle = height // 2
    dance_poses = [
        pose
        for y0, y1 in ((0, middle), (middle, height))
        for pose in load_strip(sheet.crop((0, y0, width, y1)), 6, extract)
    ]
    smoke_poses = load_row(args.smoke, 6, extract)
    save_frames(dance_poses, args.artwork / "idol-dance", allow_enlarging=True)
    save_frames(smoke_poses, args.artwork / "smoke", start=6)

    pack_path = args.artwork / "pack.json"
    pack = json.loads(pack_path.read_text(encoding="utf-8"))
    durations = [440, 290, 280, 350, 260, 270, 330, 340, 280, 300, 420, 570]
    pack["scenes"]["idol-dance"] = {
        "label": "Танец айдола",
        "loopFrom": 0,
        "frames": [{"file": f"idol-dance/{i:02}.png", "ms": ms} for i, ms in enumerate(durations)],
    }
    smoke = pack["scenes"]["smoke"]
    smoke["frames"] = smoke["frames"][:6] + [
        {"file": f"smoke/{i:02}.png", "ms": ms}
        for i, ms in enumerate([670, 540, 500, 950, 800, 600], start=6)
    ]
    smoke["loopFrom"] = 6
    pack_path.write_text(json.dumps(pack, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print("12 dance poses, 6 new smoke poses (12 total).")


if __name__ == "__main__":
    main()
