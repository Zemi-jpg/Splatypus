# Splatypus 0.2.0

## Added

- **Quick Align:** align a reconstruction to Rhino geometry using three point pairs, with refinement, reset and alignment error feedback.
- **Video Frames and Video Playback:** browse extracted frames by source video and move the Rhino camera along reconstructed tracks, with smooth playback, seeking, speed and loop controls.
- **View Capture:** capture the Rhino scene from known camera frames and prepare calibrated images for Brush training without repeating camera reconstruction.
- **Auto Background:** estimate a solid or gradient backdrop from splat colors, preview it in Rhino and use it for viewport capture.

## Updated

- **Media Import:** choose JPEG or PNG, preserve existing transparency with PNG and optionally skip near-duplicate video frames while retaining playback timing.
- **COLMAP:** choose Exhaustive, Sequential, Spatial or Auto matching, and adjust feature extraction resolution and feature count.
- **Alignment:** combine Quick Align with Geometry Translator fine adjustments and save the complete alignment consistently with the preview.
- **Brush Train:** adjust training resolution and maximum splat count.
- **Camera Planes:** output camera frames that connect directly to View Capture.
