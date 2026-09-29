# Idle-based walk rebuild — staged, not installed

## Pelvis registration correction

PrepareFront.py now reads manually reviewed anatomical points from
pelvis-landmarks.json and adjusts X/Y metadata only. All ten front frames place
that point at (0, -35) relative to the UO root, directly on the gray center line.
Frame zero's pixels remain exact idle; its X offset is now intentionally centered
too. Landmark X coordinates were rechecked against the rendered contact sheet.
alignment-check.json records each translation and verifies
zero landmark displacement across the cycle. No sprite rescaling was added.
Motion Studio shows the landmark in cyan when Root guides are enabled.
This supersedes the shared-cell-origin approach described below: generated
artwork drifted inside those cells. Remaining anatomy/pose variation and actual
in-game foot contact are separate from this registration correction.

Start with each direction's exact installed idle image. The five
frames/0-DIRECTION-0.png files are byte-for-byte copies of the corresponding
walk-idle-match/packed/decoded/1-DIRECTION-0.png files.

Frame-zero metadata must likewise copy the matching idle X,Y,Width,Height,
changing only Action to 0. Never derive horizontal anchors from foot centroids:
the previous front cycle ranged from X=27 to X=44, a likely contributor to the
reported lateral sliding. Each new pose must be registered to the same body-root
coordinate as idle, with crop offsets accounted for, not independently recentered.

The first front-facing nine-pose draft is packaged in front-draft.gif. Frame 0
remains exact idle; successors share a single scale and common registered root,
not per-pose foot centroids. PrepareFront.py verifies all five frame-zero files
byte-for-byte and writes their exact idle metadata to frame-zero.json.

Artwork generated with built-in image generation (no fallback CLI). Prompt:
preserve exact attached front idle identity, lean torso width, head, horns,
shoulders, muted colors and axe; nine successor walking-in-place poses in a 3x3
sheet; alternating small left/right strides, subtle counter-swing, fixed torso
and pelvis center, return toward idle at the end, transparent background with no
labels/shadow. Source: exec-c74c00c3-1895-4621-b184-d606886c9bfc.png.

This is NOT an accepted finished cycle: generated successors still need identity,
proportion, foot-contact and frame-9-to-0 transition review. A shared root removes
the old anchor heuristic but does not prove absence of artwork drift or foot
skating. Other directions have only frame zero. No client files have changed.
