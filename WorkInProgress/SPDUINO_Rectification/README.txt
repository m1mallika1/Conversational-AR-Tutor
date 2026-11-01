SPDUINO reference-image rectification (work in progress, NOT used by the app yet)
================================================================================
Kept outside Assets/ on purpose so Unity does not import these files.
The app still uses Assets/ReferenceImages/SPDUINO_Compatible_Board.jpg (axis-aligned crop).

Source: the user's top-down SPDUINO photo (1152x1536 raw, EXIF orientation 6 -> 1536x1152 upright).

Files
- pcb_corners_photo_px.json      PCB corners (TL, TR, BR, BL) in the UPRIGHT photo, pixels.
                                 From lines fitted to the red PCB edges (fit residual 0.3-0.9 px).
                                 Edge angles: top +2.66, bottom +2.14, left -3.45, right -2.40 deg
                                 (board rotated ~2.6 deg with slight keystone).
- SPDUINO_rectified_equalized.jpg   Proposed new reference image. Perspective-corrected to the
                                 PCB corners, 1170x900 px = 65 x 50 mm (18 px/mm), grayscale,
                                 histogram-equalized. arcoreimg eval-img score: 100.
- SPDUINO_rectified_autocontrast.jpg Same rectification, autocontrast instead. Score: 65.
- SPDUINO_rectified_color_preview.jpg Colour preview of the rectified board (half size).
- spduino_pins_mm_rectified.json Header-hole centres mapped through the same homography.
                                 Units: mm, origin = PCB bottom-left (USB side left), y up.
                                 Same convention as PinRecord.PositionMm in SpduinoProfile.cs.
                                 Differs from the current SpduinoProfile values by 2.06 mm mean,
                                 2.92 mm max (D0).
- current_crop_vs_pcb_edges.jpg  Evidence: yellow = current crop used by the app,
                                 cyan = fitted PCB edges.

Caveats
- Measured pin pitch is ~2.61 mm vs the standard 2.54 mm: header tops sit ~8.5 mm above the PCB
  (typical value, not measured) and appear ~3% larger in the photo. Marker height/parallax
  correction is still open (needs the measured header height).
- Board size 65 x 50 mm is the user's measurement; re-check with a ruler.

To apply later (not done): replace the library texture with SPDUINO_rectified_equalized.jpg
(keep 0.065 m width), update SpduinoProfile pin positions from spduino_pins_mm_rectified.json,
then Editor-test, build once, and re-run the phone scan protocol with full logcat capture.
