# Idle-first fidgets

Both native high-body fidgets (actions 17 and 18), five frames each in five stored directions. Motion Studio mirrors directions 1/2/3 to cover all eight facings. Frame 0 is the exact approved idle PNG and registration for that facing. Forty successor poses were drawn using built-in image_gen with original UO movement guides and the approved idle appearance; the side-view fur was corrected separately. Full prompts: prompts.txt. Source art: poses-0.png through poses-4.png.

Build.py packages transparent sprites using one uniform scale per direction, with manually identified pelvis landmarks. It validates exact idle frame-zero bytes and fixed root registration. This constrains scale and placement; it does not prove pixel-perfect anatomical consistency of generated poses. Review the contact sheets and playback before installation.

Run AnimationViewer.py --fidget. Select Fidget 1 or Fidget 2 and directions 0–7. Draft only: no live MUL files, walking frames, or server changes.
