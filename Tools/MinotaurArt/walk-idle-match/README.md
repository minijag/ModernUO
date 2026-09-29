# Walking width correction

Installed walk-only correction using the user's idle screenshot as reference.
The leftover Choose Profile window was closed normally at the user's request;
verification was rerun before installation. Backup and installed hashes are
recorded in packed/deployment.json.
The previous 82% walking reduction was too severe. Regenerated mechanical resizes
from the uncompressed redesigned sprites, not from the previously narrowed art.

Front idle envelope: 65px; original front walk median: 61px. Match that median
using 65/61, retaining frame-to-frame stride variation. Other non-profile facings
use their own idle-to-median-walk envelope ratios. Profile uses the median ratio
of the other facings: its extended axe/stride makes total envelope width an
unsuitable body-width target. This is a visual proportion approximation, not
an anatomical redrawing. Factors are recorded in factors.json.

All 50 walking frames changed; all 230 other frames and their index records
preserved. Heights and vertical offsets unchanged; horizontal offsets rescaled.
All 280 frames decode correctly with zero color error. Exactly five intended
records changed, original MUL prefix intact. No live in-game visual check performed.

Native decoded previews: packed/preview-0.gif. Comparison: comparison.png.
Source/script: ../MatchIdleWalk.py. Standing, combat, death and idles unchanged.
