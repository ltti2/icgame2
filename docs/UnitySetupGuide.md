# Ice Coast: Unity Guide

This guide takes you from an empty Unity install to playing the prototype. It then explains every Unity-specific piece of the project, so you can extend it confidently. It assumes you know C# but are not a Unity expert.

**Contents**

1. [Install and create the project](#1-install-and-create-the-project)
2. [Add the code](#2-add-the-code)
3. [Assembly definitions: how the code is split](#3-assembly-definitions-how-the-code-is-split)
4. [Set up the scene](#4-set-up-the-scene)
5. [Press Play: your first ten minutes](#5-press-play-your-first-ten-minutes)
6. [Tuning with the SimConfig asset](#6-tuning-with-the-simconfig-asset)
7. [Rendering: camera, shader and isometric depth](#7-rendering-camera-shader-and-isometric-depth)
8. [Input](#8-input)
9. [Tests](#9-tests)
10. [Saves](#10-saves)
11. [Performance and profiling](#11-performance-and-profiling)
12. [Moving to real pixel art](#12-moving-to-real-pixel-art)
13. [The interface](#13-the-interface)
14. [Making a build](#14-making-a-build)
15. [Version control](#15-version-control)
16. [Troubleshooting](#16-troubleshooting)

---

## 1. Install and create the project

1. Install **Unity Hub**, then install **Unity 6 LTS (6000.0.x or newer)**. Include *Windows Build Support (Mono)* (or Mac/Linux equivalents) and *Visual Studio* or point Unity at Rider/VS Code later.
2. In Unity Hub: **New project**, choose the **Universal 3D** template.
   * "3D" may sound wrong for a 2D isometric game. It isn't: the game draws its own isometric meshes with an orthographic camera, and it needs a normal depth buffer, which the 3D renderer provides. The **Universal 2D** template's 2D Renderer is built around sprite sorting layers and is the wrong fit for this approach.
   * **3D (Built-In Render Pipeline)** also works. URP is recommended because it is Unity's actively developed pipeline.
3. Name it `IceCoast` and create it.
4. Optional but recommended, in **Edit > Project Settings**:
   * **Editor > Enter Play Mode Settings**: set *When entering Play Mode* to *Reload Scene only* (skips the domain reload). Play mode then starts almost instantly. This is safe here: the only static mutable state (`RenderMaterials`, `GameInput.KeyboardSuppressed`) is re-initialised by the bootstrap and HUD every play session.
   * **Player > Other Settings > Color Space**: leave at *Linear* (the default). The shader handles the conversion; see [section 7](#7-rendering-camera-shader-and-isometric-depth).
   * **Quality**: for crisp pixel art later, set *Anti Aliasing* to *Disabled* on the URP asset (`Assets/Settings/*_RPAsset`), and turn off *HDR* and *Post-processing* on the camera. Not required for the prototype.

## 2. Add the code

Unity's project folder *is* the repository root. Copy these from this repo into the folder Unity just created:

| Copy | To |
| --- | --- |
| `Assets/_Project/` | `IceCoast/Assets/_Project/` |
| `docs/`, `tools/`, `README.md` | `IceCoast/` |
| `.gitignore`, `.gitattributes` | `IceCoast/` |

Switch back to Unity. It compiles everything automatically; watch the bottom-right spinner. The **Console** (Window > General > Console) should show no errors.

> **Why `_Project`?** Everything you author lives in one folder that sorts to the top, separate from third-party packages and Unity's own `Settings/` folder. Moving or upgrading the project means moving one folder.

### Folder layout inside Unity

```
Assets/_Project/
├── Scripts/Simulation   game rules (no Unity)
├── Scripts/View         everything Unity: rendering, tools, HUD, camera, input
├── Scripts/Editor       editor-only menu items
├── Tests/EditMode       unit tests
├── Shaders              IceCoast/UnlitVertexColor
├── Config               SimConfig assets (created by the setup menu)
├── Scenes               save your Game scene here
├── Prefabs, Art, Audio  empty, ready for content
```

## 3. Assembly definitions: how the code is split

Each `.asmdef` file turns a folder into its own compiled assembly (DLL). Ice Coast has four:

| Assembly | Folder | References | Key setting |
| --- | --- | --- | --- |
| `IceCoast.Simulation` | Scripts/Simulation | nothing | **No Engine References = on** |
| `IceCoast.View` | Scripts/View | Simulation, Unity.InputSystem | |
| `IceCoast.Editor` | Scripts/Editor | Simulation, View | Editor platform only |
| `IceCoast.Tests.EditMode` | Tests/EditMode | Simulation, NUnit, TestRunner | Editor only, `UNITY_INCLUDE_TESTS` |

What this buys you:

* **The architecture is enforced, not just documented.** If anyone types `using UnityEngine;` in the simulation, it will not compile. Click `IceCoast.Simulation.asmdef` and look at *No Engine References* in the Inspector.
* **Faster iteration.** Editing a HUD file recompiles only `IceCoast.View`, not the simulation.
* **The dependency arrow points one way.** View knows Simulation; Simulation never knows View.

When you add a new folder of view code, it is automatically part of `IceCoast.View` because it sits under that asmdef. Only create a new asmdef for a genuinely separate module, such as a future modding API.

## 4. Set up the scene

### The quick way

1. **File > New Scene** (Basic or Empty), then **File > Save As** `Assets/_Project/Scenes/Game.unity`.
2. Menu **Ice Coast > Set Up Game Scene**. This:
   * creates a `Game` GameObject with the **GameBootstrap** component,
   * creates or reuses the Main Camera and makes it orthographic,
   * creates `Config/DefaultSimConfig.asset` and `Config/SandboxSimConfig.asset`,
   * wires the shader, camera and config into the bootstrap.
3. **Ctrl+S** to save the scene.
4. **File > Build Profiles** (Unity 6) or **Build Settings**: click *Add Open Scenes* so `Game` is scene 0.

### The manual way (to understand what the menu did)

1. Create an empty GameObject named `Game`. **Add Component > Game Bootstrap**.
2. In the Project window: **right-click Config > Create > Ice Coast > Sim Config**. Name it `DefaultSimConfig`.
3. Select `Game` and fill in the GameBootstrap fields:
   * **Config**: `DefaultSimConfig`
   * **Unlit Shader**: `IceCoast/UnlitVertexColor` (drag `Shaders/IceCoastUnlit.shader`)
   * **Game Camera**: Main Camera
4. Main Camera: Projection *Orthographic*, position `(0, 0, -100)`, rotation `(0, 0, 0)`. The bootstrap enforces these at runtime anyway.

### What the bootstrap builds at runtime

`GameBootstrap` is the **composition root**: the one place where objects are created and connected. Press Play and expand `Game` in the Hierarchy:

```
Game
├── Simulation     SimulationRunner   drives ticks, speed, save/load
├── Terrain        TerrainRenderer    └── Chunk x,y (256)  ── Terrain, Trees
├── Scenery        SceneryRenderer    └── Scenery x,y      ── the range beyond the map
├── Day Light      DayLightController  sun, sky colour (shader globals)
├── Contours       ContourRenderer    └── Contour Lines    ── one mesh per chunk
├── Lifts          LiftRenderer       └── Structures, Chairs
├── Guests         GuestRenderer
├── Tool Preview   PreviewRenderer
├── Tools          ToolController
└── HUD            GameHud            toolbar, windows, status bar, minimap
```

Nothing uses singletons or `FindObjectOfType`. Each component receives what it needs in `Initialize(...)`, so you can read `GameBootstrap.Awake` top to bottom and know every dependency in the game.

**Bootstrap inspector options**

| Field | Effect |
| --- | --- |
| Config | Which SimConfig asset a new game uses |
| Sandbox Mode | Infinite money (the GDD's testing mode) |
| Override Seed / Seed | Generate a different mountain |

## 5. Press Play: your first ten minutes

You start at 7:30 AM on Tuesday, Dec 1, looking at the base area in the bottom-right corner of the map. The summit (4,241 ft) is toward the top of the screen, with lower peaks around it, all densely forested like an untouched Vermont mountain. Land you don't own has a light blue-grey tint. Beyond the map edge, the mountain range continues as scenery. Measurements are in feet, inches, mph and °F; Settings (the gear on the toolbar) switches to metric.

1. **Build a rope tow.** Press `2` or click the chairlift button on the toolbar; a dropdown of lift types opens under it, with Rope Tow selected. Click a tile at the top edge of the grey base area, then a tile about 25 tiles up the cleared slope. The line previews green with cost, length, rise and grade, or red with the reason it can't be built (too steep, not owned, too long).
2. **Paint a run.** Press `3`. Drag from near the tow's top station down to near its bottom station, beside the lift line. The tooltip next to the cursor estimates the rating. Press **Enter**.
   * A run is "connected" when a lift top is within 4 tiles of any run tile, and a lift bottom is within 12 tiles of the run's lowest tile.
   * Click the run with the Inspect tool (`1`) to open its window and see its issues ("No lift reaches the top", "guests walk out").
3. **Watch.** From 8:00 guests arrive (they need at least one lift), queue in a corral, ride, and carve S-turns down your run, each holding their own line across it and drifting toward snow they like. Outfit colour = skill: yellow first-timer, orange beginner, light blue intermediate, red advanced, purple expert, black pro.
4. **Speed up.** Press `.` for 2x/4x/8x and Space to pause. Building works while paused.
5. **Reading the slope.** The sun moves through the day: the mountain faces east, so mornings light the face you're looking at and afternoons put it in shade behind the summit. Steeper ground is always a little darker. Press `C` for contour lines (100 ft, bold every 500 ft): tightly bunched lines mean steep. While painting a run, any stretch where skiers would travel uphill turns yellow.
6. **Overlays.** Use the map-mode buttons above the minimap (bottom right), or press `O` to cycle: Difficulty (slope rating of every tile), Snow Depth, Snow Quality, Wear (skier compaction: watch your run turn red over the day), Temperature, Ownership.
7. **Mislabel a run.** Inspect your run and sign it as Green even if it's Blue. You'll see the warning, and injured guests will sue you. This is intended.
8. **End of day.** At 16:00 the lifts stop, guests go home, and at midnight the day closes with a summary in the status bar. Reputation moves toward the day's average guest happiness.
9. **Expand.** Press `4`: parcels you can buy now are highlighted yellow; ones gated by reputation are purple. Buy adjacent land, cut trees (`6`), reshape terrain (`5`), build bigger lifts as reputation unlocks them.
   * The forest is dense everywhere, so cut runs before painting them. A run painted through trees is a glade: fun for experts, dangerous for everyone else.
10. **Build up the mountain.** Put a second lift's bottom station beside your first run (within about 200 ft) or near the first lift's top. Guests plan routes across the network: they ride the lower lift to reach the upper one, peel off mid-run to lap it, and head back down when they want to go home. Inspect a run to see what it "connects to".

`F5` quick-saves, `F9` quick-loads.

## 6. Tuning with the SimConfig asset

Select `Config/DefaultSimConfig.asset`. Every balance number in the game is here, grouped into foldouts: Map, Time, Terrain, Economy, Guests, Runs, Snow, Weather, Lifts.

How it works:

* `SimConfig` is a plain C# class in the **simulation** assembly marked `[Serializable]`.
* `SimConfigAsset` is a `ScriptableObject` in the **view** assembly that holds one. Unity can draw and save the plain class without the simulation ever referencing Unity.
* On New Game, the bootstrap takes a **copy** (`CreateRuntimeCopy`, via `JsonUtility`), so runtime changes never write back into your asset.

Tips:

* Changes apply to the next **new game**. Stop and press Play again.
* Make variants by duplicating the asset (Ctrl+D): `SmallTestMap` (128x128 is great for fast iteration), `HardMode`, `SandboxSimConfig`.
* Map width and height are rounded down to multiples of 32 (the chunk size) by `OnValidate`.
* For bigger balance passes, use the headless runner. It simulates a week in about a second:
  ```bash
  cd tools/SimHarness/Balance
  dotnet run -c Release -- 14
  ```
  It uses `SimConfig.CreateDefault()`. To test values, edit the defaults in `SimConfig.cs` or load JSON in `BalanceRunner.cs`.

> **Gotcha:** the defaults written in `SimConfig.cs` only apply when the asset is *created*. Once an asset exists, its saved values win. If you change a default in code and want it, reset the field in the Inspector (right-click > Reset) or recreate the asset.

## 7. Rendering: camera, shader and isometric depth

### The projection

`IsoProjection` (View/Iso) is the only code that converts grid coordinates to world space:

```
world.x = (gx - gy) * 1.0        // tile is 64 px wide  = 2 units at 32 px/unit
world.y = -(gx + gy) * 0.5 + h * 0.125   // 32 px tall; 4 px per metre of height
world.z = -(gx + gy) * 0.01      // depth: nearer diagonals are closer to the camera
```

To change the art scale (for example 4 px per metre to 8), change the constants there and nothing else.

### How depth sorting works (and why there are no sort keys)

The GDD planned a sort key, `(x + y) * K + height * H + layerOffset`. The prototype goes one step further: every vertex gets its Z from the grid diagonal, and the shader depth-tests. For a heightfield viewed from a fixed isometric angle, "larger diagonal = nearer" is always correct. So:

* Hills in front correctly hide terrain, guests and lifts behind them.
* Lift cables, towers and chairs need no special cases; they get a slight Z bias so they sit in front of the tile they stand on.
* There is no per-frame sorting at all.

### The shader

`Shaders/IceCoastUnlit.shader` (`IceCoast/UnlitVertexColor`) is a tiny unlit shader that outputs vertex colour. Notes:

* It is written in CG with no `LightMode` tag, so it runs in **both** URP (as `SRPDefaultUnlit`) and Built-in.
* `_ZTest`, `_ZWrite`, `_SrcBlend` and `_DstBlend` are material properties. `RenderMaterials` creates two materials from it: **Opaque** (depth-tested, for the world) and **Overlay** (always on top, alpha-blended, for tool previews).
* In Linear colour space it converts vertex colours from sRGB, so `Palette` colours look as authored.
* The bootstrap holds a serialized reference to the shader. That's what makes Unity include it in builds; `Shader.Find` alone would fail in a player.

### Meshes instead of GameObjects

| What | How it's drawn | Rebuilt when |
| --- | --- | --- |
| Terrain | One mesh per 32x32 chunk (4 vertices per tile) | Chunk `ShapeVersion` changes |
| Snow, overlays, run paint | Same mesh, colours only | `SurfaceVersion` or overlay changes |
| Trees | One mesh per chunk | With the terrain |
| Lift structures | One mesh for all lifts | `LiftsVersion` changes or selection changes |
| Chairs | One mesh, every frame | Always (animated) |
| Guests | One mesh, every frame, off-screen guests skipped | Always (interpolated between ticks) |
| Tool previews | One overlay mesh, every frame | Always |

`TerrainRenderer` has a **Frame Budget** field: how much chunk rebuilding it does per frame, visible chunks first. Raise it for faster initial load, lower it if snow updates cause hitches on a slow machine.

### Lighting and contours

* **Sunlight is computed in the shader**, not baked into vertex colours. Terrain vertices carry a slope normal in grid space, and `DayLightController` sets the sun direction, colour and ambient light as global shader properties every frame. The sun moves smoothly with no mesh rebuilds. A "sky" term darkens steep ground at any hour, so downhill reads darker. Overlays turn lighting down so their colours stay readable. Tweak sunrise, sunset, peak sun height and strength on the Day Light object.
* **Compass:** the mountain faces east, toward the viewer. In grid space east is (+1,+1), north (+1,-1), south (-1,+1). `TerrainShading` documents this, and the simulation's sun-exposure melt uses the same convention.
* **Contour lines** (`ContourRenderer`) are real line geometry, traced per chunk with marching squares and drawn just above the tree canopy so the forest can't hide them. They rebuild when terrain changes, when you change zoom (to keep a constant on-screen width) and when you switch units.

### Camera

`IsoCameraController` (added to the camera by the bootstrap):

* Zoom snaps to `0.125x, 0.25x, 0.5x, 1x, 2x, 3x, 4x` of the art scale. `orthographicSize = Screen.height / (2 * 32 * zoom)`, so at 1x one art pixel is one screen pixel. This keeps future pixel art crisp.
* Wheel zooms toward the cursor. WASD/arrows pan (Shift faster), middle mouse drags. Edge scrolling is off by default; toggle it in Settings. Click or drag on the minimap to jump the camera.
* There is no rotation, by design (GDD non-goal).

### Picking (mouse to tile)

Because terrain has height, many tiles can be under the same pixel. `IsoPicker` walks candidate tiles in the mouse's screen column from nearest to farthest and returns the first whose projected (sloped) quad contains the point. It uses the same diagonal split as the mesh, so what you click is what you see.

## 8. Input

`View/Input/GameInput.cs` wraps input so the rest of the code doesn't care which system is active.

* Unity 6 templates use the **Input System** package by default. Then `ENABLE_INPUT_SYSTEM` is defined and the wrapper uses `Keyboard.current` and `Mouse.current`.
* If your project uses the old **Input Manager** (Project Settings > Player > Active Input Handling = *Input Manager (Old)*), the wrapper uses `UnityEngine.Input` instead. No code changes needed.
* All key bindings are in one table in that file. Rebinding means editing a line there; a rebinding UI later only needs to replace that table.
* `Delete` closes every window that isn't pinned (like OpenTTD).
* While a text field in the HUD has focus (renaming a run), hotkeys are suppressed so typing `1` doesn't switch tools.

## 9. Tests

The Test Framework package ships with Unity 6 templates.

1. **Window > General > Test Runner**.
2. **EditMode** tab: you'll see `IceCoast.Tests.EditMode` with 44 tests.
3. **Run All**. They take about a second.

What they cover: chunk-border vertex sharing, slope maths, terrain max-step rule, run rating table, flow fields, lift validation and capacity, a full simulated day with guests, end-of-day cleanup, sandbox mode, land purchase, terraforming, the snow state machine, snow wear, the scenery seam and its regeneration on load, forest coverage, the summit position, guests routing to a mid-mountain lift, run exits, route values, unit formatting, **determinism** (same seed and commands give the same state) and **save/load continuing identically**.

The same test files run outside Unity:

```bash
cd tools/SimHarness/Tests
dotnet run -c Release            # all
dotnet run -c Release -- Lift    # names containing "Lift"
```

Add new tests to `Assets/_Project/Tests/EditMode/`. Use `TestGames.NewSmallGame()` for a fast 128x128 world and `TestGames.BuildStarterResort(sim)` for a lift plus run.

## 10. Saves

* Quick save: `F5`, quick load: `F9`. The toolbar's Save and Load dropdowns also have three save slots.
* Location: `Application.persistentDataPath/saves/quicksave.icsave`. Menu **Ice Coast > Open Save Folder** opens it. On Windows this is `%USERPROFILE%\AppData\LocalLow\<Company>\IceCoast\saves`.
* Format: gzip-compressed binary written by `Simulation/Persistence/SaveSystem.cs`, versioned with `CurrentVersion`. Files are written to a temp file and then renamed, so a crash mid-save can't corrupt the previous save.
* Config is **not** saved; it comes from the SimConfig asset. If you change map size in the config, old saves still load (map size is saved), but tuning values follow the asset.
* When you add state, follow the recipe in `docs/Architecture.md` section 4 (bump the version, guard new reads).

## 11. Performance and profiling

Measured in the headless harness on the 512x512 map: about 90,000 ticks per second with a few hundred guests, roughly 9,000x real time. In Unity the simulation is not the bottleneck; rendering setup is.

* **Window > Analysis > Profiler**: look for `SimulationRunner.Update` (all ticks for the frame) and `TerrainRenderer.LateUpdate` (chunk rebuilds).
* Settings > *Show performance info* opens a window with FPS, simulation milliseconds per frame, guests drawn and the hovered tile.
* **Window > Analysis > Frame Debugger** shows the draw calls: roughly one per visible chunk for terrain, one for trees, plus a handful for lifts, guests and previews.

When guest counts grow to thousands and you need more:

1. Profile first.
2. Move `GuestSystem`'s movement loop and `SnowSystem.UpdateChunk` to Burst jobs. Both already work on flat arrays, which is exactly what jobs want. Copy the arrays into `NativeArray`s at the boundary.
3. Draw guests with `Graphics.RenderMeshInstanced` instead of rebuilding a mesh.

## 12. Moving to real pixel art

The prototype colours vertices; the structure is designed so art slots in without touching the simulation.

**Import settings** for every sprite or texture (select it, then the Inspector):

* Texture Type: *Sprite (2D and UI)* (or Default for terrain atlases)
* Pixels Per Unit: **32**
* Filter Mode: **Point (no filter)**
* Compression: **None**
* Generate Mip Maps: off

Create a Preset from these settings and set it as the default for the `Art` folder (Project Settings > Preset Manager), so every new import is right.

**Terrain:** keep the chunk meshes, add UVs. In `ChunkMeshBuilder.BuildTerrain`, pick an atlas cell from the tile's terrain/snow state and emit UVs for its four corners. Add a texture to the shader (`tex2D(_MainTex, uv) * vertexColor`) so vertex colour still carries shading and overlays. Because slopes are geometry, you need one texture per *surface type*, not per slope shape.

**Trees, buildings, lift parts, guests:** replace `AddTriangle` / `AddUprightRect` calls with textured quads from a **Sprite Atlas** (Create > 2D > Sprite Atlas). Keep the same Z rule (diagonal plus a small bias) and depth-tested opaque rendering with alpha clip (`clip(col.a - 0.5)` in the shader) so sorting stays automatic.

**Pixel-perfect camera:** the discrete zoom levels already keep 1 texel = N screen pixels at integer zooms. Snap the camera position to the pixel grid (`round(pos * 32 * zoom) / (32 * zoom)`) in `IsoCameraController` to remove shimmer while panning.

## 13. The interface

The interface is modelled on OpenTTD and drawn entirely by code (`View/UI`): no prefabs, no fonts or sprites to import, no Canvas.

* **Toolbar** (top): pause / play / fast forward, save, load, new game, settings, the construction tools (Inspect, Build Lift, Paint Run, Buy Land, Terraform, Clear Trees), and info windows (resort, finances, lifts, runs, weather, messages). Buttons with options open a small window right under themselves: lift types, terraform mode and radius, brush size, speeds, save slots. Drag that window by its title to keep it somewhere else.
* **Status bar** (bottom): date and time, the latest message (click to jump to where it happened; right-click for the message log), and money (click for finances).
* **Windows**: click a lift or run to open its window; click snow for the tile window, which is reused for every tile you click. Every window can be dragged by its title, rolled up (▲), pinned (pinned windows survive `Delete`), and closed (×). New windows fill free space from the top left.
* **Map modes** (bottom right): overlay buttons, a legend, and a minimap with the camera's view drawn on it.
* **Numbers** are always set with ▼ / ▲ counters (hold to repeat).

How it works:

* `GameHud` runs before the tools each frame (`[DefaultExecutionOrder(-50)]`), decides which window or bar is under the mouse, and sets `ViewState.PointerOverUi` so clicks on the interface never reach the map.
* `Toolkit/` is a small immediate-mode widget kit: `UiRenderer` collects textured quads during `Update` and draws them in `OnGUI` with GL from one atlas (`UiAtlas`): a pixel font and 16x16 icons generated from `PixelArtData.cs`, using the `IceCoast/UI` shader in `Assets/_Project/Resources`. `WindowManager` owns z-order, dragging, resizing, shading and pinning.
* Interface size is a whole-pixel scale (Settings > Interface size; *Auto* picks 2x at 1080p) so the pixel font stays crisp.
* To add a window: subclass `UiWindow`, draw into `DrawContent(UiContext ui)` with `ui.Field`, `ui.Button`, `ui.Counter`, `ui.Checkbox`, `ui.TextField`, `ui.ListRow`..., and open it with `GameUi.OpenSingleton` (or a key per object, like `LiftWindow.KeyFor`).
* Tool options go in `ToolBase.OnOptionsUi`; set `HasOptions` and the toolbar gives the tool a dropdown.
* To change the font or icons, edit `PixelArtData.cs` (each glyph and icon is a little ASCII picture).

## 14. Making a build

1. **File > Build Profiles** (Unity 6), select *Windows* (or your platform). Make sure `Game` is in the scene list.
2. **Player Settings**: Company Name and Product Name (these decide the save folder), default resolution, *Resizable Window* on.
3. Scripting backend: **Mono** for fast iteration builds, **IL2CPP** for release.
4. **Build**. If geometry renders pink, the shader was stripped: check that the GameBootstrap's *Unlit Shader* field is assigned, or add the shader to *Project Settings > Graphics > Always Included Shaders*.

## 15. Version control

Recommended settings (Unity 6 defaults, but check them): **Project Settings > Editor > Asset Serialization = Force Text** and **Version Control Mode = Visible Meta Files**.

* **Commit every `.meta` file.** They hold asset GUIDs; losing one breaks references, such as the shader on the bootstrap.
* The included `.gitignore` excludes `Library/`, `Temp/`, `Logs/`, `UserSettings/` and generated IDE files.
* The included `.gitattributes` routes PNG, PSD, Aseprite, audio and fonts through **Git LFS** (`git lfs install` once per machine), and marks scenes and prefabs for Unity's YAML merge tool.

## 16. Troubleshooting

| Symptom | Cause and fix |
| --- | --- |
| Everything is pink/magenta | Shader missing or not compiled. Check the Console for shader errors; assign `IceCoast/UnlitVertexColor` on GameBootstrap. |
| Blue screen, no terrain | The bootstrap isn't in the scene, or there are compile errors. Check the Console. Also confirm the camera Z is negative (e.g. -100) with no rotation. |
| `InvalidOperationException: You are trying to read Input using the UnityEngine.Input class` | Something called the old Input API while only the new system is active. The project's code doesn't; check any script you added and use `GameInput`. |
| Warning: `Assembly ... has reference to non-existent assembly 'Unity.InputSystem'` | The Input System package isn't installed. Harmless (the code falls back to legacy input), or install it from Window > Package Manager. |
| Colours look washed out | Shader edit lost the linear conversion, or a custom shader without it. See section 7. |
| Tests don't appear in Test Runner | Install *Test Framework* from Package Manager; tests are in the **EditMode** tab, not PlayMode. |
| Clicking selects the wrong tile on steep slopes | Make sure any change to `IsoProjection.SplitAlongMainDiagonal` or the mesh builder is mirrored in `IsoPicker.TileContains`. |
| Guests never arrive | They need at least one lift, and arrivals happen 8:00 to 13:00. Check "Expected arrivals" in the Resort window (house button). |
| Guests ignore my upper lift | Its bottom station must be reachable: beside a run (Lift Exit Radius, 6 tiles = about 200 ft), near another lift's top (Lift Walk Radius, 12 tiles), or in the base area. Inspect the runs: "Connects to" lists what guests can reach. Also check it has a run off its top. |
| Guests take the lift but walk off unhappy | The run doesn't connect. Inspect it: the issues list tells you whether the top or bottom is the problem. |
| "Too steep for a Rope Tow" | Surface lifts have a maximum grade (Lifts section of the config). Pick a gentler line or unlock a T-bar. |
| Changing a default in `SimConfig.cs` does nothing | The asset already stores its own value. Reset the field in the Inspector or recreate the asset. |
