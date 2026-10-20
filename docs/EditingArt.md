# Editing the art

Every sprite, sprite sheet and ground texture in Ice Coast is an editable PNG in
`Assets/StreamingAssets/Art`. Open them in Aseprite (or any pixel editor), save, and the game picks the
change up — even while it is running in Play mode, within about a second.

If a PNG is missing, the game falls back to the built-in, code-drawn art for it, so you can always delete a
file to get the original back. **Ice Coast > Art** in the Unity menu has:

| Menu item | What it does |
| --- | --- |
| Export Missing Sprites to PNG | Writes any sheet or texture that isn't there yet (never overwrites). |
| Reset All Sprites to Built-in Art | Overwrites every PNG and JSON with the built-in art (asks first). |
| Open Art Folder | Shows the folder in Finder / Explorer. |
| Reload Art (Play Mode) | Reloads now instead of waiting for the file watcher. |

Outside Unity, `dotnet run` in `tools/SimHarness/ArtExport` does the same export (`--overwrite` to replace).

## Sprite sheets

| File | Contents | Grid (columns x rows, cell size) |
| --- | --- | --- |
| `trees.png` | Spruce, fir, hardwood; each bare then snowy (rows) x 2 variants | 2 x 6, 17x26 |
| `tree_clusters.png` | Stands of three trees, same layout | 2 x 6, 29x31 |
| `skiers.png` | 6 skill levels x 4 looks (rows) x 8 poses | 8 x 24, 15x21 |
| `lift_carriers.png` | Chairs and gondola cabins: empty, with riders | 2 x 4, 37x24 |
| `lift_parts.png` | T-bar, rope-tow handle, sheaves, terminal column | 4 x 1, 15x23 |
| `lift_structures.png` | Per lift type: tower, bullwheel, terminal, top hut, bottom hut | 5 x 7, 72x109 |
| `cars.png` | 6 paint jobs x (boot, bonnet toward camera) | 2 x 6, 30x24 |
| `trail_lights.png` | Small, medium, large x (pole, lamp heads) | 2 x 3, 23x82 |
| `buildings.png` | Waffle Cabin | 1 x 1, 72x74 |
| `ui_icons.png` | Toolbar and window icons, 16x16 | 12 x 4, 20x20 |
| `ui_font.png` | The interface font (any opaque pixel is ink) | 16 x 7, 10x13 |

Each sheet is a grid of equal cells, one sprite per cell. In Aseprite, **File > Import Sprite Sheet** with
the cell size above turns a sheet into frames (handy for the skier poses), or just edit the PNG as one image.

Inside a sheet every sprite's **pivot** — the point the game stands on the map (a skier's feet, a chair's grip
on the cable, the middle of a building's tile on the ground) — is at the same spot in its cell, so frames and
variants line up.

### Rules

* Fully transparent pixels are cut out; anything at least half opaque is drawn fully opaque. No soft edges.
* Colours are used exactly as you paint them.
* **Stay inside the sprite's cell.** Each cell has a couple of empty pixels of padding around the sprite.
* **Drawing past a sprite's rectangle grows it** automatically, as long as it stays in the cell. The pivot stays
  on the same pixel, so the sprite doesn't jump.
* To make a sprite **smaller**, move its **pivot**, or make a cell bigger, edit the numbers in the sheet's
  `.json` file (`x`, `y`, `w`, `h` are the sprite's rectangle in the PNG from the top-left; `pivotX`, `pivotY`
  are inside that rectangle; `cellWidth`/`cellHeight` size the grid). Resize the PNG canvas to match.
* Side-on skier poses and cars are drawn facing one way; the game mirrors them for the other direction.
* Lift towers, the terminal column and light poles are stretched vertically in game to reach the cable or lamp
  height, so their full height maps to that height.
* UI font: rows 1–7 of a glyph sit above the baseline, rows 8–9 are descenders. A glyph's advance is its width
  plus one pixel.

## Ground textures (`ground/*.png`)

128x128 seamless tiles: powder, packed, corduroy in four directions, slush, ice, crud, rocky snow, grass, dirt,
rock, scree, water, walkway, road along x and y, parking stalls and aisle. In Aseprite turn on **View > Tiled
Mode** to see the seams.

They are *multipliers* laid over the ground's own colour (so the same powder works at noon, at dusk and in
shade): **128 grey means "no change"**, lighter brightens, darker darkens, and tinting a channel tints the
ground. Keep them close to 128 for snow; the built-in ones vary by only a few steps.

One tile of the map is 32x32 pixels of the texture, and a texture repeats every 4x4 map tiles. On screen the
image is turned 45° clockwise and squashed: the PNG's x axis runs toward the lower right of the screen (grid x)
and its y axis toward the lower left (grid y). Textures must stay 128x128.

## What gets reloaded live

Saving any PNG or JSON while the game runs redraws it in place. If an edit changed a sprite's size, the world
rebuilds its meshes once (a short hitch). Problems — a missing sprite in a JSON, a ground texture of the wrong
size, an unreadable file — are logged as one warning in the Console and the built-in art is used for that piece.
