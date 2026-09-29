# Slim proportion revision

User requested a cheap horizontal-resampling correction matched to the standing
reference, not a regenerated character. Installed via InstallRedesign.ps1; see
packed/deployment.json for backup path and verified live hashes.

- Walking: 82% previous width. Front walking torso measured 23–26px compared with
  standing 20–21px at comparable central rows; 82% is an approximate compromise.
- Attacks and deaths: 85% width, a milder visual correction for extended poses.
- Reactions and short gestures (10,11,15,16): 90% width.
- Standing (1), pointing idle (17), shoulder-rest idle (18): unchanged. These
  idles already had central waist widths comparable to the standing reference.

Factors for non-walking actions are visual tuning, not exact anatomical fits.
Resampling narrows weapons/heads along with the body; no anatomical redraw was
performed. Uniform factors within each action prevent frame-specific width pumping.
Horizontal anchors are scaled by actual rounded width ratios; height and vertical
anchors are preserved. Nearest-neighbor retains the existing native pixel palette.

225 frames narrowed, 55 frames preserved, 50 index records replaced. All 280
frames decoded and checked with zero color error. Original MUL prefix unchanged;
standing/idle index entries and unrelated creatures untouched. Installed files
are hash-verified. No in-game visual test or server restart performed.

Source: ../redesign-v2/packed/decoded. Script: ../SlimRedesign.py.
Comparison: comparison.png. Native animation previews: packed/preview-*.gif.
Repack with PreserveActions 1,17,18 to retain the original standing/idle records.
