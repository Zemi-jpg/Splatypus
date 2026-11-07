# Splatypus 0.2.0 capture and training options

All new inputs have defaults and can remain unconnected. Saved components retain their original input positions and component IDs; new settings are appended when loading a definition. The matching component is now named **SP COLMAP Match**, retaining the old Exhaustive Match component's ID and default behavior.

| Component | New input | Default | Choices / meaning |
| --- | --- | --- | --- |
| SP Media Import | Image Format / Format | 0 | 0 JPEG; 1 PNG |
| SP Media Import | Skip Near Duplicates | False | Remove near-identical sampled video frames; still photographs remain |
| SP COLMAP Pipeline and SP COLMAP Match | Matching Method / Matching | 0 | 0 Exhaustive; 1 Sequential; 2 Spatial; 3 Auto |
| SP COLMAP Pipeline and SP COLMAP Feature Extract | Extraction Max Image Size / Extract Size | 3200 | Longest image side used for feature extraction |
| SP COLMAP Pipeline and SP COLMAP Feature Extract | Maximum Features / Features | 8192 | Maximum SIFT features per image |
| SP Brush Train | Maximum Training Resolution / Resolution | 1920 | Longest image side loaded for training |
| SP Brush Train | Maximum Splat Count / Max Splats | 10000000 | Upper limit on splats, not a target count or guaranteed memory limit |

All size/count settings must be positive integers. Changing settings during processing affects the next run; it does not restart or modify the running job.

## JPEG, PNG and transparency

PNG preserves an alpha channel already present in a source image or supported video. It does not remove backgrounds or invent transparency in ordinary video. Brush can use transparent image input for alpha-aware training. JPEG does not store alpha. Keep JPEG for geotagged JPEG photographs when retaining their original EXIF calibration/GPS is important.

Matching-format stills are copied without re-encoding: JPEG to JPEG (including .jpeg sources), and PNG to PNG. Other sources are converted by FFmpeg. Format conversion does not promise to transfer EXIF/GPS metadata. Optional COLMAP undistortion is a separate image rewrite; verify transparency after that operation when using alpha.

**Imported Images** and **Estimated Images** replace the old JPEG-specific output labels at the same output indices. Replace removes the component's managed JPEGs and PNGs, including older rs_media_ JPEGs, while leaving independently named user images alone. Switching formats requires Replace when a previous import exists.

## Duplicate filtering and playback

Filtering applies FFmpeg's mpdecimate to the FPS-sampled video frames. It runs independently for each source video. Estimate is the approximate count before filtering, so the actual count may be lower. This is pixel-similarity filtering, not an overlap guarantee or automatic assessment of capture quality; inspect coverage after extraction.

The media record stores surviving-frame timestamps. Video Frames supports both JPEG and PNG sequences, and Video Playback retains gaps in the original source timing. Older regular-FPS imports continue to work. Timing is stored outside the image folder in ffmpeg-media.json.

## Choosing matching

- **Exhaustive (0):** match all image pairs. This remains the default for existing and new components.
- **Sequential (1):** match neighboring filenames in order. Suitable for an ordered video sequence. Mixed videos/photos or unrelated clips may need additional cross-matching; Auto uses a conservative fallback for those datasets.
- **Spatial (2):** use COLMAP's position priors to propose nearby image pairs. Splatypus verifies that every database image has a usable position and that positions are not all identical before launching. Missing/partial positions produce an explanatory error instead of silently leaving images unmatched.
- **Auto (3):** first use Spatial when usable positions cover the entire database; otherwise use Sequential when all database images exactly match one recorded video sequence; otherwise use Exhaustive. Status/log records the actual choice and its reason.

Geotagged photographs can supply position priors through EXIF. Drone video telemetry, container location tags and SRT sidecars are not automatically converted into per-frame positions by this update. A drone video normally uses Sequential through Auto unless its database already contains complete per-frame positions. GPS can guide candidate matching; it does not by itself guarantee reconstruction accuracy or set Rhino model units.

Auto reads the existing database through the operating system's SQLite library. No additional executable, downloadable model or package installation is required. If inspection is unavailable, Auto explains the fallback to Exhaustive; explicit Spatial reports the prerequisite failure.

## Extraction and training limits

Extraction resolution affects COLMAP's feature computation; training resolution independently affects Brush. Neither setting resizes the stored original image files. Changed extraction settings rebuild the feature database and remove old feature matches, because COLMAP otherwise skips already extracted features. Re-run matching after changing extraction settings. Existing sparse models remain usable until a new reconstruction replaces them.

Brush's splat cap limits growth; the final count can be smaller. Lower resolution and splat caps can reduce workload, but are not hard GPU-memory reservations. These options retain the pinned tools' defaults when unconnected.

## Validation

Automated checks cover real JPEG/PNG extraction, non-opaque alpha in original PNG and video conversion, duplicate selection/timestamps, format replacement, invalid values, matching decisions, actual pinned COLMAP Sequential/Spatial execution, stored-feature rebuilding, Brush argument forwarding, cancellation, and saved-component migration. Windows and macOS targets are compiled; native macOS execution and interactive Grasshopper checks remain platform/user testing steps.

Official references: [FFmpeg filters](https://ffmpeg.org/ffmpeg-filters.html), [COLMAP 3.11 matching](https://colmap.github.io/legacy/3.11/tutorial.html#feature-matching-and-geometric-verification), [Brush 0.3.0](https://github.com/ArthurBrussee/brush/tree/v0.3.0).
