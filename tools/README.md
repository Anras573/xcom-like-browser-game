# Asset tools

```bash
pip install pillow
python tools/fetch_assets.py   # downloads the Kenney zips into tools/.cache/ (git-ignored)
python tools/build_sheets.py   # writes sheets, manifests, licenses to src/Firewall.Web/wwwroot/assets/
```

`fetch_assets.py` records each zip's SHA-256 in `tools/assets.lock.json` on the first run and
verifies it on every later run. It exits non-zero if a URL stops working or a hash changes.
`build_sheets.py` reads the zips directly, in sorted order, with fixed drawing parameters, so
two runs produce byte-identical output. Generated sheets and manifests are committed; zips are not.

## Output (`wwwroot/assets/`)

World-space textures are **uniform grids of 64×64 cells**: the engine draws a `(texture, frameIndex)` pair
(`SpriteSheet` / `Tileset`), never an arbitrary atlas rect. Only `ui.png` is free-form packed.

| Sheet | Grid | Contents |
|---|---|---|
| `tiles.png` | 27×20 | `tilesheet_complete.png` copied unchanged. Used as `Tileset("assets/tiles.png", 27, 20)`. |
| `characters.png` | 6×9 | One row per character (sorted), one column per pose (sorted: `gun hold machine reload silencer stand`). `frame = row × 6 + poseIndex`. |
| `overlays.png` | 4×4 | Range tiles, cursor, path dot/end, selection ring, cover shields, crosshair, status icons, corpse/scrap decal. Mostly drawn procedurally. |
| `fx.png` | 8×11 | Smoke, explosion, flash and puff flipbooks, each starting on its own row. |
| `ui.png` | 1024×512 | Shelf-packed 9-slice panels/buttons, bars, game icons, crosshairs. Every region has 2 px extruded edges. |

Fonts are not baked. `fonts/` holds `KenneyFuture.ttf`, `KenneyFutureNarrow.ttf` and `KenneyMiniSquare.ttf`, loaded at runtime (see `TextStyles`).
Each pack's `License.txt` is copied to `licenses/<pack>.txt`.

## Manifests

Grid sheets (`tiles.json`, `characters.json`, `overlays.json`, `fx.json`):

```json
{ "texture": "assets/characters.png", "columns": 6, "rows": 9,
  "frames": { "unit.soldier1.machine": 32 },
  "pivotOffsets": { "unit.hitman1.gun": [-1, 0] },
  "flipbooks": { "fx.explosion": { "start": 32, "count": 9 } } }
```

`pivotOffsets` and `flipbooks` are optional.

`ui.json` has pixel rects with a **top-left origin**, plus optional 9-slice insets `[left, top, right, bottom]`:

```json
{ "texture": "assets/ui.png", "width": 1024, "height": 512, "extrude": 2,
  "regions": { "ui.panel_glass": { "x": 2, "y": 2, "w": 64, "h": 64, "insets": [16, 16, 16, 16] } } }
```

### Naming

Names are dotted and unique across all manifests (`AssetRegistry` rejects duplicates).

- `unit.<character>.<pose>`: `unit.soldier1.stand`. Characters: `hitman1 manBlue manBrown manOld robot1 soldier1 survivor1 womanGreen zombie1`.
  Sprites face +X. The pivot is the **body centre at the cell centre**; weapons extend to the right.
  A few long-weapon frames (`gun`, `machine`, `silencer`) would overflow the cell, so they shift left by the overflow and list the shift in `pivotOffsets`.
- `overlay.*`, `status.*`, `decal.*`: `overlay.move_range`, `overlay.shield_half`, `status.overwatch`, `decal.corpse_scrap`.
- `fx.<name>.<nn>`: `fx.explosion.03`; `fx.<name>` is also a flipbook (`AssetRegistry.Flipbook`).
- Tiles (from `tile_map.yml`): `ground.*`, `floor.*`, `wall.<style>.*`, `prop.*`, `decal.*`. Trees are 2×2 multi-cell props (`prop.tree.green.tl/tr/bl/br`); #16 must place all four parts.
- UI: `ui.<panel|button|bar…>`, `icon.<kenneyName>`, `crosshair.light.<nnn>`.

### Tile map and wall auto-tiling

`tile_map.yml` is hand curated: `name: [col, row]`, converted to `row × 27 + col`.
The wall tiles are **blob auto-tiles on a dark floor cell**, not wall-thickness cells: a tile has a wall on each
edge that borders the outside, plus a small corner "knob" where only the diagonal neighbour is outside.
The grey style is mapped for straights, corners, T-junctions, cross, end caps and single edges:

| Names | Meaning |
|---|---|
| `wall.grey.n/e/s/w` | one edge |
| `wall.grey.straight_h/_v` | N+S / E+W |
| `wall.grey.corner_nw/ne/sw/se` | two adjacent edges (`.plain` = without knobs) |
| `wall.grey.t_n/e/s/w` | one edge plus the two knobs on its far side |
| `wall.grey.cross` | four knobs, no edges |
| `wall.grey.end_n/e/s/w` | three edges, open on that side |
| `wall.grey.knob_*` | no edges, only the listed corner knobs |

The orange and tan wall styles use the same layout one block over and can be added to the yml the same way.

## Sampling decision

`tiles.png` has no spacing between tiles, so the default `TextureSampling.Default` (Linear + Clamp) bleeds neighbouring
cells at fractional camera positions. A checkerboard `Tilemap` of grass and water (cells that are not adjacent in the
texture) showed 1 px seams at 1× and 2× when panned to sub-pixel positions, and none with `Pixelated`. The debug scene
cannot show this, because it draws sheets in their source layout, so a bleed there samples a matching colour.

**Decision:** `assets/tiles.png` uses `TextureSampling.Pixelated`, set in `Game.razor` before preload. The other sheets keep
the default: their cells are drawn as sprites with transparent padding. #16 should re-check the real map, and if
`Pixelated` looks too blocky at some zoom, ask Yaeger for margin/spacing support on `SpriteSheet`/`Tileset`.
