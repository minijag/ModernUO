# Shard Motion Studio

Double-click **Launch Motion Studio.cmd** to open the read-only animation viewer.
Requires Python with Pillow and Tk; the launcher prefers the existing bundled
Python runtime and falls back to Python on PATH.

Two panels play independently selected sources/actions on a shared frame clock.
Choose a direction, speed and integer pixel zoom. Space pauses; left/right arrows
step frames. The bottom slider scrubs and pauses playback. Shorter cycles wrap;
idle remains stationary. Reload files after generating a new draft.

Root guides use the native animation placement formula:
left = rootX - X; top = rootY - Y - Height. No per-frame centering is applied.
Bounds show the complete sprite canvas, including transparent padding.

The new walk draft contains only direction 0. Missing directions are explicitly
shown as unavailable. Current installed revision means the previously verified
walk-idle-match export, not a live read of anim5.mul. The viewer never installs
assets or changes the shard/client. This is sprite playback, not a simulation of
UO movement speed or world rendering.

Developer smoke test: `python AnimationViewer.py --self-test` validates every
source image and tests GUI construction, frame stepping, reload and missing poses.
