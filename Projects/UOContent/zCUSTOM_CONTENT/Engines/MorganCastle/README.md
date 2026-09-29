# Morgan's castle

The castle NPC spawner definitions are in `Distribution/zodiac.json`, named
`Morgan Castle: ...`. They replace the old spawners inside Felucca's inclusive
rectangle (5424,910) through (5521,1005).

`MorganCastleResident` is the persistent NPC type required by those entries and
the world save. Its constructor parameter selects king, jester, cook, scholar,
swordsman, sentry, archer, library-assistant, kitchen-assistant, or prisoner. Morgan keeps his name on each respawn. Residents hold
their posts, defend themselves, and respawn after 5–10 minutes. The cook and
scholar are role NPCs, not shop vendors.

Use the standard spawner import/export commands for future changes. There is no
castle startup installer or second placement list. Normal restarts load spawners
from the world save; JSON edits take effect when explicitly imported.

The upstairs library was identified from bookcases; the southeast raised chair
area and southwest dining/service area were chosen for the court and cook.
The pre-change world and JSON backups are in
`Distribution/Backups/MorganCastle-before-20260927`.

The expanded population includes eight `MorganThroneGuard` NPCs at X=5509/5511,
Y=930/934/938/942, Z=22. West-side guards face East and east-side guards face West.
They cannot walk, remain frozen, and restore their facing on each AI tick.
Four library assistants, three kitchen assistants, and ten prisoners are also
defined in zodiac.json. Dungeon openings use 27 standalone locked `IronGate` world items at Z=0;
18 broken-gate static art pieces were removed from the shared statics0.mul/staidx0.mul.
The map and save backup for this expansion is `Distribution/Backups/MorganCastle-expansion-20260927`.

Dungeon gate adjustments: new gate (5510,946) and prisoner (5510,949);
the Y=936 pair moved east to X=5472/5473; the X=5453/5454/5455 row moved
north to Y=946. The Y=922 entrance retains only X=5469/5470 gates, with
stone wall tile 132 at X=5468/5471. The wall at (5453,946) was removed to
clear the relocated gate. All these changes are at Z=0. Pre-change map,
spawn definitions, and saves are backed up in
`Distribution/Backups/MorganCastle-gate-adjustments-20260927`.

## Persistent doors

Doors are ordinary server items saved in `Distribution/Saves/Items`, not spawned
objects. The 27 gate spawners were detached and removed while preserving the
existing gates and their serials, positions, lock/key settings, and links.
NPC spawners (including prisoners) remain in zodiac.json.

`Distribution/Data/MorganCastle/doors.json` is the recovery blueprint for the
27 gates on Felucca. `[RebuildMorganDoors` (Developer access) explicitly creates
only missing gates; it recognizes open doors by their closed position, leaves
existing doors unchanged, and refuses obstructed placements. Save the world
after using it. It never runs automatically. It does not infer double-door
links; use `[Link` explicitly if desired. Update the blueprint when intentionally
relocating gates so recovery does not recreate an old placement.

Migration backup: `Distribution/Backups/MorganCastle-standalone-doors-20260927`.
No MUL changes were needed for this conversion.
