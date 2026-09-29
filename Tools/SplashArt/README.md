# Login splash replacement

Replaces castle background gump 9001 (0x2329) in the shared Ultima Online Classic 7.0.15.1 relpor gumpart.mul/gumpidx.mul. ClassicUO LoginGump uses this for versions 5.0.0a through pre-7.0.64.0.

Source: ../../Assets/SplashConcepts/watercolor-extended-4x3.png (approved painting extended downward).
Install.cs renders the complete 4:3 painting across the full 640x480 background with no black bands. The client's existing login controls overlay it. It appends RGB555/RLE data and changes only index 9001. No server changes. final-preview.png is decoded from the installed artwork; prior previews are historical.

Original MUL files: ../../Distribution/Backups/Splash-20260927/.
Pre-fullscreen MUL files: ../../Distribution/Backups/Splash-fullscreen-20260927/. fullscreen-preview.png is decoded from the new full-screen MUL record.
Pre-top75 MUL files: ../../Distribution/Backups/Splash-top75-20260927/. top75-preview.png shows the current decoded background.
original-9001.png is the original castle background; installed-preview.png is decoded from the staged MUL. Other index entries were byte-compared and unchanged, preserving shield buttons and borders.

Restart ClassicUO to reload artwork. To restore, close ClassicUO and UOFiddler and restore the two backed-up MUL files together (only if no later artwork changes have been made).
