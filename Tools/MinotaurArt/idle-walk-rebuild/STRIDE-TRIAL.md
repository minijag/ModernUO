# Original-UO stride comparison

Source: original action 0, direction 0, ten frames in native order, rendered
with original X/Y offsets into original-walk-guide.png. Previous trial inserted
four idle poses and reversed reused lifts, producing a march/pause rather than
the continuous original stride. This trial uses ten generated consecutive poses.
Frame zero is now a walking pose to match the original, not forced idle.

Built-in image generation used the guide for poses and the approved idle for
appearance. Full prompt: stride-prompt.txt. Result: uo-stride-sheet.png.
Packaged manifest: stride-draft.json; PNGs: stride-frames/; GIF: stride-draft.gif.
PrepareStride.py uses one uniform scale, horizontal head-center registration,
and the original top-of-sprite vertical bob. It does NOT prove exact anatomical
or foot trajectory matching. Generated legs approximate the guide, and fur,
eyes and weapon details drift from the approved design. Preview, not final art.

Run AnimationViewer.py --stride for synchronized new/original walking playback.
Only front direction exists in this trial. Original assets, other drafts,
client MUL files and shard remain unchanged.
