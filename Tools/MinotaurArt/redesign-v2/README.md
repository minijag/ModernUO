# Tormented Minotaur — full new-design first pass

Installed September 28, 2026. `packed/deployment.json` records exact live hashes,
installation time and rollback backup. This replaces body 262 / anim5 body 21.
Only that body's 65 populated records were changed. The 45 empty records remain
empty. All 280 original frame slots now contain newly generated character art.

## Scope

Actions 0, 1, 2, 3, 4, 5, 6, 10, 11, 15, 16, 17, 18; five stored directions each.
Standing and walking are approximately 98px high, matching the requested enlarged
test scale. Weapon extension and crouching naturally change individual bounds.
The sprites depict the new broad black head, upward ivory horns, muted flesh,
white eyes and black furry legs, not the rejected recolored original animation.

This is a complete-coverage **first pass**. Original poses guide the new art; the
new artwork is not pixel-identical skeletal retargeting. The walking stride is
restrained, and generated poses have some shape/equipment variation. Native data
validation does not establish perfect visual continuity. No live in-game visual
test was performed. Further polish should edit these new sheets, never restore
the old recoloring as a substitute.

## Files and pipeline

- `action-*.png`: generated source sheets, built-in image-generation tool.
- `guide-*.png`: original source-pose references assembled by ActionGuides.ps1.
- `frames/`: 280 trimmed native-sized sprites and world-anchor metadata.
- `extraction-audit.json`: bounds of every extracted sprite, zero outer-sheet
  edge contacts. Transparent gutters determined separately in each column.
- `packed/anim5.mul`, `packed/anim5.idx`: installed replacement files.
- `packed/decoded/`: all 280 actual decoded frames.
- `packed/preview-*.gif`: animated native decoded strips with all five facings.
- `packed/overview.png`: representative frames from all 13 actions.
- `packed/verification.txt`: decoder validation result.

Extraction uses binary alpha appropriate for MUL, preserving only substantial
coverage so invisible colored fringe is not made opaque. A shared 240-color
RGB555-compatible palette limits frame-to-frame palette flicker. Each action and
direction uses uniform scaling across its frames. Standing/attack anchors derive
from the feet; death anchors follow the original motion offsets.

Scripts: `../ExtractRedesign.py`, `../Pack.ps1`, `../Verify.ps1`,
`../PreviewRedesign.py`, `../InstallRedesign.ps1`. Packing/verifying must pass
InputDir=`redesign-v2/frames` and OutputDir=`redesign-v2/packed`; old defaults refer
to rejected experiments. Installation requires closed clients/editors, exclusive
file access, an unchanged source prefix, backups, and post-write hash checks.

## Validation

280 frames decoded with correct counts, dimensions, anchors and alpha.
Maximum RGB555 channel error: 7. Exactly 65 intended index records changed.
Original MUL bytes preserved as an unchanged prefix. Other creatures and empty
slots untouched. Installed file hashes match staging. All decoded preview sprites
fit their display canvases. No server restart or gameplay changes were needed.

## Generation prompt set (built-in tool; no fallback CLI)

Common identity/reference specification: replace original orange minotaur sprites
in the action guide with the new character, not a recoloring of the old anatomy.
Use the approved source for broad black bull head, upward ivory horns, white
eyes, gray-taupe muscular human torso, black furry hips and legs, brown bracers
and single axe. Gritty low-contrast UO pixel art. Equal cells, transparent alpha,
complete bodies, generous gutters; no text, grid, floor, shadow or glow. Rows
front, front-left, left profile, rear-left, back, preserving original facing
order and pose progression. Back views must show backs. Consistent physical size.

Action-specific prompts:

| Action | Sheet | Motion instruction |
|---|---|---|
| 1 | five across | Neutral standing turnaround, axe lowered, same weapon hand. |
| 0 | 10 columns × 5 rows | Complete alternating walk cycle with contact/down/passing/up phases, opposing leg in second half, modest bob and arm swing. |
| 2 | 4 × 5 | Backward death: upright recoil, tipping backward hips low, falling horizontal, lying flat on back. |
| 3 | 4 × 5 | Forward death: stagger, lean forward, fall face-forward, lie face down. |
| 4 | 4 × 5 | Overhead axe strike: raised weapon, forward swing, low bent follow-through, recovery. |
| 5 | 4 × 5 | Low-to-high sweep: axe across waist, dip low, straighten and raise arm, backward follow-through. |
| 6 | 4 × 5 | Both hands on one axe: overhead windup, downward swing, deep crouch, recover across waist. |
| 10 | 4 × 5 | Hit recovery: drooping shoulders, knees flexed recoil, begin to straighten, upright alert. |
| 11 | 5 × 5 | Flinch: hunched hand across belly, recover, recoil upright arm lifted, drop shoulders, bend forward. |
| 15 | 3 × 5 | Both arms and axe forward at chest, extend farther, relax to upright. |
| 16 | 3 × 5 | Torso twist axe across abdomen, dip and draw axe across waist, untwist. |
| 17 | 5 × 5 | Idle gesture: relaxed, free forearm crosses body, hand by face, arm out, point aside. |
| 18 | 5 × 5 | Axe on shoulder and free hand at chin: look left, forward, right, chin rub, return. |

Source generation IDs by action:

0: exec-445b98c1-417a-4b5f-9b0e-fb5bd56ea6cc
1: exec-0c6a05b4-07c4-4c88-af59-1c6a700d2638
2: exec-5364c7ce-f8c8-4c5c-9492-171a25fad2e8
3: exec-944e8c5e-2100-4bf1-aae1-b7f4e93a6449
4: exec-93b2878a-ff5f-4dc6-a7ae-21bc8eacd962
5: exec-7c64fd87-cf9d-4764-a440-fee94024bade
6: exec-676a8a01-824f-4fd6-b025-8f8deb50ef68
10: exec-5849a166-9b67-415c-aefe-3bd45e6b40a1
11: exec-ac8b078a-c647-4758-a725-410bc6ef9133
15: exec-cd789f1b-5825-4e8c-8f30-df2426928984
16: exec-89321e89-7aca-4691-9360-9e34549d4a71
17: exec-e440edcf-9630-4502-b3d5-ca507144284c
18: exec-e3e33b35-a915-45c2-a460-0602c97e62b1
