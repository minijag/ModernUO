# Alternating-step trial — preview only

Open AnimationViewer.py with --fluid, or select Alternating step trial in Motion
Studio. Source sheet: fluid-step-sheet.png. Packaged frames: fluid-frames/;
manifest: fluid-draft.json; playback: fluid-draft.gif.

Built-in image generation produced this new sheet from exact idle. No CLI used.
The final prompt is recorded in fluid-prompt.txt.

The generated sheet did not follow every requested phase: cell 2 lifts the wrong
leg. Selected cells 1/8 provide the left lift, 6/7 the right lift. Reusing the
first lift on descent yields a symmetric lift/peak/lower progression. Exact idle
is retained at frames 0,4,5,9. This is a conservative stepping-in-place trial,
not a finished locomotion cycle. Repeated contact frames introduce a pause.

Each supporting hoof is registered to a constant ground baseline; the artwork
is uniformly scaled, not stretched per frame. Horizontal trunk landmarks are
manually reviewed. The head bob remains inconsistent in the generated artwork,
and the idle-to-generated anatomical transition is not yet seamless. Do not
claim the full gait or in-game movement is fixed. No MUL or shard files changed.
