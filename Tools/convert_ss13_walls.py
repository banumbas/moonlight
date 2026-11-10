#!/usr/bin/env python3
"""Convert /tgstation wall and window templates to Space Station 14 RSI directories.

The input is the five-column ``{name}.png`` template used by the SS13 icon
cutter.  Each column is 32x48 and represents one of the five corner types:
convex, concave, horizontal, vertical and flat.  The output uses the wall
layout used by Resources/Textures/_Moonlight/Structures/Walls/*.rsi: every
state has four directional frames in a 2x2 sprite sheet, with a 32x64 frame.

The cutter's ``bitmask/windows`` template is also supported.  It contains ten
32x64 columns: five regular corner templates followed by five alternate
templates used by SS13's split window renderer.  Moonlight's tall RSI layout
uses the regular templates with their top 16 pixels removed, producing the
same 32x48 input layout as walls.
"""

from __future__ import annotations

import argparse
import json
import shutil
import tomllib
import sys
from pathlib import Path

from PIL import Image


WIDTH = 32
SOURCE_HEIGHT = 48
FRAME_HEIGHT = 64
LICENSE = "CC-BY-SA-3.0"
WALL_COPYRIGHT = "take in https://github.com/wall-nerds/wallening/tree/stable/icons/turf/walls"
WINDOW_COPYRIGHT = (
    "take in "
    "https://github.com/wall-nerds/wallening/tree/stable/icons/obj/smooth_structures/windows"
)
DAMAGE_OVERLAY_SOURCES = (
    ("window_broken_light.png", "DamageOverlay_5"),
    ("window_broken_medium.png", "DamageOverlay_10"),
    ("window_broken_heavy.png", "DamageOverlay_20"),
)
DAMAGE_OVERLAY_FILENAMES = {filename for filename, _ in DAMAGE_OVERLAY_SOURCES}

# Source columns in the cutter template.
CONVEX, CONCAVE, HORIZONTAL, VERTICAL, FLAT = range(5)

# SS14 wall state order, retained for compatibility with the existing
# reinforced.rsi in this repository. Entries are source columns for the
# (NW, NE, SW, SE) corners of the 32x48 assembled icon.
STATE_CORNER_TYPES = (
    (CONVEX, CONVEX, CONVEX, CONVEX),
    (HORIZONTAL, VERTICAL, VERTICAL, HORIZONTAL),
    (CONVEX, CONVEX, CONVEX, CONVEX),
    (HORIZONTAL, VERTICAL, VERTICAL, HORIZONTAL),
    (VERTICAL, HORIZONTAL, HORIZONTAL, VERTICAL),
    (CONCAVE, CONCAVE, VERTICAL, VERTICAL),
    (VERTICAL, HORIZONTAL, HORIZONTAL, VERTICAL),
    (FLAT, FLAT, VERTICAL, VERTICAL),
)
STATE_SIGNATURES = (0x00, 0x07, 0x00, 0x07, 0x0B, 0x03, 0x0B, 0x03)

# Directional visibility masks. Directions are in the RSI/DMI order used by
# SS14: South, North, East, West. The source art is kept at the top of a
# 32x64 frame; the lower 16 pixels are transparent padding.
DIRECTION_MASKS = (
    (16, 16, 32, 48),  # south
    (0, 0, 16, 16),     # north
    (16, 0, 32, 16),    # east
    (0, 16, 16, 48),     # west
)


def load_config(source_path: Path) -> dict:
    config_path = source_path.with_name(source_path.name + ".toml")
    if not config_path.exists():
        return {}
    with config_path.open("rb") as config_file:
        return tomllib.load(config_file)


def is_window_template(path: Path, image: Image.Image) -> bool:
    template = load_config(path).get("template")
    if template == "bitmask/windows":
        return True
    if template is not None:
        return False

    # Some source assets (notably frosted_window and the paper fixing
    # overlays) do not have a sidecar config. Their exact cutter layout is
    # still unambiguous: ten 32x64 columns.
    return image.width == WIDTH * 10 and image.height % FRAME_HEIGHT == 0


def load_source(path: Path) -> list[list[Image.Image]]:
    image = Image.open(path).convert("RGBA")
    if is_window_template(path, image):
        if image.width < WIDTH * 10 or image.height % FRAME_HEIGHT:
            raise ValueError(
                f"{path} must be at least 320 pixels wide and a multiple of 64 pixels high; "
                f"got {image.width}x{image.height}"
            )
        frame_count = image.height // FRAME_HEIGHT
        # SS13's window renderer splits each 64px icon into upper/lower
        # states and has a second, alternate set of five columns for windows
        # adjacent to walls. Moonlight stores a single 48px-tall visible icon,
        # matching the regular set cropped from y=16 through y=64.
        frames = [
            [
                image.crop(
                    (
                        column * WIDTH,
                        frame * FRAME_HEIGHT + 16,
                        (column + 1) * WIDTH,
                        (frame + 1) * FRAME_HEIGHT,
                    )
                )
                for column in range(5)
            ]
            for frame in range(frame_count)
        ]
        # The vertical and flat templates extend into SS13's lower split
        # state. Keeping that strip in a single tall SS14 sprite makes the
        # lower window draw over the window above it. In Moonlight that strip
        # belongs to the neighbouring tile and must stay transparent.
        for frame in frames:
            for column in (VERTICAL, FLAT):
                frame[column].paste((0, 0, 0, 0), (0, 32, WIDTH, SOURCE_HEIGHT))
        return frames

    if image.width < WIDTH * 5 or image.height % SOURCE_HEIGHT:
        raise ValueError(
            f"{path} must be at least 160 pixels wide and a multiple of 48 pixels high; "
            f"got {image.width}x{image.height}"
        )
    frame_count = image.height // SOURCE_HEIGHT
    return [
        [
            image.crop((i * WIDTH, frame * SOURCE_HEIGHT, (i + 1) * WIDTH, (frame + 1) * SOURCE_HEIGHT))
            for i in range(image.width // WIDTH)
        ]
        for frame in range(frame_count)
    ]


def prefab_columns(source_path: Path) -> dict[int, int]:
    config = load_config(source_path)
    return {int(signature): int(column) for signature, column in config.get("prefabs", {}).items()}


def assemble(source: list[Image.Image], corner_types: tuple[int, int, int, int]) -> Image.Image:
    """Assemble a 32x48 icon from the five source corner templates."""
    out = Image.new("RGBA", (WIDTH, SOURCE_HEIGHT))
    # The SS13/SS14 wall split is at x=16, y=16 in the visible wall region.
    # A source wall is 48 pixels tall, so the lower piece is 32 pixels tall.
    regions = {
        "nw": (0, 0, 16, 16),
        "ne": (16, 0, 32, 16),
        "sw": (0, 16, 16, 48),
        "se": (16, 16, 32, 48),
    }
    positions = ("nw", "ne", "sw", "se")
    for corner, kind in zip(positions, corner_types):
        left, top, right, bottom = regions[corner]
        piece = source[kind].crop((left, top, right, bottom))
        out.paste(piece, (left, top))
    return out


def directional_frames(icon: Image.Image) -> list[Image.Image]:
    frames = []
    for left, top, right, bottom in DIRECTION_MASKS:
        frame = Image.new("RGBA", (WIDTH, FRAME_HEIGHT))
        frame.paste(icon.crop((left, top, right, bottom)), (left, top))
        frames.append(frame)
    return frames


def make_animation_sheet(frames: list[Image.Image]) -> Image.Image:
    sheet = Image.new("RGBA", (WIDTH * 2, FRAME_HEIGHT * 2 * len(frames)))
    # RSI indexes the sheet row-major.  Its loader calculates an offset for
    # each direction, so the cells must be laid out as:
    #   direction 0: frame 0..N, direction 1: frame 0..N, ...
    # Do not place a whole direction in one visual column: with more than two
    # frames that is not the same as contiguous row-major sheet indices.
    directions = [directional_frames(icon) for icon in frames]
    for direction_index in range(4):
        for frame_index, direction_set in enumerate(directions):
            direction = direction_set[direction_index]
            sheet_index = direction_index * len(frames) + frame_index
            x = (sheet_index % 2) * WIDTH
            y = (sheet_index // 2) * FRAME_HEIGHT
            sheet.paste(direction, (x, y))
    return sheet


def animation_delays(source_path: Path, frame_count: int) -> list[float] | None:
    config = load_config(source_path)
    if not config or frame_count == 1:
        return None
    configured = config.get("animation", {}).get("delays")
    if not configured:
        return [1.0] * frame_count
    # SS13 cutter delays are deciseconds; RSI metadata uses seconds.
    return [float(configured[i % len(configured)]) / 10 for i in range(frame_count)]


def write_rsi(source_path: Path, output_path: Path, prefix: str | None = None) -> None:
    source_frames = load_source(source_path)
    with Image.open(source_path) as source_image:
        copyright_text = (
            WINDOW_COPYRIGHT
            if is_window_template(source_path, source_image)
            else WALL_COPYRIGHT
        )
    output_path.mkdir(parents=True, exist_ok=True)
    state_prefix = prefix or source_path.stem.removesuffix("_wall")
    delays = animation_delays(source_path, len(source_frames))
    prefabs = prefab_columns(source_path)

    states = []
    for index, corner_types in enumerate(STATE_CORNER_TYPES):
        name = f"{state_prefix}{index}"
        signature = STATE_SIGNATURES[index]
        prefab = prefabs.get(signature)
        if index == 7:
            # The flat template is the material-filled centre used when all
            # four neighbours are present.  It happens to be black for the
            # original reinforced wall, but must retain each wall's own color
            # (notably DebugWall and the colored materials).
            icons = [source[FLAT] for source in source_frames]
        else:
            icons = [
                source[prefab] if prefab is not None else assemble(source, corner_types)
                for source in source_frames
            ]
        make_animation_sheet(icons).save(output_path / f"{name}.png")
        state = {"name": name, "directions": 4}
        if delays is not None:
            state["delays"] = [delays] * 4
        states.append(state)

    # The full icon is useful for previews and matches the repository's
    # existing wall RSI convention.
    full = Image.new("RGBA", (WIDTH, FRAME_HEIGHT * len(source_frames)))
    full_prefab = prefabs.get(0)
    for frame_index, source in enumerate(source_frames):
        full.paste(source[full_prefab if full_prefab is not None else CONVEX], (0, frame_index * FRAME_HEIGHT))
    full.save(output_path / "full.png")
    full_state = {"name": "full"}
    if delays is not None:
        full_state["delays"] = [delays]
    states.append(full_state)

    # The Classic prototypes use these construction states as visualizer
    # overlays. Keep them in the same RSI as the base wall, just like the
    # existing reinforced.rsi convention.
    construction_states: list[tuple[str, Path | None]] = []
    if state_prefix == "reinforced":
        construction_states = [
            (f"reinf_construct-{index - 1}", source_path.with_name(f"reinforced_wall_decon{index}.png"))
            for index in range(1, 7)
        ]
    elif state_prefix == "shuttle":
        construction_states = [(f"shuttle_construct-{index}", None) for index in range(6)]

    for state_name, construction_source in construction_states:
        target = output_path / f"{state_name}.png"
        if construction_source is None:
            shutil.copyfile(output_path / "full.png", target)
        else:
            decon_frames = load_source(construction_source)
            decon_full = Image.new("RGBA", (WIDTH, FRAME_HEIGHT * len(decon_frames)))
            for frame_index, decon_source in enumerate(decon_frames):
                decon_full.paste(decon_source[CONVEX], (0, frame_index * FRAME_HEIGHT))
            decon_full.save(target)
        states.append({"name": state_name})

    meta = {
        "version": 1,
        "size": {"x": WIDTH, "y": FRAME_HEIGHT},
        "license": LICENSE,
        "copyright": copyright_text,
        "states": states,
    }
    (output_path / "meta.json").write_text(
        json.dumps(meta, ensure_ascii=False, indent=2) + "\n", encoding="utf-8"
    )


def write_damage_overlay_rsi(input_path: Path, output_path: Path) -> bool:
    """Combine the three broken-window templates into a DamageVisuals RSI."""
    sources = [(input_path / filename, state) for filename, state in DAMAGE_OVERLAY_SOURCES]
    if not all(source.exists() for source, _ in sources):
        return False

    output_path.mkdir(parents=True, exist_ok=True)
    states = []
    for source, state_name in sources:
        source_frames = load_source(source)
        overlay = Image.new("RGBA", (WIDTH, FRAME_HEIGHT))
        overlay.paste(source_frames[0][CONVEX], (0, 0))
        overlay.save(output_path / f"{state_name}.png")
        states.append({"name": state_name})

    meta = {
        "version": 1,
        "size": {"x": WIDTH, "y": FRAME_HEIGHT},
        "license": LICENSE,
        "copyright": WINDOW_COPYRIGHT,
        "states": states,
    }
    (output_path / "meta.json").write_text(
        json.dumps(meta, ensure_ascii=False, indent=2) + "\n", encoding="utf-8"
    )
    return True


def iter_inputs(input_path: Path) -> list[Path]:
    if input_path.is_file():
        return [input_path]
    return sorted(input_path.glob("*.png"))


def main() -> int:
    parser = argparse.ArgumentParser(description=__doc__)
    parser.add_argument(
        "input", type=Path, help="A wall/window PNG template or a directory of templates"
    )
    parser.add_argument("output", type=Path, help="RSI directory or directory containing RSI outputs")
    parser.add_argument("--prefix", help="State prefix when converting one input")
    args = parser.parse_args()

    inputs = iter_inputs(args.input)
    if not inputs:
        parser.error(f"No PNG templates found in {args.input}")
    if args.input.is_file():
        output_paths = [args.output]
    else:
        output_paths = [args.output / f"{p.stem.removesuffix('_wall')}.rsi" for p in inputs]

    failed = 0
    for source, output in zip(inputs, output_paths):
        if args.input.is_dir() and source.name in DAMAGE_OVERLAY_FILENAMES:
            print(f"included {source.name} in combined broken-window overlays")
            continue
        try:
            write_rsi(source, output, args.prefix if args.input.is_file() else None)
            print(f"converted {source.name} -> {output}")
        except (OSError, ValueError) as error:
            if args.input.is_dir() and "must be at least" in str(error):
                print(f"skipped {source.name}: not a supported wall or window template")
                continue
            failed += 1
            print(f"error: {source}: {error}", file=sys.stderr)

    if args.input.is_dir():
        damage_output = args.output / "window_damage.rsi"
        try:
            if write_damage_overlay_rsi(args.input, damage_output):
                print(f"combined broken-window overlays -> {damage_output}")
        except (OSError, ValueError) as error:
            failed += 1
            print(f"error: broken-window overlays: {error}", file=sys.stderr)
    return 1 if failed else 0


if __name__ == "__main__":
    raise SystemExit(main())
