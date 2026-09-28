# Splatypus: one active dataset

Each project has one active base image/model pair. Reconstruction creates it; optional undistortion replaces its images and camera calibration together. Optional alignment writes a separate model, preserving the base for every subsequent alignment. Alignment works with either original or undistorted images.

## Connect Grasshopper

**Project Root → Active Dataset → Brush Dataset → Brush Train**

1. Import photos or video into the project's images folder.
2. Run **COLMAP All**, or the separate feature extraction, matching and sparse reconstruction components.
3. Optionally run **Undistort Images** on that same project. After success, corrected images and cameras become the active dataset.
4. Optionally preview the current **Dataset** in **Sparse Point Cloud** and **Camera Planes**, adjust it with **Geometry Translator**, and press **Align Model → Write**.
5. Connect Project Root to **SP Active Dataset**, then its single **Dataset** output to **Brush Dataset**. Enable **Prepare** and connect the resulting Brush Dataset to Brush Train. To train the aligned branch, also connect **Align Model → Aligned Sparse Model** to Brush Dataset's optional **Sparse Model Override**. Use **Aligned Text Model** for a separate preview of the saved alignment.

Active Dataset replaces Training Dataset Check and refreshes after project operations finish. Its other outputs are readiness/count diagnostics. Update refreshes after external edits. Undistort returns the same Dataset path for direct chaining; Align returns separate text and binary model paths.

Brush Dataset has **Dataset**, **Prepare** and optional **Sparse Model Override** inputs. Leave the override empty to follow the base model automatically, including undistortion. Connect the aligned sparse output to use alignment. Its directories remain linked, so subsequent successful alignment writes update that branch without another copy. No original-versus-undistorted selection is needed. Undistorted workspace and Replace inputs remain retired.

## Optional actions

- **Undistort Images:** replaces the active images and matching camera model, then refreshes its text view. This intentionally removes the project's previous image/model pair. Unregistered images may be omitted by COLMAP's undistorter. The stale feature database is cleared; extract features again before reconstructing from corrected images.
- **Align Model:** reads the active base binary model, applies one ordered similarity transform to points and camera poses, preserves camera intrinsics, then replaces only the separate aligned output. Repeated writes always start from the base model, so transforms do not accumulate. Keep the base previews connected while adjusting Geometry Translator. After another undistortion or reconstruction, write alignment again before training the aligned branch.
- **Import new media:** invalidates the previous reconstruction and feature database before changing images. Reconstruct again before preparing/training Brush.

Use buttons for Run/Write, or return a toggle to False before running again. Reopened definitions must reset Run/Write to False once before starting an overwrite action; loading a saved True value does not trigger another overwrite.

Replacement work is staged and validated before publication. Failed or cancelled undistortion/alignment retains the current pair; a filesystem replacement failure rolls back earlier moves. If rollback itself fails, the error identifies retained recovery files. A pipeline commits each successful stage; failure in a later stage does not undo earlier successful stages. Dataset mutations and training are serialized per project.

## COLMAP All controls

Find **Splatypus → 03 COLMAP → COLMAP All**. Connect **Exe** and **Root** and press **Run**. Extract, Match, Sparse and Export default to True. **Undistort defaults to False.** Enable it to overwrite the active pair after reconstruction.

Disable stages to reuse existing prerequisites. **Replace** permits sparse reconstruction to replace an existing model; optional undistortion and text refresh always replace their current results after success. Model-changing stages automatically synchronize the readable text view; Export also provides a standalone refresh.

**Progress** displays selected stages completed, not an estimated duration. **Terminal** opens one window for the whole pipeline, streaming each stage into `colmap/pipeline.log`. Standalone components can still open their own terminals. Successful text export prints `COLMAP model saved to: <full path>` in the terminal and Log output. **Cancel** stops the current process and skips later stages. Logs are saved under `colmap`. Inputs are captured when a job starts. The single Dataset output is published on success; Code, Log, Status and Progress remain diagnostic outputs.

COLMAP All and the individual reconstruction components use the same processing runners. Choose either sequence for a reconstruction; individual components allow inspection between stages. Optional alignment and Brush training remain separate actions.

## On disk and existing definitions

| Data | Current location |
| --- | --- |
| Images | `images` |
| Camera model and sparse points | `colmap/sparse/0` (`.bin`) |
| Readable view of the same model | `colmap/sparse-text` (`.txt`) |
| Separate aligned model | `colmap/aligned/sparse/0` (`.bin`) |
| Separate aligned text model | `colmap/aligned/sparse-text` (`.txt`) |
| Brush handoff | `brush-dataset/images` and `brush-dataset/sparse` links |
| Trained splats | `output/<job name>/*.ply` |

Base text files are a synchronized format of the base model. Temporary staging folders are removed after successful replacement. Existing old `colmap/dense` or separately named aligned folders are not selected or deleted automatically; the active base dataset starts from `images` plus `colmap/sparse/0`.

Install both updated plugin binaries together with Rhino closed. Component IDs are retained. Align restores **Aligned Text Model** and **Aligned Sparse Model**, preserving those sockets in older definitions. Its former in-place **Active Dataset** output is removed; reconnect the project Dataset to Brush's Dataset input, and the aligned sparse output to its optional override. Existing definitions gain the override when absent. If an old saved component does not update its sockets correctly, place a fresh instance from the Splatypus toolbar. This change cannot recover a base model already overwritten by the previous in-place alignment; restore or reconstruct that base once if needed.

## Validation

Automated checks cover the single handoff, original and undistorted alignment, paired replacement, cancellation, concurrent operations, filesystem rollback, media invalidation and linked Brush updates. A real COLMAP 3.11.1 fixture verifies undistortion and alignment through actual BIN/TXT conversion. Native macOS validation and testing an existing definition in Rhino remain separate.


All component names and canvas nicknames use the **SP** prefix under the Splatypus tab. New media files use `sp_media_` and Brush exports use `sp_`. Older RS imports and `rhinosplat_` PLY files remain readable.
