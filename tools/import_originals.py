"""Import original transparent artwork from an existing Vespera generation run."""

import argparse
import json
import sys
from pathlib import Path

from PIL import Image


CANVAS = (576, 624)
MARGIN = 24
GROUND = CANVAS[1] - MARGIN
SCENES = {
    "idle": ("idle", "Спокойное ожидание", [700, 260, 180, 400, 350, 680], 0),
    "work": ("running", "Щелчок плетью", [440, 420, 360, 180, 360, 620], 0),
    "smoke": ("waiting", "Перекур", [480, 470, 440, 620, 750, 1050], 4),
    "wave": ("waving", "Приветствие", [420, 360, 410, 680], 0),
    "dance": ("jumping", "Танцевальный прыжок", [420, 260, 280, 300, 600], 0),
    "inspect": ("review", "Проверка результата", [390, 340, 360, 380, 360, 680], 0),
    "failed": ("failed", "Реакция на сбой", [340, 290, 300, 330, 270, 360, 390, 640], 0),
    "step-right": ("running-right", "Бег вправо", [150] * 8, 0),
    "step-left": ("running-left", "Бег влево", [150] * 8, 0),
}


def torso_center(frame):
    width, height = frame.size
    alpha = frame.getchannel("A")
    band = alpha.crop((0, round(height * .43), width, round(height * .55)))
    box = band.point(lambda value: 255 if value > 96 else 0).getbbox()
    return (box[0] + box[2]) / 2 if box else width / 2


def load_row(path, count, extract):
    with Image.open(path) as image:
        rgba = extract.load_native_alpha_image(image, path)
    return load_strip(rgba, count, extract)


def load_strip(rgba, count, extract):
    groups = extract.component_frame_groups(rgba, count)
    if groups is None or len(groups) != count:
        raise ValueError(f"Expected {count} distinct poses")
    result = []
    for group in groups:
        left, top, right, bottom = extract.component_bounds(group)
        frame = extract.component_group_image(rgba, group, padding=0)
        if frame.size != (right - left, bottom - top):
            raise ValueError("Pose bounding box mismatch")
        result.append((frame, torso_center(frame), top, bottom))
    return result


def save_frames(items, target, *, start=0, allow_enlarging=False):
    max_bottom = max(bottom for _, _, _, bottom in items)
    height = max_bottom - min(top for _, _, top, _ in items)
    extent = max(max(anchor, frame.width - anchor) for frame, anchor, _, _ in items)
    scale = min((GROUND - MARGIN) / height, (CANVAS[0] / 2 - MARGIN) / extent)
    if not allow_enlarging:
        scale = min(scale, 1)
    target.mkdir(parents=True, exist_ok=True)
    for index, (frame, anchor, _, bottom) in enumerate(items):
        size = tuple(round(value * scale) for value in frame.size)
        scaled = frame.resize(size, Image.Resampling.LANCZOS) if size != frame.size else frame
        x = round(CANVAS[0] / 2 - anchor * scale)
        y = GROUND - round((max_bottom - bottom) * scale) - scaled.height
        if x < 0 or y < 0 or x + scaled.width > CANVAS[0] or y + scaled.height > CANVAS[1]:
            raise ValueError(f"Pose {index + 1} outside the canvas")
        canvas = Image.new("RGBA", CANVAS)
        canvas.alpha_composite(scaled, (x, y))
        canvas.save(target / f"{index + start:02}.png")


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument("--run", type=Path, required=True)
    parser.add_argument("--extractor", type=Path, required=True)
    parser.add_argument("--out", type=Path, required=True)
    args = parser.parse_args()
    sys.path.insert(0, str(args.extractor))
    import extract_strip_frames as extract

    manifest = json.loads((args.run / "imagegen-jobs.json").read_text(encoding="utf-8-sig"))
    sources = {job["id"]: Path(job["source_path"]) for job in manifest["jobs"] if job.get("source_path")}
    pack = {"scenes": {}, "lookFrames": []}
    for name, (kind, label, durations, loop_from) in SCENES.items():
        items = load_row(sources[kind], len(durations), extract)
        save_frames(items, args.out / name)
        pack["scenes"][name] = {
            "label": label,
            "loopFrom": loop_from,
            "frames": [{"file": f"{name}/{i:02}.png", "ms": ms} for i, ms in enumerate(durations)],
        }
    look_items = [item for row in ("look-row-9", "look-row-10") for item in load_row(sources[row], 8, extract)]
    save_frames(look_items, args.out / "look")
    pack["lookFrames"] = [f"look/{index:02}.png" for index in range(len(look_items))]
    (args.out / "pack.json").write_text(json.dumps(pack, ensure_ascii=False, indent=2) + "\n", encoding="utf-8")
    print(f"Exported {sum(len(s['frames']) for s in pack['scenes'].values())} animation frames and {len(look_items)} look frames at {CANVAS}.")


if __name__ == "__main__":
    main()
