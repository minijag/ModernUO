# Reusable menu button artwork

`button-states.png` is the selected original RGBA sprite sheet, generated with
the built-in image-generation tool on 2026-10-08. The imagegen skill guided the
raster workflow. The top disc is normal, the bottom disc is pressed. The round
carved-walnut design and tarnished brass rim fit the shared gump theme; there is
no text or action-specific symbol baked into either state.

Use `CustomGumpStyle.AddMenuButton` with either a static or dynamic builder.
Provide the horizontal center, label Y, reply ID, and label text. The helper
draws a centered label above the button, so this single asset pair can be reused
for any menu action. Each native button is 56 x 56 pixels, IDs 64042 and 64043.

## Rebuild and installation

With Python and Pillow:

```
python Tools/MenuButtonArt/build.py "CLIENT DATA DIRECTORY" artifacts/menu-button-art
```

The packer splits the source vertically, alpha-crops the states, proportionally
fits and centers them, and encodes native RGB555/RLE. Transparency depends only
on alpha, not dark wood colors. It stages copies of `gumpart.mul` and
`gumpidx.mul`; it never writes into the live directory. All other index bytes
are verified unchanged, including the removed blue shield entries and existing
title banner. Decoded native `normal.png` and `pressed.png` are QA outputs.

Occupied slots are rejected unless `--replace-existing` explicitly allows known
56 x 56 menu-button entries. Close ClassicUO and map/art editors, back up both
live MUL files, install the staged pair together, then deploy the matching
server assembly. Preserve subsequent client edits rather than restoring whole
historical MUL backups over them.

## Final generation prompt

Use case: stylized-concept. Asset type: ONE reusable Ultima Online game menu
push-button sprite sheet with two interaction states. Exactly TWO circular
buttons stacked vertically, generous transparent separation and generous outer
margins: top is normal, bottom is pressed. Both discs have identical silhouette,
dimensions, central knotwork pattern, wood grain registration and ornament
locations. Front-facing orthographic flat game UI, no perspective. Design: a
round dark carved walnut disc surrounded by a narrow weathered antique-brass
double-beveled ring; restrained four small brass pins on the cardinal axes;
centered simple shallow-engraved four-fold interlaced knot/compass rosette carved
into the wood, NO lettering and NO function-specific pictogram. This neutral
ornament must work for Website, Wiki, Discord, Help, Stats, Rankings or any other
action. Normal state softly raised with a top-left highlight; pressed state only
slightly darker/recessed and less highlighted, not a different design.
Late-1990s hand-painted realistic fantasy pixel-game material style, dark gritty
and understated, muted brown and ochre palette, crisp contour, simple features
legible reduced to 56x56 pixels. Labels will be rendered ABOVE these circles by
game code and must NOT appear anywhere in this image. Actual transparent alpha
everywhere outside the discs. No text, no letters, no numerals, no shields, no
blue, no gemstones, no ribbons, no scenery, no glow, no exterior drop shadow, no
watermark. Aim for a convincing tactile round push button, not a coin or a flat
logo. Identical state silhouettes so the button never jumps on press.
