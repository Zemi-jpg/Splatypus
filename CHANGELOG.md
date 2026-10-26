# Release notes

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
