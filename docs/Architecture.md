# Ice Coast Architecture

This document explains how the code is organised and why, so that it can grow from a bunny hill to a world-class resort without turning into a tangle.

## 1. Two layers, one direction

```
┌─────────────────────────────────────────────┐
│  View layer (IceCoast.View, uses Unity)      │
│  Renderers  Tools  HUD  Camera  Input        │
│        │ reads state      │ sends commands   │
└────────┼──────────────────┼──────────────────┘
         ▼                  ▼
┌─────────────────────────────────────────────┐
│  Simulation layer (IceCoast.Simulation)      │
│  WorldState + Systems + Commands + Events    │
│  No UnityEngine. No rendering. No input.     │
└─────────────────────────────────────────────┘
```

* **Simulation** owns the truth. It is plain C# compiled with `noEngineReferences: true` in its assembly definition, so Unity physically prevents `using UnityEngine;` from creeping in.
* **View** reads simulation state and draws it. It never writes to simulation state directly; every change is an `ISimCommand`.

Benefits you get for free:

* Tests run in milliseconds, inside or outside Unity (`tools/SimHarness`).
* Balance can be tuned by simulating weeks headlessly.
* Save/load and determinism are tractable because all state lives in one place.
* The renderer can be swapped (pixel-art sprites, a different engine) without touching game rules.

**Rule of thumb (from the GDD):** if you can describe it as plain data and rules, it belongs in the simulation layer.

## 2. Simulation layer

### State vs. systems

| Kind | Where | Example |
| --- | --- | --- |
| State (data only) | `WorldState` and the classes it holds | `Chunk`, `Lift`, `Run`, `GuestPool`, `Economy`, `WeatherState` |
| Ticking systems (rules, run every tick) | `ISimSystem` implementations | `LiftSystem`, `GuestSystem`, `SnowSystem` |
| Services (rules, run on demand) | Plain classes | `ParcelSystem`, `TerraformSystem`, `TreeSystem`, `LiftPlacement`, `WalkingAreaSystem`, `BuildingRules` |

Systems hold **no saveable state**. Anything that must survive a save lives in `WorldState`. This is why saving never needs to "ask" a system for anything. The two exceptions are cached values (e.g. `ArrivalSystem.CurrentRatePerHour`), which the save writes explicitly and documents.

### Tick order

`GameSimulation.Tick()` runs in a fixed order (GDD section 19):

1. Snapshot guest positions (for view interpolation)
2. Queued commands
3. `TimeSystem`: advances the clock and sets `ClockSignals` (minute/hour/day edges)
4. `WeatherSystem`
5. `LightingSystem`: is it dark, are the trail lights on, is there a night session
6. `ArrivalSystem`: cars arrive (each needs a parking space)
7. `ParkingSystem`: cars drive in and out; a parked car's party steps out
8. `EconomySystem`: hourly costs (lifts, light power, food buildings), end-of-day books, reputation, bankruptcy
9. `LiftSystem`: status, loading, riders
10. `GuestSystem`: state machine
11. `BuildingSystem`: food windows serve the front of their lines
12. `SnowSystem`: chunk-staggered (roads and parking stay plowed)
13. `RunSystem`: periodic snow stats

Systems that only care about minutes, hours or days check `sim.Clock` instead of counting ticks themselves.

### Time scale

* 10 ticks per real second at 1x (`SimulationRunner.ticksPerSecond`).
* 10 ticks per game minute (`SimConfig.Time.TicksPerGameMinute`), so **1 game minute = 1 real second at 1x** and a ski day (9:00 to 16:00) takes 7 real minutes.
* Like RollerCoaster Tycoon, movement is time-compressed: skiers move a few tiles per game minute so they are watchable. Lift *capacity* (people per hour) is realistic; lift and skier *speeds* are tuned for readability. Both live in `SimConfig`.
* Speeds offered: pause, 1x, 2x, 4x, 8x and 16x (`SimulationRunner.SpeedSteps`).
* The frame rate is locked at 30 fps (`GameBootstrap.targetFrameRate`, vSync off), like OpenTTD and RCT. Ticks are independent of frames and positions are interpolated between ticks.

### Lift loading

Lifts load discrete **carriers** (a rope-tow handle, a T-bar, a chair, a cabin). Each lift has a `LineClock` that advances one per tick while the haul rope turns (not on wind hold). Carrier *k* reaches the loading point when the clock passes *k* x `LiftSystem.CarrierInterval` (seats x ticks per hour / capacity), and up to `SeatsPerCarrier` guests from the front of the line sit down on it together, each in their own seat. The first seat always fills when someone is waiting; further seats fill with the type's `LoadingEfficiency`, so lines run longer than nameplate capacity (`Lift.EffectiveCapacityPerHour` drives wait estimates). The view draws carrier *k* and its riders from the same clock, so riders are always on their chair. Line speeds are slow enough to watch at 1x (a chair climbs about 3 tiles per real second; a 100-tile chair ride takes about 30 seconds) while capacities stay realistic, so carriers ride fairly close together (spacing = speed x seats x 60 / capacity: about 0.6 tile for T-bars and chairs, 0.8 for bubbles, 1.2 for gondola cabins; rope-tow riders hold the rope wherever). `Carriers_HaveClearSpaceBetweenThem` guards this. Loading a save applies the current config's speed and capacity to existing lifts (`LiftSystem.ApplyDefinition`).

### Skiers go downhill

Every carving step must reduce the skier's flow distance to the end of the run (`MinCarveProgress`), and a skier never swings round more than ~110 degrees from the way they are moving, so flat run-outs are glided across rather than zig-zagged back up. Guests never join a run (after a run, at a junction, or while traversing onto one) at a tile above where they stand, except for a short side-step from a lift's top station. Off the runs, guests keep their skis on wherever there is snow: they glide downhill to lift lines and back to the car, and only walk (skis on the shoulder) on bare ground.

### Data layout

* **Chunks** (32x32 tiles) store tile data as parallel arrays (structure of arrays): `SnowDepth[]`, `TreeDensity[]`, `Flags[]`, and so on. Index with `Chunk.TileIndex(lx, ly)`.
* **Vertex heights** are per grid corner, duplicated on chunk borders as the GDD specifies. Always write through `WorldMap.SetVertexHeight`, which updates every copy.
* **Guests** are indices into `GuestPool` (also structure of arrays). No per-guest objects means no GC pressure and trivial serialization.
* **Hot loops** resolve a `TileRef` once (`map.Locate(x, y)`) and index the chunk arrays directly.

### Change tracking (instead of a single Dirty flag)

The simulation never knows who is watching. It only bumps version counters:

| Counter | Bumped when | Consumer |
| --- | --- | --- |
| `Chunk.ShapeVersion` | heights, trees, ownership change | Terrain mesh rebuild |
| `Chunk.SurfaceVersion` | snow or run paint changes | Colour-only refresh |
| `WorldState.LiftsVersion` | lifts added, removed, opened, closed | Lift renderer |
| `WorldState.RunsVersion` | runs created, deleted, re-rated | Terrain colours, HUD |
| `WorldState.ParcelsVersion` | land bought | Ownership overlay |
| `WorldState.LightsVersion` | lights added, removed, switched on or off | Trail light renderer |
| `WorldState.WalkingVersion` | walking areas painted or erased | (tools) |
| `WorldState.ParkingVersion` | lots or the road change | (car renderer reads lots every frame) |

Derived caches in systems key off their own counters too: `LightingSystem.Version` (darkness, lights, coverage), `WalkingAreaSystem.Version` (zones and lift links), `RunSystem.ConnectionsVersion`, `LiftSystem.StatusVersion`. `RouteValues` rebuilds when any of them change.

Each consumer remembers the version it last processed. Multiple consumers can watch the same data, and the view never writes into simulation state.

### Commands

Every player action is an `ISimCommand` with two methods:

* `Validate(sim)`: pure, safe to call every frame, returns `CommandResult` (ok, error text, cost). Tools use this for live previews and price quotes.
* `Execute(sim)`: applies the change; only ever called right after a successful `Validate`.

Because the preview and the real action use the same validation, the UI can never promise something the simulation then refuses. Commands are also the natural seam for undo, replays and a debug console later.

`SimulationRunner.Submit` executes commands immediately between ticks (so building works while paused). `GameSimulation.Enqueue` is available for anything that should apply at the next tick.

### Determinism

* All randomness comes from `WorldState.Rng` (`SimRandom`, xorshift64*), which is saved.
* Render-only randomness (tree jitter) uses the stateless `SimRandom.Hash` so it never disturbs the game stream.
* Iteration is over lists in insertion order.
* `StateHasher` produces a fingerprint of the world; `SimulationTests.SaveLoad_ContinuesIdentically` proves that save, load and continue matches never saving.

Floating-point results are deterministic on the same build and machine, which is what saves need. Cross-platform lockstep (multiplayer) would need fixed-point maths; that is a non-goal.

### Guest route planning

A big mountain is a network, not a set of separate lifts, so guests plan across it:

* **Run exits** (`Run.Exits`, built by `RunSystem.RefreshConnections`): every lift bottom station a run passes within `LiftExitRadius`, and every other run it touches. They are sorted in skiing order (highest first).
* **Route values** (`Guests/RouteValues.cs`): for each skill level, a value for every lift and run that includes *where it leads*. A lift is worth the best run off its top (or a lift a short walk away); a run is worth its own skiing plus `RouteLookahead` x the best exit or ending below it. A few rounds of value iteration settle the numbers, so an expert at the base will ride a mediocre lower lift because it leads to black terrain further up, then lap the upper lift.
* **Decisions** (`GuestSystem.DecideNext` and `TryTakeExit`): at lift tops, run ends and the base, guests compare every lift in walking distance and every run they can ski onto. While skiing, each exit they reach is weighed against "keep going". Queue lengths, walking distance and uphill walking are applied live; route values are not.
* **Going home**: a separate "way down" value steers tired guests along routes that end at the base area.

### Skier lines

Every run has an across-the-run coordinate per tile (`Run.Across`, 0 = one edge, 1 = the other), built by slicing the run by flow distance and measuring each tile's sideways offset from the slice's centre, perpendicular to the local direction of travel, so it follows the trail around bends. Each skier holds their own line (`GuestPool.Line`) and makes S-turns around it, instead of everyone funnelling down the flow field's single shortest path.

Lines are picked when a skier joins a run and re-picked every few turns (`GuestSystem.ChooseLine`): the next stretch of the run is split into lanes and each lane is scored by how much this skier likes its snow (`GuestChoices.SnowAppeal`: experts hunt powder and leftover crud, beginners want corduroy, everyone avoids ice) minus how tracked out and crowded it is (tile traffic since the last snow update), plus personal taste. Tile by tile, the same snow appeal nudges which neighbour they ski onto. `SkierLineTests` checks that a wide run is used edge to edge and that an icy half is avoided.

The cache rebuilds whenever lifts, runs, ratings, snow stats or lift status change, and never depends on queue lengths, so a loaded save recomputes identical values and stays deterministic.

### Night skiing and trail lights

`Time/Daylight.cs` gives sunrise and sunset for each game day (solar declination plus the equation of time at the resort's latitude and longitude, Eastern time with daylight saving from the second Sunday in March): about 7:05 and 4:15 PM on Dec 1. `TimeSystem.IsDark` is sunset plus `TwilightMinutes`. The view's sky reads the same numbers, so it looks dark exactly when guests treat it as dark.

Trail lights (`Lighting/TrailLight`, sizes in `SimConfig.Lighting.Lights`) occupy a tile (`OccupantKind.Light`) beside a trail and add light to the tiles around them (`Chunk.LightLevel`, fading with distance, overlapping lights add up). A tile at `LitThreshold` is lit; a trail with `RunLitFraction` of its tiles lit is a lit trail (`Run.LitFraction`). Light levels are derived and rebuilt after a load.

Operating hours have two sessions. The day session runs from `LiftsOpenMinute` to `LiftsCloseMinute`. With night skiing on (`WorldState.NightSkiing`) and at least one lift with a lit, open trail off its top, a night session follows until `NightCloseMinute`. `LiftSystem.ComputeStatus` only runs a lift in the night session (or whenever it is dark) if it serves lit terrain, so guests never ride up into the dark. The lights switch on shortly before sunset and cost power hourly while on (Utilities).

After dark, guests treat unlit trails as dark (`LightingSystem.IsRunDark`): route values and live scores take `DarkRunPenalty` off them and count them half as a way home, so a dark trail is only skied when nothing lit will do. An evening crowd arrives for the night session (`NightDemandFactor`, scaled by the lit share of open terrain).

### Walking areas

`Chunk.Surface` holds a `SurfaceType` per tile: natural, walking area, road or parking. Trails never overlap walking areas, roads or parking. Walking areas are painted and erased with `PaintWalkingAreaCommand` (one brush stroke per command). `WalkingAreaSystem` flood-fills walking areas plus parking into zones (derived, rebuilt on change and after a load) and links lift stations within `LiftLinkRadius` to them. Guests standing on a zone can walk to every lift that opens onto it however far across it, a trail ending on a zone leads to those lifts (`Run.EndZoneId`), and a lift whose top opens onto a zone connects to them (`RouteValues.BuildTransfers`). A zone containing the base village or the lot counts as the base. On walking areas guests take their skis off and walk.

`Buildings/BuildingRules.CanPlaceFootprint` is the placement rule for buildings: every footprint tile must be an owned, unoccupied, fairly flat walking area tile. Walking area under a building can't be erased.

### Waffle Cabins (food)

`Buildings/Building` + `BuildingSystem` (stats in `SimConfig.Buildings`, hunger in `SimConfig.Food`). A cabin is a one-tile building (`OccupantKind.Building`) on a walking area. It costs `BuildCost` to put up, `OperatingCostPerHour` while open (from an hour before the lifts open until the last lift closes) and `IngredientCostPerSale` per waffle, all booked under `LedgerCategory.FoodService`; sales go to `Food`. The player sets each cabin's price (`SetBuildingPriceCommand`) and can close it.

Guests get hungrier through the day (`GuestPool.Hunger`, 0..100). Above `HungryThreshold`, whenever they choose what to do next (`DecideNext`, or peeling off a run toward a lift), they look for an open cabin in reach: within `FoodWalkRadius`, on the walking area they're standing on, or anywhere in the base village when they're at the base, with a line under `MaxWaitMinutes`. They buy only if the price is at most what they personally will pay (`BuildingSystem.PersonalMaxPrice`: everyone pays the fair price, nobody pays `MaxPrice`, hashed per guest so the answer is stable); otherwise the cabin counts them in `PricedOutToday`. They walk to the back of the line (`WalkingToFood`), wait (`WaitingForFood`; the window serves one every `ServiceMinutes`), pay, and eat nearby (`Eating`): hunger resets, energy comes back, and happiness rises by `HappinessFor(price)` (the boost at the fair price, up to 1.5x when cheap, down to 0.5x when dear). `WaffleCabinTests` cover placement, sales, price sensitivity and save/load.

### Road, parking and cars

`Terrain/BaseLayout` places the starting base from the resort entrance: a half-disc walking plaza on the valley side, a parking lot beside it (stall rows either side of an aisle, four spaces per stall tile, 150 spaces by default) and an access road leaving along the aisle row, off the map edge and on through the scenery to the very edge of the world, so it never dead-ends in view. Cars drive in from there. The terrain generator flattens the lot and grades the road (no steeper than 10%), and the parking system builds the lot's spaces from the same layout.

Guests come by car in parties of one to four (`ArrivalSystem`). `ParkingSystem.TryAdmitCar` takes the free space nearest the lodge, or turns the car away if every lot is full (counted in the ledger; costs a little reputation). A car drives in along `WorldState.RoadPath` and the aisle (`ParkingSpace.Timer` / `DriveTicks`); when it parks, its party steps out (`GuestPool.Car` records whose car is whose). Guests going home walk back to their car; when the last of the party has gone, the car drives out and frees the space. `ParkingSystem.TryGetCarPose` gives the view each car's position and heading from the same timer.

### Early birds and opening time

From `EarlyArrivalsStartMinute` (6 AM) until the morning rush, a car occasionally pulls in: one dice roll per game minute (`EarlyBirdCarsPerHour`, ramping toward the rush), so they come in ones and twos rather than a steady stream. Early birds stroll around the base (`GuestState.Resting`: walk somewhere on the plaza, stop a few minutes, repeat). From `LineUpBeforeOpeningMinutes` before opening (8:30), lifts that will open are `LiftSystem.Boardable`, so guests walk to them and queue for first chair; their wait only starts counting once the lift runs. Guests arriving before opening never go home for lack of open lifts, and their planned stay counts from opening.

### Editing trails

`EditRunTilesCommand` adds tiles to and/or erases tiles from an existing run, keeping its id, name, sign and settings. It is refused if the run would be cut in two or drop below `MinRunTiles`. Skiers heading for an erased tile re-join the run below them (`GuestSystem.OnRunReshaped`).

### Terrain and scenery

`Terrain/MountainShape.cs` builds an old, worn New England mountain in the spirit of Killington. A broad **apron** carries the mountain to the valley. **Peaks** and **ridgelines** are defined by their *prominence above the apron*, which keeps flanks at believable East Coast grades. Broad **drainages** are cut in, and **rolling** benches added. The layout continues beyond the map with neighbouring mountains.

Heights are generated over the map *plus* `SceneryPadding` tiles on every side in one pass, so the edge is seamless. The outer ring becomes `World/Scenery`: visual only, never simulated or saved, and regenerated from `WorldState.MapSeed` when a save loads. Along the seam, scenery reads live map heights, so terraforming at the edge still lines up.

### Units

The simulation computes in SI (metres, centimetres of snow, km/h, °C) because its constants are physical. Everything shown to the player goes through `GameSimulation.Units` (`Core/Units.cs`). It is imperial with °F by default (`SimConfig.Units`), and Settings can toggle it to metric. Never format a measurement with a hard-coded unit; call `Units.Length`, `Elevation`, `SnowDepth`, `Temperature` or `Speed`.

## 3. View layer

### Composition root

`GameBootstrap` is the only component placed in a scene. In `Awake` it creates every runtime object as a child and passes dependencies in through `Initialize(...)` methods. There are no singletons and no `FindObjectOfType` at runtime, so dependencies are visible in one file.

```
Game (GameBootstrap)
├── Simulation      SimulationRunner     fixed timestep, speed, save/load, events
├── Terrain         TerrainRenderer      256 chunk objects, each with Terrain + Trees meshes
├── Scenery         SceneryRenderer      the range beyond the map edge, in 32x32 blocks
├── Day Light       DayLightController   time-of-day sun and sky via shader globals
├── Contours        ContourRenderer      contour line meshes (toggle with C)
├── Lifts           LiftRenderer         static structures mesh + animated chairs mesh
├── Trail Lights    TrailLightRenderer   light poles, rebuilt when lights change or switch on
├── Buildings       BuildingRenderer     Waffle Cabins, rebuilt when buildings change
├── Cars            CarRenderer          parked and moving cars, every frame
├── Guests          GuestRenderer        one dynamic mesh for every guest
├── Tool Preview    PreviewRenderer      always-on-top highlights for the active tool
├── Tools           ToolController       picking, hotkeys, active tool
├── HUD             GameHud              OpenTTD-style toolbar, windows, status bar; map modes + minimap
└── Art Hot Reload  ArtHotReload         reloads edited PNGs from StreamingAssets/Art while playing
Main Camera         IsoCameraController
```

### Isometric rendering approach

* `IsoProjection` is the only code that knows the grid-to-world mapping (tile = 64x32 px, 1 unit = 32 px, 4 px per metre of height).
* Every vertex gets a **Z from its grid diagonal** (`x + y`). With depth testing on, nearer terrain hides farther terrain automatically. For a heightfield seen from this fixed angle this ordering is exact, so there is no sort-key maintenance and no sorting bugs with towers, slopes or chairs (GDD risk #3).
* Terrain is a mesh per chunk built straight from corner heights, so any slope shape works without hand-drawn slope tiles (GDD risk #4). Snow, ice and overlays are vertex colours.
* All geometry uses one vertex-colour shader that also samples the **world atlas** (`Rendering/Art/WorldAtlas.cs`), packed at startup from the art library. Final colour = vertex colour x texel x 2, so the atlas's neutral texel (0.5) leaves plain shapes unchanged.
* **Editable art** (`ArtSheets`, `ArtLibrary`, `ArtHotReload`; guide in `docs/EditingArt.md`): every sprite sheet and ground texture is a PNG in `Assets/StreamingAssets/Art` (sheets have a JSON beside them with each sprite's rectangle and pivot). The code below draws the defaults; a PNG replaces them and a missing one falls back. The UI atlas (icons, font) loads the same way. Files are watched while playing and redrawn in place. `tools/SimHarness/ArtExport` writes the defaults without Unity and round-trip checks the loader.
  * **Ground textures** (`TerrainDetailArt`): powder with wind ripples and sparkle, corduroy laid down the fall line, tracked-out packed snow, lumpy crud, glassy ice, slush, rock poking through snow, and bare grass/dirt/rock/scree/water. Each tiles every 4x4 map tiles at 32 texels per tile; overlays switch them off.
  * **Sprites** (`SpriteArt`): spruce, fir and leafless hardwoods, singly or as stands of three (snow-laden when the ground under them has snow, bare when it doesn't; swapped by UV only when snow comes or goes), skiers in a few poses with a two-frame walk, and a carrier for each lift type. Each tree slot is one cut-out triangle with no normals, spread over the tile's quarters, so a thick forest stays cheap. The scenery ring is forested just as densely all the way to its edge.
  * **Lift structures** (`LiftArt`): pixel-art isometric huts and halls, elevated terminals for detachables and gondolas, towers (stretched to meet the cable), bullwheels and sheaves, per lift type. The up and down lines sit `LiftArt.LineOffset` either side of the centre line so passing carriers never overlap.
  * **Props** (`PropArt`): cars (six paint jobs, nose-toward-camera and away, mirrored for grid y) and trail light poles with separate lamp-head sprites.
  * **Paved and walking surfaces**: asphalt road with a dashed centre line (along x or y), parking stalls and aisle, and trampled walkway snow. Paved tiles use a light "paint" vertex colour and the texture darkens everything but the painted lines.
* **Trail-light glow**: on lit geometry the vertex colour's alpha carries lamp light (255 = none, 0 = full). The shader never lets lit pixels go darker than `_IC_LampColor` x lamp, so at night the snow under lights glows warm, lamp heads shine and guests and cars under them are lit. Terrain writes lamp light per corner (`ChunkMeshBuilder.SetLampAlpha`) so the glow fades smoothly.
* **Map modes**: Natural (trails tinted by sign), Natural without trail markings (`OverlayMode.Scenic`), Lighting (coverage: lit trail tiles yellow, dark ones red), plus the data overlays.
* The camera's screen centre is clamped to the map's on-screen outline (owned or not), and zoom-out stops before the 160-tile scenery ring stops filling the screen, so the edge of the world is never visible.

### Tools

A tool (`ToolBase`) gets a `ToolContext` each frame (hovered tile and vertex, preview renderer, runner) and:

1. Builds a command from the mouse state.
2. Calls `runner.Preview(command)` and draws the result.
3. Calls `runner.Submit(command)` on click.

To add a tool, subclass `ToolBase` and add it to `ToolController.CreateTools()`.

Area editors share `TileBrush`: a radius, a Paint / Erase mode (Shift flips it while held) and strokes collected from mouse down to mouse up, so a whole drag is validated live and applied as one command. The trail editor (`PaintRunTool`) paints new trails (Enter creates), adds strokes to a picked existing trail, and erases strokes from any trail; the walking-area editor (`WalkingAreaTool`) works the same way. Tools: 1 Inspect, 2 Build Lift, 3 Trail Editor, 4 Buy Land, 5 Terraform, 6 Clear Trees, 7 Walking Areas, 8 Trail Lights, 9 Waffle Cabins.

## 4. How to add a new system (worked example: grooming, M4)

1. **State**: add `List<Groomer> Groomers` to `WorldState`, and a `Groomer` class in `Simulation/Grooming/`.
2. **Config**: add a `GroomingSettings` section to `SimConfig` (fuel cost, work rate, winch requirement).
3. **System**: `GroomingSystem : ISimSystem` that moves groomers along their plan overnight and calls the existing `SnowSystem.Groom(x, y)`.
4. **Register**: construct it in `GameSimulation` and insert it into `_tickOrder` at "vehicles and staff" (after guests, before snow).
5. **Commands**: `BuyGroomerCommand`, `SetGroomPlanCommand` with validation.
6. **Save**: write and read the new state in `SaveSystem`, bump `CurrentVersion`, and guard reads with `if (version >= 2)`.
7. **Tests**: a test that a groomed run's tiles become `Groomed` overnight, and extend the save round-trip test.
8. **View**: a `GroomerRenderer` (dynamic mesh like `GuestRenderer`), a "Grooming" overlay in `TerrainColorizer`, a `UiWindow` subclass in `UI/Windows`.

The same recipe works for snowmaking, buildings, staff and power/water.

## 5. Conventions

* Namespaces mirror folders: `IceCoast.Simulation.Lifts`, `IceCoast.View.Rendering`.
* Simulation types never use `float` time or `UnityEngine.Random`; use ticks and `SimRandom`.
* Grid coordinates are `Int2` (tiles) or `(float gx, float gy)` (continuous, tile units). Heights are integer steps (1 step = 1 m by default).
* Balance numbers belong in `SimConfig`, not in system code.
* Public methods that mutate state live on systems and are called by commands. Views call `Preview`/`Submit`, never system mutators.
* One class per file, file named after the class (small related enums may share a file).

## 6. Milestone map

| Milestone | Status | Where it lives / will live |
| --- | --- | --- |
| M1 Prototype | Done | Everything above |
| M2 Core loop | Done (buildings: Waffle Cabins) | `Land/`, `Runs/`, `Finance/`, `UI/`, `Walking/`, `Parking/` |
| M3 Snow | v0 done (wear, melt, refreeze) | `Snow/SnowSystem.cs`; add wind drift next |
| M4 Operations | Hooks only, plus trail lights / night skiing | `SnowSystem.Groom`, `OccupantKind.Building`, `GuestState.Eating/Warming`, `Lighting/` |
| M5 Terrain and trees | Done (tools), effects partial | `Terrain/`, `Trees/`; tree shelter feeds wind drift in M3/M6 |
| M6 Weather and snowmaking | Weather v0 done | `Weather/`; snowmaking needs power/water systems |
| M7 Balance and polish | Tools ready, UI done | `tools/SimHarness/Balance`, art pass |
