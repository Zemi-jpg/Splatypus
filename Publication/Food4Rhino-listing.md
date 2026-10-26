# Food4Rhino listing copy

First public release: 0.1.0. Public author: Zeming Li. Support: [lizeming2021@gmail.com](mailto:lizeming2021@gmail.com). Website: [zeming.li](https://zeming.li/). These details are recorded in release.json.

## Name

Splatypus

## Short description

Photogrammetry and Gaussian splat training workflows for Grasshopper in Rhino 8.

## Description

Bring photographs and video into a connected reconstruction and training workflow in Grasshopper.

Splatypus connects FFmpeg media import, COLMAP camera reconstruction and Brush Gaussian splat training through a shared local project. Run a complete COLMAP pipeline or use the individual components to inspect each stage. Preview cameras and sparse points, align the active model, prepare a Brush dataset and train with progress, logs and cancellation.

Exported PLY files can be inspected as colored point clouds in Rhino.

Features include:

- Project folders and user-triggered installation of pinned external tools.
- Photograph/video import with frame-count estimates.
- Individual COLMAP stages and an all-in-one reconstruction pipeline.
- Optional undistortion and alignment through one active dataset.
- Camera-plane, sparse-point and geometry inspection tools.
- Brush dataset preparation, background training and PLY exports.
- Colored point-cloud preview.
- Logs, progress and cancellation for long-running jobs.
- An included Grasshopper example: Examples/Splatypus_Workflow.gh, with setup notes.

**Requirements:** Rhino 8.34 or newer running .NET 8, with Grasshopper. Windows x64 is the primary release target. FFmpeg, COLMAP and Brush are separate applications downloaded on request or supplied through manual paths. Brush requires a compatible GPU.

**Scope:** Splatypus includes a point-cloud preview, not a Gaussian splat viewport renderer. Third-party applications and trademarks remain with their owners; no affiliation or endorsement is implied.

**macOS:** A separate package is provided. Automatic Mac tool installation targets Apple Silicon only.

**Install:** In Rhino run PackageManager, search Splatypus, install and restart. Choose Package Manager or manual installation and keep one installed copy.

## Suggested form values

| Field | Value |
| --- | --- |
| App type | Grasshopper add-on |
| Name/package | Splatypus |
| Compatible Rhino | Rhino 8; specify 8.34+ and .NET 8 in description |
| Platform | Windows x64 and macOS (Apple Silicon tool installation) |
| License/price | MIT License; free |
| Version | 0.1.0 |
| Author/developer | Zeming Li |
| Support email | lizeming2021@gmail.com |
| Website | https://zeming.li/ |
| Copyright | Copyright (c) 2026 Zeming Li |
| Keywords | photogrammetry, gaussian-splatting, grasshopper, reconstruction, point-cloud, COLMAP, Brush, FFmpeg |

Select categories available in the current form that fit photogrammetry, reconstruction, visualization and geometry. No exact category names or upload pixel requirements are claimed without checking the live form.

## Assets and captions

- assets/splatypus-logo-512.png: Splatypus product mark.
- assets/splatypus-hero-1600x900.png: Photogrammetry and Gaussian splat training workflows in Grasshopper.
- assets/splatypus-workflow-1600x900.png: A local workflow from media to camera reconstruction, training and PLY output; optional alignment and undistortion.
- assets/splatypus-components-1600x1000.png: The 21 Splatypus components grouped by workflow stage.
- ../Examples/Splatypus_Workflow.gh: Downloadable Grasshopper workflow definition included in the packages.
- Splatypus-Quick-Start.pdf: Installation, the first workflow, limits and recovery.

The graphics are explanatory diagrams and icon sheets, not screenshots of completed reconstruction. Alt text and editable SVG masters are documented in Asset-register.md. Resize/crop copies only if the upload form requires it.

## Linking Food4Rhino and Package Manager

Publish the Yak package first, then using **Create App from Yak** (or the current equivalent) in Food4Rhino with package name Splatypus. For an existing listing, use its linking option or contact Food4Rhino support rather than creating a duplicate.

McNeel describes this integration in [developer news](https://www.rhino3d.com/en/e-news/0224f4r_dev/) and [staff guidance](https://discourse.mcneel.com/t/gh-plugin-version/121483/14). Use the integration option available in your publisher account.
