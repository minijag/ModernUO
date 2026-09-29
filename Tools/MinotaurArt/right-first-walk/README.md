# Idle-first right/left walk — five-direction preview

Scope: walking only, directions 0–4. No other action work or installation.

All five directions now have ten walking frames plus their exact idle reference.
Directions 1–4 were generated separately from their respective idle PNGs.
BuildDirections.py packages them and preserves the reviewed front files. Run
Build.py then BuildDirections.py when rebuilding everything; Build.py alone
only rebuilds the front manifest. Per-direction contact-N.png and preview-N.gif
are provided, with validation in directions-validation.json. The profile and
rear-left sheets received corrections to distinguish opposite-leg lifts.
Full built-in generation prompts are in direction-prompts.txt.

Frame 0 is a byte-for-byte copy of approved idle. Frames 1–4 lift, peak,
lower and plant the character's RIGHT foot (viewer left); frames 5–8 do the
same for the LEFT foot. Frame 9 copies idle to close the loop. Black-furred
legs are retained. Pelvis landmarks are registered to (0,-35) from the UO root,
placing their X directly on the viewer's gray center line. Idle X is centered
for this preview; original PNG pixels are unchanged.

New artwork was made with built-in image generation. See prompt.txt for the
complete prompt. poses.png is the generated 4x2 sheet, frames/ contains the
packaged ten-frame walk and separate exact idle. Build.py only packages assets;
validation.json records geometric checks. The generated poses still differ
slightly from idle in anatomy, so this is a reviewable trial, not an assertion
of a seamless final cycle. No native MUL files were changed.

Run AnimationViewer.py --right-first to compare the loop with centered idle.
Use Direction 0–4 in the viewer. preview.gif and contact.png are the front view.
Small shape/shading differences between generated poses and copied idle remain;
these are preview assets, not a claim of fully polished game animation.
