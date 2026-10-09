# Shared title banner

Selected artwork: darker muted ochre-gold cloth, toned down from the yellow swatch.
Dimensions and placement are unchanged.

`banner-source.png` was generated using the built-in image-generation tool on
2026-10-01. The imagegen skill guided the raster artwork workflow. The original
RGBA source is retained; `build.py` proportionally scales it and encodes only
native gump 64040. Binary transparency comes from alpha, never from cloth colors.

Run with Python and Pillow:

```
python Tools/GumpBanner/build.py "CLIENT DATA DIRECTORY" artifacts/gump-banner-art
```

Output is staged copies of gumpart.mul/gumpidx.mul and the 300 x 32 banner PNG.
All other index records are verified unchanged. Close clients/editors, back up
both live MUL files, then install the pair together. The script refuses an
already-occupied slot unless `--replace-existing` explicitly authorizes replacing
the existing 300x45 or 300x32 banner at 64040.
Deploy the matching server assembly before reopening themed menus.

Generation prompt: One isolated wide medieval cloth title banner for Ultima
Online classic gumps, front-facing orthographic, muted dark burgundy, subtle
same-color folds and restrained gritty late-1990s hand-painted material. Broad
flat blank center for a separately rendered title, symmetrical folded-back
swallowtail tips. No text, symbols, gold trim, patterns, rope, poles, shields,
wood, background, or external shadow. Real RGBA transparency. Crisp contour,
no cropped tips, legible at roughly 300 x 38 pixels.

Thin revision: edited with the built-in image tool. Prompt: Change ONLY the
thickness, preserving width, burgundy color, gritty cloth shading, symmetrical
folded swallowtail tips and blank center. Reduce silhouette height about 30%,
targeting 9.4:1 and a 300x32 UI sprite. Keep a broad straight center with subtle
folds; no lettering, symbols, trim, poles, ropes, or external shadows. Preserve
real RGBA transparency, clean edges, and uncropped ends.

Muted-color revision: edited with the built-in image tool. Prompt: Change ONLY
the saturated red cloth color to muted earthy umber with a subtle brown-red
undertone, very low saturation, like faded oxblood cloth beside walnut wood.
Keep similar midtone brightness for white text. Preserve the slender silhouette,
width, height, folds, swallowtail tips, shading and blank center. No added text,
details, trim or shadows; preserve genuine RGBA transparency and framing.

Burgundy refinement: built-in image edit. Prompt: Make ONLY a modest color
adjustment from brown toward muted burgundy, dusty wine-red with subtle brown
undertones. Clearly more burgundy but still restrained and desaturated, not
bright crimson, scarlet or purple. Preserve thin silhouette, dimensions, folds,
swallowtail ends, brightness, texture, blank center and framing. No text, trim,
ornaments or external shadow. Preserve genuine transparent RGBA background.

Golden revision: built-in image edit with the brown ribbon as edit target and
the user's yellow swatch as color reference. Prompt: Recolor ONLY the cloth to
closely match the reference golden yellow, with yellow-gold highlights and
restrained warm ochre fold shadows. Matte cloth, not metal. Preserve the thin
silhouette, proportions, folds, swallowtail tips, blank center and framing.
No text, ornaments or external shadow. Preserve genuine RGBA transparency;
do not copy the white swatch background or thicken the ribbon.

Muted gold revision: built-in image edit. Prompt: Change only color and
brightness; reduce yellow saturation roughly half and brightness roughly 20%
for weathered ochre/antique mustard cloth beside walnut. Keep recognizable gold,
not brown, green or gray; matte with no glowing or metallic highlights. Preserve
silhouette, proportions, folds, blank center, tips, texture and framing. No text
or ornaments; genuine RGBA transparency and clean edges. No shape changes.

Darker ochre revision: built-in image edit. Prompt: Change ONLY brightness,
darkening the entire cloth about 25 percent including highlights. Keep current
ochre-gold hue, restrained saturation, matte material and readable folds; do not
make brown or increase saturation. Preserve thin silhouette, proportions,
texture, folds, tips, blank center and framing. Genuine RGBA transparency;
no text, ornaments, metallic glare or extra shadows.
