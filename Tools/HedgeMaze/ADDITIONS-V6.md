# Arbor additions, September 28, 2026

These are ordinary persistent world items, not MUL edits. The V5 map is unchanged.

- Three single-tile hedge walls: Felucca (1876,927,0), (1844,914,0), (1818,932,0).
- Whispering Shrine at (1795,930,0): crystal balls and lights framing the path; one ElderGazer encounter.
- Eastern Fort Patrol at (1921,937,0): one Daemon encounter.
- Return teleporter: (5249,784,0) -> (1873,914,0), Felucca. Landing is clear of the outbound cave triggers.

Spawner definitions remain in Distribution/zodiac.json; the return route is also in Distribution/Data/teleporters.json. AdditionsData.ps1 stages these definitions, refusing duplicates.

ArborAdditionsDeployment is a one-time opt-in installer. It checks occupied tiles and houses, rolls back newly created items on validation failure, verifies walking routes with the actual movement engine, tests the return teleport, and requests a world save. Its pending marker is retired after the save. Backups are in Distribution/Backups/ArborMazeV6-20260928, including the pre-install Saves, configuration and assembly.
