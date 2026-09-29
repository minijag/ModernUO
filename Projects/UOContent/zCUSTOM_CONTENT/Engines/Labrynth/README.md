# Labrynth Dungeon entrance

Three one-way Felucca entrances: (1872,908,-18), (1873,908,-19), (1874,908,-17). Destination: Felucca (5248,784,0). No return teleporter requested.

Definitions live in Distribution/Data/teleporters.json. Live items are ordinary invisible, active Teleporter instances named Labrynth Dungeon, persisted by normal world saves, not spawners.

LabrynthEntrance is an opt-in startup migration gated by Data/labrynth-entrance.pending. It checks only these exact definitions, preflights conflicting teleporters, creates missing entries without duplication, and schedules a world save. It does nothing without the request file.

Installed 2026-09-28: startup reported three created; world save completed at 12:50:46 local. Request retired to Distribution/Backups/Labrynth-20260928/labrynth-entrance.applied after save confirmation. Original assembly and installation log are in that folder. Pre-install world snapshot: Backups/Automatic/2026-09-28-19-50-00.

Do not run global TelGen just to repair this entrance: it regenerates all configured teleporters. For deliberate recovery, restore the request file and restart; retire it after confirming the world save. Destination walking behavior has not been tested in-game.
