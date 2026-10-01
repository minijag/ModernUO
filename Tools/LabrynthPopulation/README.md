# Felucca Labrynth population

Installed September 28, 2026. This is the enclosed dungeon entered at (5248,784,0), containing the existing minotaur preview at (5276,715,0), not the stock Malas Labyrinth or the outdoor hedge maze.

Source of truth: `Distribution/zodiac.json`, prefix `Labrynth Dungeon - `. Added 129 independent encounter spawners with 266 total creatures. Preserved all 1,900 prior definitions and the existing preview spawner. No map edits or unrelated spawn deletions.

Population: 23 redesigned minotaurs (internal type `TormentedMinotaur`), 41 gargoyles, 30 giant spiders, 41 stone harpies, 30 giant serpents, 59 wraiths, and 42 liches. The existing preview minotaur is additional. No hydras or old-body Minotaur variants.

The live movement survey reached 11,417 standing positions. `Plan.py` uses Z0 floor clearance to identify chamber cores, spaces encounter centers at least eight tiles apart, and excludes a 21-tile Manhattan radius around the entrance. Passage packs contain two creatures or one elite minotaur; larger chambers have four-creature groups, and the great hall has five-creature groups or pairs of minotaurs. Large rooms contain multiple encounters. These are encounter placement rules, not combat balance/playtest results.

Respawn: 5–8 minutes. Spawn radius: one tile. Walking range: three tiles in passages/small rooms, five in larger rooms. Each spawner uses one monster type to ensure predictable population, rather than a lottery that could omit requested types.

`LabrynthPopulationDeployment.cs` is explicitly marker-gated. It surveys movement in batches and validates all planned anchors before creating spawners in batches. It verifies actual spawned counts and dungeon bounds, refuses duplicate GUIDs, then requests a normal world save and retires the markers. It does not install on ordinary restarts. The server log confirms the save snapshot finished after installation. `Distribution/Data/LabrynthPopulation/installed.json` records all 129 GUIDs and their actual populations.

Backup: `Distribution/Backups/LabrynthPopulation-20260928` contains pre-deployment Saves, zodiac.json and UOContent assemblies. Do not restore old Saves over later player progress. Re-running scripts requires deliberate review; definition installation refuses an existing population prefix.

Build passed with zero warnings/errors. Runtime validation confirmed all 266 new creatures spawned. Minotaur display-name changes are included in the deployed assembly.
