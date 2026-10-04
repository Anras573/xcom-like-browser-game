#!/usr/bin/env python3
"""Build the game's texture sheets and manifests from the cached Kenney packs.

Run `python tools/fetch_assets.py` first. Output goes to src/Firewall.Web/wwwroot/assets/.
Everything is deterministic: inputs are read from the zips in sorted order, drawing is
procedural with fixed parameters, and PNGs are written without metadata.

World-space sheets are uniform grids of 64x64 cells (the engine draws `(texture, frameIndex)`);
only ui.png is a free-form packed atlas.
"""
import io
import json
import re
import sys
import xml.etree.ElementTree as ET
import zipfile
from pathlib import Path

from PIL import Image, ImageDraw

HERE = Path(__file__).resolve().parent
CACHE = HERE / ".cache"
OUT = HERE.parent / "src" / "Firewall.Web" / "wwwroot" / "assets"
CELL = 64

PACK_ZIPS = {}  # pack name -> ZipFile, filled in main()


def pack(name: str) -> zipfile.ZipFile:
    if name not in PACK_ZIPS:
        path = CACHE / f"{name}.zip"
        if not path.exists():
            sys.exit(f"{path} is missing; run tools/fetch_assets.py first")
        PACK_ZIPS[name] = zipfile.ZipFile(path)
    return PACK_ZIPS[name]


def read(pack_name: str, member: str) -> bytes:
    z = pack(pack_name)
    try:
        return z.read(member)
    except KeyError:
        sys.exit(f"{pack_name}.zip has no '{member}'; the pack layout changed")


def load(pack_name: str, member: str) -> Image.Image:
    im = Image.open(io.BytesIO(read(pack_name, member)))
    im.load()
    return im.convert("RGBA")


def save_png(im: Image.Image, path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    im.save(path, format="PNG", optimize=True)


def save_json(obj, path: Path) -> None:
    path.parent.mkdir(parents=True, exist_ok=True)
    path.write_text(json.dumps(obj, indent=2) + "\n", encoding="utf-8", newline="\n")


def grid_sheet(cells: list[Image.Image], columns: int) -> tuple[Image.Image, int]:
    rows = -(-len(cells) // columns)
    sheet = Image.new("RGBA", (columns * CELL, rows * CELL), (0, 0, 0, 0))
    for i, cell in enumerate(cells):
        sheet.paste(cell, ((i % columns) * CELL, (i // columns) * CELL))
    return sheet, rows


# --------------------------------------------------------------------------- tiles

TILES_COLUMNS, TILES_ROWS = 27, 20


def build_tiles() -> None:
    raw = read("top-down-shooter", "Tilesheet/tilesheet_complete.png")
    (OUT / "tiles.png").write_bytes(raw)  # unchanged, byte for byte
    size = Image.open(io.BytesIO(raw)).size
    if size != (TILES_COLUMNS * CELL, TILES_ROWS * CELL):
        sys.exit(f"tilesheet_complete.png is {size}, expected a 27x20 grid of 64px tiles")

    frames = {}
    pattern = re.compile(r"^([A-Za-z0-9_.]+):\s*\[\s*(\d+)\s*,\s*(\d+)\s*\]\s*(#.*)?$")
    for n, line in enumerate((HERE / "tile_map.yml").read_text().splitlines(), 1):
        stripped = line.strip()
        if not stripped or stripped.startswith("#"):
            continue
        m = pattern.match(stripped)
        if not m:
            sys.exit(f"tile_map.yml:{n}: cannot parse '{line}'")
        name, col, row = m.group(1), int(m.group(2)), int(m.group(3))
        if name in frames:
            sys.exit(f"tile_map.yml:{n}: duplicate name '{name}'")
        if col >= TILES_COLUMNS or row >= TILES_ROWS:
            sys.exit(f"tile_map.yml:{n}: [{col}, {row}] is outside the 27x20 grid")
        frames[name] = row * TILES_COLUMNS + col
    save_json(
        {
            "texture": "assets/tiles.png",
            "columns": TILES_COLUMNS,
            "rows": TILES_ROWS,
            "frames": dict(sorted(frames.items())),
        },
        OUT / "tiles.json",
    )


# --------------------------------------------------------------------------- characters


def build_characters() -> None:
    root = ET.fromstring(read("top-down-shooter", "Spritesheet/spritesheet_characters.xml"))
    atlas = load("top-down-shooter", "Spritesheet/spritesheet_characters.png")
    sub = {}
    for s in root.iter("SubTexture"):
        name = s.get("name")[: -len(".png")]
        name = name.replace("zoimbie", "zombie")  # typo in the Kenney atlas
        char, pose = name.split("_")
        x, y, w, h = (int(s.get(k)) for k in ("x", "y", "width", "height"))
        sub[(char, pose)] = atlas.crop((x, y, x + w, y + h))

    chars = sorted({c for c, _ in sub})
    poses = sorted({p for _, p in sub})
    if len(chars) != 9 or len(poses) != 6:
        sys.exit(f"expected 9 characters x 6 poses, got {chars} x {poses}")

    cells, frames, pivots = [], {}, {}
    for r, char in enumerate(chars):
        # The body sits at the left edge of every pose and arms/weapons extend to +X, so the
        # body centre (half the width of the `stand` pose) goes at the cell centre. That keeps
        # the body from wobbling between poses when the unit rotates around the cell centre.
        body = sub[(char, "stand")].width // 2
        for c, pose in enumerate(poses):
            im = sub[(char, pose)]
            x0 = min(CELL // 2 - body, CELL - im.width)  # the longest weapons would overflow
            y0 = (CELL - im.height) // 2
            cell = Image.new("RGBA", (CELL, CELL), (0, 0, 0, 0))
            cell.paste(im, (x0, y0))
            cells.append(cell)
            name = f"unit.{char}.{pose}"
            frames[name] = r * len(poses) + c
            dx = (x0 + body) - CELL // 2
            if dx:
                pivots[name] = [dx, 0]  # body centre relative to the cell centre, in pixels
    sheet, rows = grid_sheet(cells, len(poses))
    save_png(sheet, OUT / "characters.png")
    manifest = {
        "texture": "assets/characters.png",
        "columns": len(poses),
        "rows": rows,
        "frames": frames,
    }
    if pivots:
        manifest["pivotOffsets"] = pivots
    save_json(manifest, OUT / "characters.json")


# --------------------------------------------------------------------------- overlays

SS = 4  # supersampling factor for procedural art


def canvas() -> tuple[Image.Image, ImageDraw.ImageDraw]:
    im = Image.new("RGBA", (CELL * SS, CELL * SS), (0, 0, 0, 0))
    return im, ImageDraw.Draw(im)


def finish(im: Image.Image) -> Image.Image:
    return im.resize((CELL, CELL), Image.LANCZOS)


def s(v: float) -> int:
    return round(v * SS)


def move_range(fill, border) -> Image.Image:
    im, d = canvas()
    d.rounded_rectangle([s(3), s(3), s(61), s(61)], radius=s(6), fill=fill, outline=border, width=s(2))
    return finish(im)


def path_dot() -> Image.Image:
    im, d = canvas()
    d.ellipse([s(23), s(23), s(41), s(41)], fill=(255, 255, 255, 235), outline=(20, 30, 40, 200), width=s(2))
    return finish(im)


def path_end() -> Image.Image:
    im, d = canvas()
    d.ellipse([s(14), s(14), s(50), s(50)], outline=(255, 255, 255, 235), width=s(4))
    d.ellipse([s(26), s(26), s(38), s(38)], fill=(255, 255, 255, 235))
    return finish(im)


def selection_ring() -> Image.Image:
    im, d = canvas()
    d.ellipse([s(6), s(6), s(58), s(58)], outline=(80, 220, 255, 255), width=s(4))
    d.ellipse([s(10), s(10), s(54), s(54)], outline=(80, 220, 255, 90), width=s(2))
    return finish(im)


def shield_points(cx=32.0, top=10.0, bottom=56.0, half_w=18.0):
    return [
        (cx - half_w, top),
        (cx + half_w, top),
        (cx + half_w, top + 20),
        (cx, bottom),
        (cx - half_w, top + 20),
    ]


def shield(full: bool, color=(255, 255, 255, 255), fill_alpha=230) -> Image.Image:
    im, d = canvas()
    pts = [(s(x), s(y)) for x, y in shield_points()]
    d.polygon(pts, fill=(0, 0, 0, 90), outline=color)
    d.line(pts + [pts[0]], fill=color, width=s(3), joint="curve")
    inner = Image.new("L", im.size, 0)
    di = ImageDraw.Draw(inner)
    di.polygon([(s(x), s(y)) for x, y in shield_points(top=14, bottom=50, half_w=13)], fill=255)
    if not full:  # half cover: only the upper half is filled
        di.rectangle([0, s(33), im.width, im.height], fill=0)
    fill = Image.new("RGBA", im.size, color[:3] + (fill_alpha,))
    im.paste(fill, (0, 0), inner)
    return finish(im)


def eye(color=(255, 210, 70, 255)) -> Image.Image:
    im, d = canvas()
    d.ellipse([s(6), s(18), s(58), s(46)], fill=(0, 0, 0, 110), outline=color, width=s(3))
    d.ellipse([s(23), s(19), s(41), s(45)], fill=color)
    d.ellipse([s(28), s(26), s(36), s(38)], fill=(20, 20, 20, 255))
    return finish(im)


def drop(color=(230, 40, 50, 255)) -> Image.Image:
    im, d = canvas()
    d.ellipse([s(18), s(26), s(46), s(54)], fill=color, outline=(90, 10, 15, 255), width=s(2))
    d.polygon([(s(32), s(8)), (s(19.5), s(34)), (s(44.5), s(34))], fill=color)
    d.line([(s(32), s(8)), (s(19.5), s(34))], fill=(90, 10, 15, 255), width=s(2))
    d.line([(s(32), s(8)), (s(44.5), s(34))], fill=(90, 10, 15, 255), width=s(2))
    d.ellipse([s(24), s(35), s(30), s(43)], fill=(255, 255, 255, 140))
    return finish(im)


def corner_brackets() -> Image.Image:
    im, d = canvas()
    c = (255, 255, 255, 255)
    for (x, y, dx, dy) in [(5, 5, 1, 1), (59, 5, -1, 1), (5, 59, 1, -1), (59, 59, -1, -1)]:
        d.line([(s(x), s(y + 14 * dy)), (s(x), s(y)), (s(x + 14 * dx), s(y))], fill=c, width=s(3), joint="curve")
    return finish(im)


def fit_cell(im: Image.Image) -> Image.Image:
    """Scale an image down (never up) to fit a 64x64 cell and centre it."""
    scale = min(CELL / im.width, CELL / im.height, 1.0)
    if scale < 1.0:
        im = im.resize((max(1, round(im.width * scale)), max(1, round(im.height * scale))), Image.LANCZOS)
    cell = Image.new("RGBA", (CELL, CELL), (0, 0, 0, 0))
    cell.paste(im, ((CELL - im.width) // 2, (CELL - im.height) // 2))
    return cell


def tile_cell(col: int, row: int) -> Image.Image:
    tiles = load("top-down-shooter", "Tilesheet/tilesheet_complete.png")
    return tiles.crop((col * CELL, row * CELL, (col + 1) * CELL, (row + 1) * CELL))


def build_overlays() -> None:
    entries = [
        ("overlay.move_range", move_range((60, 160, 255, 90), (120, 200, 255, 230))),
        ("overlay.dash_range", move_range((255, 200, 60, 90), (255, 220, 120, 230))),
        ("overlay.attack_range", move_range((255, 70, 70, 90), (255, 130, 130, 230))),
        ("overlay.cursor", corner_brackets()),
        ("overlay.path_dot", path_dot()),
        ("overlay.path_end", path_end()),
        ("overlay.selection_ring", selection_ring()),
        ("overlay.shield_half", shield(False)),
        ("overlay.shield_full", shield(True)),
        (
            "overlay.crosshair",
            fit_cell(load("crosshair-pack", "PNG/Light/crosshair-006.png")),
        ),
        ("status.overwatch", eye()),
        ("status.hunker", shield(True, (110, 190, 255, 255))),
        ("status.bleeding", drop()),
        ("decal.corpse_scrap", tile_cell(21, 11)),
    ]
    columns = 4
    sheet, rows = grid_sheet([e[1] for e in entries], columns)
    save_png(sheet, OUT / "overlays.png")
    save_json(
        {
            "texture": "assets/overlays.png",
            "columns": columns,
            "rows": rows,
            "frames": {name: i for i, (name, _) in enumerate(entries)},
        },
        OUT / "overlays.json",
    )


# --------------------------------------------------------------------------- fx

FX_COLUMNS = 8
FX_FLIPBOOKS = [
    ("fx.black_smoke", "Black smoke/blackSmoke{:02d}.png", 25),
    ("fx.explosion", "Explosion/explosion{:02d}.png", 9),
    ("fx.flash", "Flash/flash{:02d}.png", 9),
    ("fx.white_puff", "White puff/whitePuff{:02d}.png", 19),
]


def build_fx() -> None:
    cells, frames, flipbooks = [], {}, {}
    for name, pattern, count in FX_FLIPBOOKS:  # each flipbook starts on a new row
        while len(cells) % FX_COLUMNS:
            cells.append(Image.new("RGBA", (CELL, CELL), (0, 0, 0, 0)))
        start = len(cells)
        for i in range(count):
            im = load("smoke-particles", "PNG/" + pattern.format(i))
            cells.append(fit_cell(im))
            frames[f"{name}.{i:02d}"] = start + i
        flipbooks[name] = {"start": start, "count": count}
    sheet, rows = grid_sheet(cells, FX_COLUMNS)
    save_png(sheet, OUT / "fx.png")
    save_json(
        {
            "texture": "assets/fx.png",
            "columns": FX_COLUMNS,
            "rows": rows,
            "frames": frames,
            "flipbooks": flipbooks,
        },
        OUT / "fx.json",
    )


# --------------------------------------------------------------------------- ui atlas

UI_WIDTH = 1024
EXTRUDE = 2

PANELS = ["panel_glass", "panel_glass_screws", "panel_rectangle", "panel_rectangle_screws", "panel_square", "panel_square_screws"]
BUTTONS = ["button_rectangle", "button_rectangle_depth", "button_square", "button_square_depth"]
COLORS = ["Blue", "Green", "Grey", "Red", "Yellow"]
BAR_PARTS = ["l", "m", "r"]
ICONS = [
    "arrowDown", "arrowLeft", "arrowRight", "arrowUp", "checkmark", "cross", "door", "exclamation",
    "export", "forward", "gear", "home", "import", "information", "locked", "medal1", "minus",
    "multiplayer", "next", "pause", "plus", "power", "previous", "question", "return", "singleplayer",
    "star", "target", "trashcan", "unlocked", "warning", "wrench", "zoomIn", "zoomOut",
]
CROSSHAIRS = [6, 7, 8, 9, 10, 12, 13, 26, 28, 34]
INSET = 16


def extrude(im: Image.Image, e: int) -> Image.Image:
    w, h = im.size
    out = Image.new("RGBA", (w + 2 * e, h + 2 * e), (0, 0, 0, 0))
    out.paste(im, (e, e))
    out.paste(im.crop((0, 0, w, 1)).resize((w, e), Image.NEAREST), (e, 0))
    out.paste(im.crop((0, h - 1, w, h)).resize((w, e), Image.NEAREST), (e, h + e))
    out.paste(im.crop((0, 0, 1, h)).resize((e, h), Image.NEAREST), (0, e))
    out.paste(im.crop((w - 1, 0, w, h)).resize((e, h), Image.NEAREST), (w + e, e))
    for (sx, sy, dx, dy) in [(0, 0, 0, 0), (w - 1, 0, w + e, 0), (0, h - 1, 0, h + e), (w - 1, h - 1, w + e, h + e)]:
        out.paste(Image.new("RGBA", (e, e), im.getpixel((sx, sy))), (dx, dy))
    return out


def build_ui() -> None:
    items = []  # (name, image, insets or None)
    ui = "PNG/Extra/Default/{}.png"
    for n in PANELS:
        items.append((f"ui.{n}", load("ui-pack-sci-fi", ui.format(n)), [INSET] * 4))
    for n in BUTTONS:
        bottom = INSET + 4 if n.endswith("depth") else INSET
        items.append((f"ui.{n}", load("ui-pack-sci-fi", ui.format(n)), [INSET, INSET, INSET, bottom]))
    for color in COLORS:
        for shape in ("rectangle", "square"):
            n = f"button_square_header_large_{shape}"
            im = load("ui-pack-sci-fi", f"PNG/{color}/Default/{n}.png")
            items.append((f"ui.{n}.{color.lower()}", im, [INSET] * 4))
    for part in BAR_PARTS:
        items.append((f"ui.bar_shadow.{part}", load("ui-pack-sci-fi", f"PNG/Extra/Default/bar_shadow_square_large_{part}.png"), None))
        for color in COLORS:
            if color == "Grey":
                continue
            im = load("ui-pack-sci-fi", f"PNG/{color}/Default/bar_square_large_{part}.png")
            items.append((f"ui.bar.{color.lower()}.{part}", im, None))
    for n in ICONS:
        items.append((f"icon.{n}", load("game-icons", f"PNG/White/1x/{n}.png"), None))
    for i in CROSSHAIRS:
        items.append((f"crosshair.light.{i:03d}", load("crosshair-pack", f"PNG/Light/crosshair-{i:03d}.png"), None))

    names = [i[0] for i in items]
    if len(set(names)) != len(names):
        sys.exit("duplicate ui region names")

    # Deterministic shelf packing: tallest first, ties by name. Footprints include the
    # extruded edge, so neighbouring regions never share texels.
    order = sorted(items, key=lambda it: (-it[1].height, it[0]))
    placed, x, y, shelf_h = [], 0, 0, 0
    for name, im, insets in order:
        fw, fh = im.width + 2 * EXTRUDE, im.height + 2 * EXTRUDE
        if x + fw > UI_WIDTH:
            x, y, shelf_h = 0, y + shelf_h, 0
        placed.append((name, im, insets, x, y))
        x += fw
        shelf_h = max(shelf_h, fh)
    used_h = y + shelf_h
    height = 1
    while height < used_h:
        height *= 2

    atlas = Image.new("RGBA", (UI_WIDTH, height), (0, 0, 0, 0))
    regions = {}
    for name, im, insets, px, py in placed:
        atlas.paste(extrude(im, EXTRUDE), (px, py))
        region = {"x": px + EXTRUDE, "y": py + EXTRUDE, "w": im.width, "h": im.height}
        if insets:
            region["insets"] = insets  # left, top, right, bottom in pixels
        regions[name] = region
    save_png(atlas, OUT / "ui.png")
    save_json(
        {
            "texture": "assets/ui.png",
            "width": UI_WIDTH,
            "height": height,
            "extrude": EXTRUDE,
            "regions": dict(sorted(regions.items())),
        },
        OUT / "ui.json",
    )


# --------------------------------------------------------------------------- licenses, fonts

LICENSE_PACKS = [
    "top-down-shooter", "ui-pack-sci-fi", "kenney-fonts", "crosshair-pack", "game-icons",
    "smoke-particles", "sci-fi-sounds", "impact-sounds", "interface-sounds",
]


def copy_licenses_and_fonts() -> None:
    for name in LICENSE_PACKS:
        z = pack(name)
        members = [m for m in z.namelist() if m.lower() in ("license.txt", "readme.txt") and m.count("/") == 0]
        lic = [m for m in members if m.lower() == "license.txt"]
        if not lic:
            sys.exit(f"{name}.zip has no License.txt")
        (OUT / "licenses").mkdir(parents=True, exist_ok=True)
        (OUT / "licenses" / f"{name}.txt").write_bytes(z.read(lic[0]))
    # Fonts are loaded at runtime (not baked); ship only the one the debug scene needs.
    fonts = OUT / "fonts"
    fonts.mkdir(parents=True, exist_ok=True)
    (fonts / "KenneyFuture.ttf").write_bytes(read("kenney-fonts", "Fonts/Kenney Future.ttf"))


def main() -> int:
    OUT.mkdir(parents=True, exist_ok=True)
    build_tiles()
    build_characters()
    build_overlays()
    build_fx()
    build_ui()
    copy_licenses_and_fonts()
    total = sum((OUT / f).stat().st_size for f in ("tiles.png", "characters.png", "overlays.png", "fx.png", "ui.png"))
    print(f"sheets total {total / 1024:.0f} KiB")
    if total > 4 * 1024 * 1024:
        sys.exit("sheet PNGs exceed the 4 MB budget")
    return 0


if __name__ == "__main__":
    sys.exit(main())
