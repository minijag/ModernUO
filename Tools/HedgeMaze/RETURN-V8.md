# Hedge additions and corrected dungeon return

September 28, 2026. Felucca, Z0.

- Three ordinary world-saved Static(0xDB8) hedges at X1800, Y925–927. MUL files unchanged.
- Southern Gazer Patrol: one Gazer at 1831,961, home range 1, walking range 2, respawn 6–9 minutes. Definition in Distribution/zodiac.json.
- Existing return teleporter moved from 5249,784 to 5248,784; destination unchanged at 1873,914. Matching source definition updated in Distribution/Data/teleporters.json. No duplicate return gate left behind.
- Cave arrivals land on the return gate without immediately bouncing. Step off, then back onto 5248,784 to return to the maze.
- ArborReturnDeployment installs only with its opt-in marker, validates movement to all named encounter centers and the return landing, tests arrival and gate activation, and saves. It rolls back newly created world items and the gate position if validation fails.
- Backup: Distribution/Backups/ArborMazeV8-20260928 (pre-install Saves, assembly and configurations).
