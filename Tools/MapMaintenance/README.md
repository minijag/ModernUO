# Castle removal, 2026-09-27

Removed the old castle in inclusive Felucca rectangle X=5246..5353,
Y=858..979, all elevations. Morgan's castle to the east is unaffected.

`ClearCastle.cs` stages the bounded binary map edit: 13,176 land tiles become
black terrain ID 580 (0x244), Z=0, matching surrounding void. All 22,503 statics
inside the rectangle are removed from the active static block records.
It verifies unchanged terrain and static records outside the rectangle.
The source directory is read-only; Stage writes into a separate destination.

The matching 26 spawners were removed from zodiac.json. A one-time world-save
migration removes local server items and non-player NPCs, with a safety check
for players, controlled/summoned/stabled creatures, and player houses. The
migration is not included in the production assembly.

Pre-change map files, zodiac.json, world save, and assembly are preserved in
`Distribution/Backups/Castle-removal-5246-20260927`. Restore map0.mul,
staidx0.mul and statics0.mul together, only with the server/client/editors closed.
Restoring the world save would also roll back subsequent world progress.
