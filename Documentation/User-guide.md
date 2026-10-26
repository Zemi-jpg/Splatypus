# Splatypus user guide

## What Splatypus does

Splatypus orchestrates local FFmpeg, COLMAP and Brush applications from Grasshopper. Photographs and video become a camera reconstruction, then a training dataset for Gaussian splatting. You can inspect sparse points/cameras, align the model, preview exported point centers.

The project has one active image/model pair. Undistortion updates both together. Alignment writes a separate model with transformed points and camera poses, leaving the active base pair intact. Keep downstream components connected through Active Dataset. To train the aligned model, also connect Align Model's Aligned Sparse Model output to Brush Dataset's Sparse Model Override input. Re-prepare after changes and do not train while editing the dataset.

## Included example

The package includes [Splatypus_Workflow.gh](../Examples/Splatypus_Workflow.gh) and [setup notes](../Examples/README.md). Disable the solver while reviewing its saved settings, supply your own folders/media/tool paths, then run the workflow in stages. Save a working copy outside the installed package folder.

## Capture and project setup

Use photographs you have permission to process, with strong overlap, varied viewpoints, good exposure and visible detail. Moving subjects, reflections and textureless surfaces are difficult for photogrammetry. There is no universal minimum image count or guaranteed training time.

Create a writable local project with SP Project. Choose a Parent Folder and Project Name, then set Create=True. Root is the shared project path. The folders `images`, `colmap`, `brush` and `output` store separate stages. Keep original media outside the project as a backup.

Install tools using SP Download Tools, or connect full executable paths to SP FFmpeg Setup, SP COLMAP Setup and SP Brush Train. Use Buttons for Run and Cancel. A held True does not start repeated background jobs; reset to False before starting again.

## Import photographs or video

Connect FFmpeg and Root to SP Media Import. Select the source and sampling settings, refresh the estimate after source files change, and run import. FFprobe is used for estimates. Wait for success before reconstructing. Review the imported images for blur and redundancy.

See [FFmpeg estimates](../FFmpeg-estimate.md). Many image pairs in exhaustive matching can be expensive, so begin with a small representative capture.

## Reconstruct with COLMAP

Connect COLMAP Exe and Root to SP COLMAP Pipeline. Its default stages are Extract, Match, Sparse and Export; Undistort defaults to False. Press Run, follow Status/Progress and inspect the log. Replace permits a new sparse reconstruction to overwrite an existing active model.

Alternatively, run Feature Extraction, Exhaustive Matching, Sparse Reconstruction and Export individually. Choose one approach for a given run; do not start two reconstruction workflows on the same project simultaneously.

Inspect the model with SP Model Report, SP Camera Planes and SP Sparse Point Cloud. Optional SP Undistort Images replaces images and calibration together and clears stale feature data. Use SP Geometry Translator and SP Align Model to write a separate model with a useful orientation/scale. Use that aligned output as the optional Brush Dataset model override; the base model is preserved. Reconstruction has no inherent real-world scale without a reference.

See [COLMAP pipeline](../Colmap-pipeline.md), [alignment](../Alignment-branches.md), and [geometry translator](../Geometry-translator.md).

## Prepare and train

Connect Root to SP Active Dataset. Connect its Dataset output to SP Brush Dataset and enable Prepare. Its linked dataset depends on the project files remaining available; moving a project or deleting a target can break links. The project filesystem must support directory links. On Windows, the plugin falls back to directory junctions when symbolic links are unavailable.

Connect the prepared Dataset, Root and Brush Exe to SP Brush Train. Pick a new Job Name. The default is 30,000 steps; use a short test first and increase only after the workflow succeeds. Viewer opens Brush's own viewer; use Viewer=False for a basic background test. Replace=True deletes that job's previous exports before training, so use a new name to retain earlier runs.

Run starts training, Cancel stops it. Completed exports may remain after cancellation. Read Code, Status and the full `brush-training.log` in `output/<job name>`. Reduce workload if GPU memory is exhausted. Download validation only checks executable startup, not real training.

## Preview and export

Load an exported binary little-endian PLY with SP Splat Preview. Max defaults to 150,000 points; lower it to improve interaction. This is a colored point-cloud view of stored centers, not Gaussian splat rasterization. Preview keeps stored coordinates.

Rotate for Visualization in Brush Train writes a separate `_rgs.ply` rotated +90 degrees around X for a compatible external RhinoGaussianSplatting workflow. The original remains available. No third-party Rhino renderer is bundled or licensed by Splatypus.

## Logs, cancellation and recovery

Set Terminal=True before a run for a live terminal where supported. Closing it does not cancel a task: use Cancel. Capture Status, Code and logs for support. Project operations share a dataset lock. Failed or cancelled replacements aim to retain the previous active model, but independent backups remain useful.

Read [troubleshooting](Troubleshooting.md) and [privacy](Privacy.md) before sharing logs or datasets.
