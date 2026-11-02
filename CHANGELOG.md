# Release notes

## 0.2.0 — 2026-11-02

### Added

- **Quick Align:** align a reconstruction to Rhino geometry using three point pairs, with refinement, reset and alignment error feedback.
- **Video Frames and Video Playback:** browse extracted frames by source video and move the Rhino camera along reconstructed tracks, with smooth playback, seeking, speed and loop controls.
- **View Capture:** capture the Rhino scene from known camera frames and prepare calibrated images for Brush training without repeating camera reconstruction.
- **Auto Background:** estimate a solid or gradient backdrop from splat colors, preview it in Rhino and use it for viewport capture.

### Updated

- **Media Import:** choose JPEG or PNG, preserve existing transparency with PNG and optionally skip near-duplicate video frames while retaining playback timing.
- **COLMAP:** choose Exhaustive, Sequential, Spatial or Auto matching, and adjust feature extraction resolution and feature count.
- **Alignment:** combine Quick Align with Geometry Translator fine adjustments and save the complete alignment consistently with the preview.
- **Brush Train:** adjust training resolution and maximum splat count.
- **Camera Planes:** output camera frames that connect directly to View Capture.

## 0.1.0 — 2026-10-11

First public release, published on [food4Rhino](https://www.food4rhino.com/en/app/splatypus) for Windows and macOS. This version is preserved by the Git tag `v0.1.0`.

- Splatypus Grasshopper tab with 21 component icons and stable component IDs inherited from RhinoSplat.
- Project folders, pinned external tool downloads, media import and image-count estimates.
- Individual COLMAP stages and an all-in-one pipeline with optional undistortion.
- One active image/model dataset, camera and sparse-point previews, coordinate alignment and geometry transforms.
- Brush dataset preparation, background training, PLY exports and colored point-cloud preview.
- Background progress, logs and cancellation.
- MIT licensing, publication documentation, dependency credits, listing artwork and platform-specific Yak packaging.

### Limitations

- Rhino 8.34+ with .NET 8 only; Rhino 7 and Rhino 8 in .NET Framework mode are not supported.
- No Gaussian splat renderer or texture atlas generation is included.
- A separate macOS package is available. Automated package verification does not establish native macOS reconstruction and training test coverage.
- Automatic tool installation supports Windows x64 and Apple Silicon macOS. Intel macOS and Windows ARM are not supported by the automatic installer.
- Old definitions may need reconnection to the single Active Dataset output; see Colmap-pipeline.md.

### Upgrade from development builds

Move old manual RhinoSplat/Splatypus installations out of Grasshopper's Components Folder before using Package Manager. Back up definitions and projects. Do not keep both packages installed: their component IDs are identical.
