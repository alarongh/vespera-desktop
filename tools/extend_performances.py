"""Append full-resolution continuation frames to the three existing scenes."""

import argparse
import json
import sys
from pathlib import Path

from import_originals import load_row, save_frames


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--extractor", type=Path, required=True)
    parser.add_argument("--artwork", type=Path, required=True)
    parser.add_argument("--work", type=Path, required=True)
    parser.add_argument("--dance", type=Path, required=True)
    parser.add_argument("--smoke", type=Path, required=True)
    args = parser.parse_args()
    sys.path.insert(0, str(args.extractor))
    import extract_strip_frames as extract

    pack_path = args.artwork / "pack.json"
    pack = json.loads(pack_path.read_text(encoding="utf-8"))
    scenes = (
        ("work", args.work, 6, [520, 470, 180, 420, 540, 510]),
        ("idol-dance", args.dance, 12, [380, 500, 420, 530, 780, 590]),
        ("smoke", args.smoke, 12, [480, 610, 940, 970, 780, 720]),
    )
    for key, source, start, durations in scenes:
        poses = load_row(source, len(durations), extract)
        # Close the whip loop with the approved opening pose. The raw last pose's tail hits the canvas edge.
        use_opening_pose = key == "work"
        if use_opening_pose:
            poses = poses[:-1]
        save_frames(poses, args.artwork / key, start=start)
        added = [{"file": f"{key}/{i:02}.png", "ms": duration}
                 for i, duration in enumerate(durations[:len(poses)], start=start)]
        if use_opening_pose:
            added.append({"file": "work/00.png", "ms": durations[-1]})
        scene = pack["scenes"][key]
        scene["frames"] = scene["frames"][:start] + added
        total = sum(frame["ms"] for frame in scene["frames"])
        print(f"{key}: {len(scene['frames'])} frames, {total} ms")

    pack_path.write_text(json.dumps(pack, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")


if __name__ == "__main__":
    main()
