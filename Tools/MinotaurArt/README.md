# Tormented Minotaur redesign

## Current state: slimmer new-design animations installed

Latest installed revision: `walk-idle-match/` replaces walking in `redesign-slim/`.
The user found walking too thin; 50 walking frames now match the idle width more
closely, derived from uncompressed art. Other actions are unchanged. Verification
passed again after closing the leftover profile window; installed hashes and
rollback backup are recorded in `walk-idle-match/packed/deployment.json`.

The active revision is `redesign-slim/`: 225 moving frames narrowed horizontally,
with standing and two already-matching idles preserved exactly (55 frames).
Walking -18%, attacks/deaths -15%, reactions/short gestures -10%. Heights unchanged.
All 280 frames decode correctly; zero palette error and only 50 intended records
changed. Backup and installed hashes: `redesign-slim/packed/deployment.json`.
See that revision's README for measurements, limitations, and previews.

## Previous full new-design first pass

September 28, 2026, 16:47: installed **280 newly drawn frames across all 65 populated action/direction records**, based on the approved muscular black-headed minotaur. The active assets are in `redesign-v2/`, NOT `adapted/` or `full-set/`. All 13 actions have five stored facings; ClassicUO mirrors the remaining facings. Standing/walking target approximately 98px tall. Native packing, full decode, alpha/anchor checks and installed hashes passed. No live in-game visual test was performed; this is a complete coverage first pass, not a claim of perfectly polished motion. See `redesign-v2/README.md` for provenance, previews and remaining visual caveats.

Backup immediately before installation: `Distribution/Backups/MinotaurRedesign-20260928-164737`. Server, creature stats and test spawner unchanged. Start ClassicUO to load the replacement. Do NOT reinstall the rejected recolored-original experiment documented below.

## Historical notes (superseded by the current state above)

## Rejected and reverted: original-animation adaptation

Installed September 28, 2026. All **280 nonempty source frames** across **65 action/direction records** (13 populated actions × five stored facings) are adapted. The earlier 325 count incorrectly counted null results as one-frame animations; empty slots are preserved, not fabricated.

The live anim5 pair now contains the original Tormented Minotaur's genuine poses and movement, with charcoal head/mane and legs, desaturated lighter flesh, subdued white eye accents on visible facings, and 1.10× frame dimensions and anchor offsets rounded to pixels. Original horn silhouettes, weapon, frame counts and motion remain. This is a palette/material adaptation of the original art, not the generated mockup's anatomy. It replaces the previous repeated standing pose entirely.

Pipeline: ExportOriginal.ps1 reads the pre-test backup; Landmarks.ps1 defines per-pose head/hip landmarks, including fallen poses; Adapt.ps1 applies material colors and scale; Pack.ps1 writes RGB555/RLE records; Verify.ps1 decodes every frame with Ultima.dll and checks counts, dimensions, anchors, transparency and color error. Playback.py packages the decoded frames into animated QA strips.

Validation passed: 280 frames; maximum RGB555 channel error 7; exactly 65 intended index records changed; existing MUL bytes preserved as an unchanged prefix; all unrelated and empty records untouched. Five directional contact sheets inspected. Animated decoded previews are in full-set/action-*.gif. No live client screenshot was taken.

Backup of the preceding 10%-larger standing test: Distribution/Backups/MinotaurFullSet-20260928. Pristine original animation files remain in Distribution/Backups/MinotaurPreview-20260928. Server and test spawner at Felucca 5276,715,0 were not changed. Restart ClassicUO to load the full art set. Do not rerun old Stage.ps1 against live files to install a standing-only preview again.

## Historical single-pose test

## 10% scale revision

Standing test pose enlarged from 57×89 to 63×98 pixels (rounded to whole pixels), installed September 28, 2026. Output and round-trip preview: staged-110. Previous test art backed up in Distribution/Backups/MinotaurPreview-110-20260928. Other actions are unchanged. Full matching animation set is NOT implemented: inventory contains 325 stored frames across 22 action slots and five directions. Requires new frame artwork or an animated model; do not represent the standing test as a completed animation replacement.

September 28, 2026. Body 262 maps through bodyconv.def to anim5 body 21.

- One approved still pose replaces standing action 1 in the five stored facing slots (2315–2319); client mirrors the remaining facings. This is intentionally NOT a complete animation set. Turning may mirror the same pose; walking, combat and death use the original art.
- Native decoded dimensions: 57×89. Source was extracted with the built-in image-generation tool using the approved mockup as reference: isolate minotaur, transparent background, preserve muted skin, black fur, white eyes and upward horns. Stage.ps1 trims alpha margins, scales, quantizes to UO RGB555 and encodes one-frame animation records. The decoded.png is the actual MUL round-trip result.
- Original anim5.mul bytes remain intact; the new animation record is appended. Only five anim5.idx entries differ. All other animation slots are unchanged.
- Test spawner: Felucca (5276,715,0), TormentedMinotaur, count 1, home/walking range 0, 5–7 minute respawn. Definition lives in Distribution/zodiac.json. This is a normal hostile creature, not an invulnerable display model.
- Source artwork: source.png. Encoded preview: staged/decoded.png. Output art: staged/anim5.mul and anim5.idx.
- Backups: Distribution/Backups/MinotaurPreview-20260928 contains original anim5 files, prior zodiac.json, assembly and Saves. To revert artwork immediately after this test, close ClassicUO/UOFiddler and restore that anim5 pair. If later art edits exist, restore only the five saved index entries rather than overwriting unrelated changes. Do not restore old world Saves merely to revert client art.
- Server installer MinotaurPreviewDeployment is opt-in and its pending marker is retired after confirmed save. Restart the client to read the art. No changes to creature stats or body IDs.
