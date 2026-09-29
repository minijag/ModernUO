# Arbor magical hedge maze

## Current version: bridge abutments, river outlet, encounter sites and POI gates (V5)

Installed and saved 2026-09-28 at 14:40:55 local. The main bridge approach remains **Felucca (1806,965,0)**. Towers and their working staircases are unchanged.

- Built out rounded, flat land at both bridge ends, including the troublesome northwest/southeast corners. Reworked the adjacent banks so the river passes around the expanded land and beneath the bridge.
- Cut the southern river mouth through to open sea, roughly through (1841,990), (1847,996) and (1855,1003). A terrain-water flood from beneath the bridge reached both the north ocean and (1860,1005); the southern outlet is no longer a closed tip.
- Added **16 independent encounter spawners**, spaced at least eight Manhattan tiles from existing/new encounter centers. Existing 27 spawners and their stack sizes were not changed. Total: **43 spawners** (38 maze, five fort), configured population **85**. Each new spawner has its own GUID, timer and small home range.
- Replaced all nine earlier corridor/shortcut gates with **six gates at actual points of interest**, paired exactly:
  - Violet: (1836,916,0) <-> (1908,916,0).
  - Amber: (1852,940,0) <-> (1834,970,0).
  - Jade: (1872,960,0) <-> (1886,930,0).
- Created a new archive clearing at (1834,970) and a magic clearing at (1886,930), replacing the two old POIs lost during fort revisions. Updated the three lore books for the new network.
- Gate destinations are the partner gate's exact coordinates, without offsets. Standard Teleporter.MoveToWorld arrival does not invoke destination OnMoveOver: step off the gate and step back on to return. Actual activation and reciprocal travel were tested on every gate. No global teleporter behavior was modified.
- Removed the extra cave-shortcut gate. The original three cave dungeon entrance teleporters at Y908 remain untouched; the cave is reached through the northern maze trail.

`Finish.cs` and `FinishData.ps1` are the incremental staging tools; source of truth remains `Distribution/zodiac.json` and `Distribution/Data/teleporters.json`, with migration manifest under `Data/ArborMazeV5`. Changes were limited to 983 map coordinates, with outside terrain/statics verified unchanged. Backup: `Distribution/Backups/ArborMazeV5-20260928`, including pre-change maps, configuration, assembly and Saves. Do not rerun staging or restore Saves blindly against later progress.

`ArborPolishDeployment.cs` is explicitly gated; the request was retired after save verification. Build passed with zero warnings/errors. Actual movement reached 9,535 positions, including all 43 spawns, six POIs and bridge corner test points. Every exact gate arrival and reverse trip passed without automatic bounce. These are server movement/transport tests, not final client visual approval.

## Historical winding moat, real bridge and twin towers (V4)

Installed and saved 2026-09-28 at 14:19:47 local. Main approach is **Felucca (1806,965,0)**; the wooden bridge leads north into the maze. Fort west and south entrances remain.

- Copied 122 structural components from the native bridge around (584,1412): planks, beams, posts and wooden rails. The copied deck is at Z1, with ground approaches at Z0. The reference bridge itself is unchanged.
- The moat now meanders toward the mainland instead of following one straight diagonal. Shoreline selection uses 5x5 water neighborhoods sampled from the requested native river: 347 patterns, with nearest matching patterns where an exact match was unavailable. Existing peninsula dirt shores and maze boundaries are preserved.
- Replaced the broken converted fort with two intentionally built stone towers. The northwest tower has a Z20 terrace; the southeast tower rises to Z40. A five-tile-wide elevated connector includes three unobstructed walking tiles between parapets.
- A three-wide stair flight climbs north from (1901,949,0) to the short tower's terrace. Another climbs inside the tall tower from its Z20 floor to Z40. Both have four risers, supported bases, landings and overhead clearance. The two deck teleporters were removed; the towers are accessible by walking.
- Maze capacity increased from 38 to 60 creatures. Fort capacity is nine creatures across five spawners. Total configured population: **69**, across 27 spawners. Existing 26 GUIDs were preserved; one watchtower spawner was added. The eight internal maze mirrors and cave shortcut remain unchanged.

`Fortress.cs` / `FortressData.ps1` stage the revision. The staged map was compared against its input and verified unchanged outside 1,622 scoped tiles. The pre-V4 inputs and world save are in `Distribution/Backups/ArborMazeV4-20260928`. Do not rerun staging or restore Saves blindly against a later world.

`ArborFortressDeployment.cs` is an explicitly gated one-time deployment, with the request retired after save verification. The live migration protected players/pets and cleared only verified NPC-owned summons during respawn. It removed two old deck portal pairs, preserved unrelated objects, updated the spawners and saved normally.

Build: zero warnings/errors. Verification used the **actual server movement implementation** with a temporary, non-world probe (deleted before saving), rather than fixed-Z occupancy alone. It reached 8,522 positions, every spawn point, both stair flights and all 191 standable elevated floor tiles. Runtime log: `Backups/ArborMazeV4-20260928/install.stdout.log`. This is movement validation, not a claim of final visual approval or party combat playtesting.

## Historical river banks and relocated fort (V3)

Installed and saved 2026-09-28 at 14:01:13 local. The main maze bridge entrance remains **Felucca (1806,963,0)**.

- Replaced the raised rock coast with the pre-build continent's original dirt/grass shoreline and water overlays.
- Rebuilt the diagonal moat banks using the native river near (662,947) as the tile reference: dirt land 0xA2/0x91 and 0xA1/0x8D, recessed 0x66 riverbed, and directional water overlays 0x1797, 0x17A9, 0x17B2, 0x17AB, 0x17B0. The stone bridge is retained above the bank transitions.
- Translated the existing stone fort by +20 X, +18 Y, centered near (1910,947). Its surrounding hedge has two physical openings: **west (1896,950,0)** and **south (1910,960,0)**. Neither approach uses the cave entrance. The old fort site is now maze corridors.
- Moved the four boss spawners, preserving GUIDs and population. Moved one displaced maze spawner to (1890,930,0). All other spawners retain identity/configuration.
- Removed the old fort entry/return shortcuts; moved its two deck portals with the building. Eight maze mirrors, the cave shortcut, and two deck portals remain (11 total). Books and original cave teleporters are unchanged.
- Reconnected the eastern maze branch around the moved fort, without adding a third fort opening.

`Refine.cs` and `RefineData.ps1` are the incremental staging tools. The output was verified against the pre-V3 map: 3,499 coordinates actually changed; terrain/statics elsewhere were unchanged. Do not rerun against the installed world as though it were the V2 input. The original V3 input is in `Distribution/Backups/ArborMazeV3-20260928`; the older pre-V1 reference supplies only native coastline data.

`ArborRefinementDeployment.cs` is an explicitly gated migration; its request marker was retired after save verification. It keeps player pets and summons protected, but permits only verified summons of the NPC bosses being moved to be cleared during their respawn. It does nothing on normal startup. The V3 backup contains map files, configuration, assembly and Saves; restoring Saves would discard subsequent progress.

Build passed with zero warnings/errors. Server placement checks passed for all 26 maze/fort spawners and 11 portals. A cardinal Z0 occupancy flood reached 8,131 tiles, including all ground-level spawn positions and the cave approach; each fort opening reached the courtyard independently while the other was blocked. These are map collision/connectivity checks, not an interactive client walk-through or visual approval of the river rendering.

## Historical peninsula expansion (V2)

Installed and saved 2026-09-28 at 13:34:28 local. **Current entrance: Felucca (1806,963,0)**, at the southwest end of the moat bridge. The V1 coordinates below are historical.

- Maze now follows the natural peninsula outline: 488 connected cells and 6,021 path tiles (versus roughly 3,711 originally), with three-wide corridors. The old rectangular corner extending into the sea has been restored to the original coastline.
- Rocky coastal rim: land rock tiles with an elevated Z8 inner rim and a Z-5 seaward toe. A diagonal water moat crosses the neck, with one three-tile stone bridge. Maze floors stay at Z0.
- Northern trail reaches the existing cavern approach. The three original cave entrance teleporters at Y908 are unchanged and still lead to (5248,784,0).
- Eight internal mirror teleporters (four pairs), two exit shortcuts, a fort return portal, and two fort-deck portals: 13 total. Mirror arrivals remain offset from triggers.
- 22 maze spawners support 38 creatures. Four fort spawners support seven creatures: four Daemons, two Lich Lords and an Ancient Lich. The fort replaces the separate V1 stone tower, which was removed.
- The existing orc fort footprint and elevations are retained. Timber walls, posts, floors, parapets, ramps, ladders and roofing were converted to stone/slate; orc clutter was removed. Portals connect its ground level and Z19 fighting deck.
- Three books and six themed clearings remain, relocated for the expanded layout.

`Expand.cs` stages the map; `ExpandData.ps1` creates the matching configuration and manifest. Never rerun these blindly against an already installed world. The map generator uses the pre-V1 backup ONLY for coastline/reference restoration; unchanged tiles outside the explicit V2 scope are preserved from its live input. A full outside-scope comparison and path connectivity/bridge-crossing assertions run before output is accepted.

V2 source of truth: `Distribution/zodiac.json`, `Distribution/Data/teleporters.json`, and `Distribution/Data/ArborMazeV2/manifest.json`. Ordinary world saves persist the live objects. `ArborExpansionDeployment.cs` is gated by explicit request files, which have been retired. An installed marker also prevents accidental repeated application. Do not use global spawn/teleporter regeneration as an upgrade procedure.

Pre-V2 backup: `Distribution/Backups/ArborMazeV2-20260928` (map files, configurations, assembly and Saves). Deployment removed 23 replaced spawners, 32 wild NPCs and the identified old maze objects. Audit found no players, protected creatures, houses or unknown items. The backup contains earlier player state: restoring Saves would undo later progress.

Build passed with zero warnings/errors; all configured spawn, portal and book positions passed server CanFit checks. In-game end-to-end movement and party combat remain untested. Reopen ClassicUO to reload the shared map files.

## Historical V1 installation

Installed on Felucca, 2026-09-28. South entrance approach: **1798,981,0**.
Maze footprint: X1792–1868, Y916–980. Tower: X1880–1900, Y942–962.
Shared client/server map directory: `C:\workspace\Ultima_online\Ultima Online Classic 7.0.15.1 relpor`.

The maze has three-tile-wide corridors, six clearings, magical decorations, two sand gardens, and three readable lore books. There is one physical south entrance and two portal exits. Six internal portals form three pairs; arrivals are offset from triggers to avoid immediate bouncing.

## Exits and tower

- Cave exit: (1866,922,0) to (1873,914,0), on level ground south of the existing three Labrynth cave teleporters at Y908. Those cave teleporters lead to (5248,784,0).
- Tower exit: (1866,974,0) to (1883,958,0).
- Tower ascent: (1897,945,0) to (1883,958,20).
- Tower descent: (1883,959,20) to (1896,945,0).
- Tower return: (1883,959,0) to (1867,974,0).

There are 14 spawners: ten maze spawners and four tower spawners. Tower population is an Ancient Lich, two Lich Lords, and three Daemons across two floors, intended for a small endgame party. Actual player balance and combat have not been playtested.

## Persistence and deployment

Spawner definitions are in `Distribution/zodiac.json`. Teleporter definitions are in `Distribution/Data/teleporters.json`. Ordinary spawners, teleporters, markers and books persist in world saves. No recurring rebuild is required.

`ArborMazeDeployment.cs` is an opt-in startup migration using `Data/ArborMaze/manifest.json`. Audit and install request markers have been retired after installation and a confirmed save at 13:09:05 local. **Do not restore install.pending on an already installed world: this migration is not an idempotent repair tool.**

`Build.cs` stages map changes; `BuildData.ps1` stages configuration using the pre-install world as input. **Do not rerun BuildData.ps1 against installed configuration:** it generates new GUIDs and would treat current maze spawners as old entries. The temporary staging directory is not a recovery source; use deployed definitions or backups.

Backup directory: `Distribution/Backups/ArborMaze-20260928`. It contains pre-install map0.mul, staidx0.mul, statics0.mul, configuration, assembly and Saves, plus audit/install logs. Restoring Saves would roll back later player progress, so do not restore blindly.

Installation removed eight old spawners and sixteen wild NPCs within the two footprints. Audit found no players, pets, houses or unknown world items. Map validation confirmed unchanged land/statics outside the footprints and a connected maze with exactly one three-tile boundary opening. All portal endpoints and spawn positions passed server placement checks. In-game walking and combat remain to be tested.
