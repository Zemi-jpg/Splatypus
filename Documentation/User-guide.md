# Splatypus user guide — 0.2.0

Splatypus connects local FFmpeg, COLMAP and Brush applications through Grasshopper in Rhino 8. Import photographs or video, reconstruct cameras, align the result to Rhino geometry and train a Gaussian splat. You can also capture an existing Rhino scene from known camera frames and train from those images directly.

Use Rhino 8.34 or newer with Grasshopper and .NET 8. FFmpeg, COLMAP and Brush are installed separately; Brush needs a compatible GPU. See [installation](Installation.md) for platform requirements and tool setup.

## Choose a workflow

| Goal | Workflow |
| --- | --- |
| Train from photographs or video | Project → Media Import → COLMAP Pipeline → Brush Dataset → Brush Train |
| Align a reconstruction to Rhino geometry | Sparse Point Cloud + Camera Planes → Quick Align → optional Geometry Translator → Align COLMAP Model |
| Replay a reconstructed video camera path | Video Frames → Video Playback |
| Retrain a Rhino scene with design geometry | Camera Planes Frames → View Capture → Brush Dataset → Brush Train |

The sections below explain the connections and run order. Components are under the **Splatypus** tab; names such as **Root**, **Dataset** and **Xforms** refer to their sockets.

## Set up a project and tools

1. Place **SP Project** under **01 Setup**. Choose a writable local **Parent Folder** and **Project Name**, then set **Create=True**. Its **Root** output is the shared project path.
2. Use **SP Download Tools** to install external tools, or supply your installed executable paths. Its FFmpeg, COLMAP and Brush outputs connect to the corresponding Exe inputs. Setup components validate paths; COLMAP Setup also prepares project folders. FFprobe must be beside FFmpeg for video estimates.
3. Use Buttons for **Run**, **Write** and **Cancel**. If using a toggle, return it to False before another run. Review saved True and Replace settings before enabling an existing definition.

Keep original media outside the project as a backup. Avoid running two operations on the same dataset at once, and do not change its images or cameras while training.

The included [Splatypus_Workflow.gh](../Examples/Splatypus_Workflow.gh) is the SP_020 working template for version 0.2.0. Disable the solver before opening it, review saved controls and connections, set your own media, folders and tool paths, and save a working copy outside the installation folder. See the [example notes](../Examples/README.md).

## Import photographs or video

Use sharp images with strong overlap, varied viewpoints and visible detail. Moving subjects, reflections and textureless surfaces can make reconstruction difficult.

1. Connect **SP FFmpeg Setup → FFmpeg** to **SP Media Import → Exe**, and **SP Project → Root** to **Root**.
2. Supply files or folders to **Media**. Folders are searched recursively. **FPS** controls video sampling; the default is 2 frames per second. Each still photograph contributes one image.
3. Choose **Format** and optionally enable **Skip Duplicates**. Review **Estimate** and **Probe**; refresh the estimate after changing files inside a source folder.
4. Press **Run**, wait for success and inspect the imported images before reconstructing.

| Setting | Default | Use |
| --- | --- | --- |
| Format | 0 — JPEG | Choose 1 for PNG to preserve existing source transparency. PNG does not remove backgrounds. |
| Skip Duplicates | False | Skip near-identical sampled video frames. Still photographs remain. |
| Replace | False | Replace images managed by Media Import, including its earlier JPEG/PNG imports. |

Matching-format stills are copied without re-encoding, retaining existing metadata. Converting formats does not guarantee EXIF/GPS retention. Keep geotagged JPEG photographs as JPEG when that metadata matters.

Estimate is the approximate count **before** duplicate filtering. Check coverage after filtering. Imports save surviving-frame timing in `ffmpeg-media.json` beside `images`; keep this record for playback. Importing new media invalidates the previous reconstruction and feature database, so reconstruct again before training.

See [media estimates](../FFmpeg-estimate.md) and [0.2.0 import options](../Version-0.2.0-options.md).

## Reconstruct cameras with COLMAP

Connect **SP Download Tools → COLMAP** (or a full COLMAP executable path) to **SP COLMAP Pipeline → Exe**, and connect project **Root** to **Root**. The pipeline's canvas nickname is **COLMAP All**. **Extract**, **Match**, **Sparse** and **Export** default to True; **Undistort** defaults to False. Press **Run** and follow **Status**, **Progress** and **Log**. **Replace=True** permits a new sparse reconstruction to replace the active model.

Alternatively, run **SP COLMAP Feature Extract**, **SP COLMAP Match**, **SP COLMAP Sparse Reconstruct** and **SP COLMAP Export Model** individually. Use either the pipeline or the individual stages for a given run.

| Matching | Method | Choose it when |
| --- | --- | --- |
| 0 | Exhaustive — default | You need all image pairs considered, including mixed photos and videos. Large sets can be expensive. |
| 1 | Sequential | Images form an ordered sequence, such as one video. |
| 2 | Spatial | Every database image has a usable position prior, and positions are not all identical. |
| 3 | Auto | You want Spatial with complete usable positions, otherwise Sequential for one confirmed recorded video, otherwise Exhaustive. |

Geotagged photographs can provide position priors. Drone video telemetry and sidecars are not automatically assigned to extracted frames. Status and the log explain Auto's choice.

**Extract Size=3200** limits the longest image side used for feature extraction; **Features=8192** limits features per image. These controls do not resize stored source images. Changing either rebuilds stored features and clears old matches: run extraction and matching again before reconstructing.

Connect **Root → SP Active Dataset → Dataset** to **SP Sparse Point Cloud** and **SP Camera Planes** to inspect the result. The project has one active base image/model pair. Optional **SP COLMAP Undistort Images** replaces its images and camera calibration together, updates the text model and clears stale feature data. COLMAP may omit unregistered images during undistortion. Verify transparency afterwards if you rely on alpha.

Reconstruction has no inherent real-world scale without a reference. See [COLMAP and active datasets](../Colmap-pipeline.md) for stage reuse and replacement details.

## Align the reconstruction to Rhino geometry

Alignment has two steps: preview a transform, then **write it to the camera model**. Moving preview geometry alone does not change the cameras used for training or video playback.

### Quick alignment with three point pairs

1. Keep **SP Sparse Point Cloud** and **SP Camera Planes** connected to the original active Dataset. Connect **Sparse Point Cloud → Cloud** to **SP Quick Align → Cloud**, and **Camera Planes → Planes** (optionally Centres too) to **Quick Align → Cameras**.
2. Hide upstream cloud and camera previews to see Quick Align's result clearly.
3. Double-click **SP Quick Align**. Pick a highlighted sparse point, then its matching reference point on native Rhino geometry. Repeat for three pairs in the order source 1/reference 1, source 2/reference 2, source 3/reference 3. Use widely separated points forming a triangle and Rhino object snaps for the references.
4. Inspect the transformed Cloud and Cameras. **Error** reports the RMS fitting distance in Rhino document units. Double-click again to refine the currently aligned result; **Xform** remains the complete transform from the original model.

**Esc** cancels picking and retains the last completed alignment. **Reset** restores original coordinates and outputs an identity transform. Completed picks and Reset support Grasshopper Undo, and pairs are saved with the definition. If the source cloud changes, repick or Reset before writing. Reset does not reset Geometry Translator's sliders.

### Fine adjustment and saving

For Quick Align alone, connect **Xform → SP Align COLMAP Model → Xforms**. Supply the original **Dataset** and COLMAP **Exe**, then press **Write**.

For fine adjustment:

1. Connect Quick Align's **Cloud** and **Cameras** to **SP Geometry Translator → Geo**. Choose **CenPt** in the already aligned coordinates, then adjust movement, rotation in degrees and positive uniform scale.
2. Connect **Quick Align → Xform** to **Geometry Translator → Previous**. Previous records the earlier alignment for saving; the geometry input already contains that alignment.
3. Connect **Geometry Translator → Transforms** directly to **Align COLMAP Model → Xforms**, supply the original Dataset and Exe, and press **Write**. Release any Data Dam before writing.

Do not flatten duplicate transform branches from cloud and camera geometry, or add Quick Align's transform a second time. The writer uses identical branches once; different sequences require selecting the intended branch. Without Quick Align, use Geometry Translator on the original previews and leave Previous empty.

Write creates the separate `colmap/aligned` model from the active base. Repeated writes always start from that base. It transforms all reconstructed points and camera poses, including those omitted by preview limits, while preserving intrinsics.

Use **Aligned Text Model** for a separate saved-result preview and **Aligned Sparse Model → Brush Dataset → Sparse Model Override** for aligned training. After reconstructing or undistorting again, rewrite the alignment. Keep the base Dataset connected to the alignment source previews.

See [Quick Align](../Quick-alignment.md), [Geometry Translator](../Geometry-translator.md) and [alignment writing](../Alignment-branches.md).

## Prepare and train with Brush

1. Connect **Root → SP Active Dataset**, then **Dataset → SP Brush Dataset → Dataset**. Set **Prepare=True**.
2. To train in aligned coordinates, also connect **Align COLMAP Model → Aligned Sparse Model** to **Brush Dataset → Sparse Model Override**. Leave the override empty to use the active base model.
3. Connect **Brush Dataset → Dataset** to **SP Brush Train → Dataset**. Supply **Brush Exe**, project **Root** and a new **Job Name**.
4. Start with a short test before using the default **30,000 Steps**. **Viewer=True** opens Brush's viewer; choose False for background training without that viewer. Press **Run**.

| Training control | Default | Effect |
| --- | --- | --- |
| Resolution | 1920 | Maximum image side loaded for training; independent of COLMAP extraction resolution. |
| Max Splats | 10,000,000 | Upper bound on splat growth. Final count can be smaller. |
| Replace | False | True deletes the selected job's previous exports before training. Use a new Job Name to keep them. |

Training runs outside Grasshopper's document solution, so you can continue working. Inputs are captured at the start; edits affect the next job. Brush still shares GPU resources with Rhino. Lower resolution and splat counts can reduce workload but do not guarantee a GPU-memory limit.

**Cancel** stops the job and retains already exported checkpoints. Outputs update when the process exits. Read **Status**, **Code** and **Log**; the full log and PLY exports are in `output/<job name>`. Removing the component or closing its document cancels its job.

Brush Dataset links to project images and the model rather than copying them. The filesystem must support directory links; Windows can fall back to junctions. Keep project files available and prepare/check the handoff after model changes or moving a project.

## Preview exported splats

Load an exported binary little-endian PLY into **SP Splat Preview**. **Max=150,000** limits displayed points; reduce it for easier interaction. This component shows colored point centers. Use a separate compatible Gaussian renderer to inspect the full splat appearance.

**Rotate for Visualization=True** in Brush Train writes an additional `_rgs.ply`, rotated +90 degrees around X for the external RhinoGaussianSplatting workflow. Both original and rotated files are in Export. Match the PLY and camera coordinates when combining them with Rhino geometry. No third-party renderer is bundled.

## Replay a video camera track

1. Connect **SP Active Dataset → Dataset** to **SP Video Frames → Dataset** under **06 Preview**. **Frames** lists existing JPEG/PNG paths in one branch per source video, excluding still photographs. **Videos** gives the corresponding zero-based indices; **Times** and **Registered** show timing and camera availability.
2. Leave **Model** empty to prefer an existing aligned text model under `colmap`, falling back to the base text model. **Status** shows the selected path. To choose a specific model, connect a text model folder or project root to Model. Write alignment first if playback should follow aligned geometry.
3. Connect **Tracks → SP Video Playback → Tracks**, leaving the list intact. Choose **Video Index** and activate a Rhino model viewport.
4. Set **Play=True**. Set False to pause, change **Time** to seek, and enable **Preview** to apply the sought camera while paused.
5. Adjust **Speed** (1 normal, 0.5 half speed, 2 double speed), **Loop** and **Smooth**. Smooth defaults to True and softens position and rotation jitter. It can shift intermediate poses; choose False to follow original positions with straight segments.
6. Press **Restore View** to stop and return to the view saved before playback. Toggle Play off before starting again.

Playback needs a COLMAP text model with registered cameras matching the frames. Missing poses interpolate between available cameras; a track with no registered camera cannot play. The Camera output is a plane with X right, Y up and negative Z forward. Playback animates the Rhino view; it does not export a movie or overlay the source video.

New imports use saved timestamps, including gaps left by duplicate filtering. For older imports without `ffmpeg-media.json`, connect the original media files or mixed folder to **Video Frames → Media**. Older imports use a default pace of two extracted frames per second; adjust Speed as needed. Dataset changes update automatically; there is no FPS or Refresh input on Video Frames.

See [video frames and playback](../Video-playback.md) for model selection, interpolation and framing limits.

## Capture a Rhino scene for retraining

Use **SP View Capture** to record an existing scene, including visible native Rhino design geometry, from known camera positions. The captured images receive new calibration and can go directly to Brush without another COLMAP reconstruction.

1. Put the scene and cameras in the same coordinates. For a saved alignment, load **Aligned Text Model** with **SP Camera Planes**. Connect **Camera Planes → Frames** to **SP View Capture → Frames**. Use Frames, not the preview surfaces on Planes; flatten multiple frame branches into one list.
2. Make the desired geometry and renderer visible. Hide camera surfaces, sparse inspection clouds and other guides. For material-based capture, bake geometry or use a material-aware preview. Pause Video Playback and other camera animation.
3. Choose a **fresh destination project Root**, separate from the source reconstruction. An initialized project with empty images and colmap folders is also accepted. Existing images, models or capture metadata are rejected.
4. Choose **Width/Height** (default maximum 1920 × 1280), **Mode** (Rendered), **Lens** (35 mm equivalent), **Background** (white) and **Wait** (1 second). Increase Wait for a heavy renderer; try 2–3 seconds in a small batch.
5. Activate an unlocked Rhino model viewport and press **Run**. Keep it visible and unchanged during capture. Avoid navigating, resizing, switching views or starting other Rhino commands. Keep other windows and the cursor away from the capture area.
6. After success, inspect the PNGs. Connect **View Capture → Dataset** to **SP Brush Dataset → Dataset**, enable Prepare, then connect its Dataset to Brush Train. Use the captured project Root for Brush Train's Root. Leave Sparse Model Override empty for this new dataset.

Capture moves the live Rhino camera, waits for the renderer, then saves through **ScreenCaptureToFile**. Width and Height define the aspect ratio and maximum size: larger requests are reduced to fit the visible viewport. **Status** reports the actual dimensions used in calibration. Enlarge the viewport before starting if you need more pixels. The original view and display mode are restored after completion or cancellation.

Frames preserve position, orientation and roll, but contain no original lens calibration. Lens and aspect ratio define a new shared pinhole camera; they need not reproduce the original photographs' framing. Raytraced/progressive modes are unsupported. A renderer must support Rhino's screen capture; verify a small batch first. Capturing SP Splat Preview captures its point-cloud proxy.

Images go directly into `Root/images`, cameras into `Root/colmap`, and the prepared Brush handoff into `Root/brush/dataset`. **Dataset** outputs the completed project Root. Calibration is saved as COLMAP binary/text files plus `transforms.json`; capture does not create reconstructed sparse points. Brush can initialize this camera-only dataset. Do not reconstruct or undistort these synthetic pinhole captures before training.

**Cancel** preserves saved images under Root/images. Images and Dataset outputs are withheld until the complete batch succeeds. Inspect Status and `capture-progress.txt` after an interruption, then use a fresh Root for another batch.

See [viewport capture](../Viewport-capture.md) for camera conventions, output files and renderer requirements.

### Estimate a matching background

**SP Auto Background** estimates a solid color or top/bottom gradient from baked PLY colors near camera boundaries.

1. Connect the coordinate-matched **Splat PLY**. If the displayed splat object was transformed, supply the same **Transform**.
2. Share View Capture's **Frames**, **Lens** and **Aspect** (Width / Height, default 1.5).
3. For a gradient, connect **Bottom → View Capture Background** and **Top → Background Top**. For a solid fill, connect **BG → Background** and leave Background Top empty.
4. Enable **Preview** to inspect the gradient in the active viewport. It fits one stable backdrop across supplied frames. Disable Preview to restore the normal backdrop. Wire colors explicitly to View Capture so the saved batch uses them.

Keep the external splat renderer's relighting off when matching its baked appearance. Auto Background changes the viewport fill; it does not reconstruct missing scenery or alter lighting. Inspect Status if boundary samples are sparse or no colors are visible, and verify PLY/camera alignment. Without Frames, it samples the active viewport when it solves; recompute after changing that camera.

See [automatic background](../Auto-background.md) for supported PLY formats and estimation limits.

## Project files and upgrading existing definitions

| Data | Location relative to Root |
| --- | --- |
| Imported or captured images | `images` |
| Base camera model | `colmap/sparse/0` |
| Base readable text model | `colmap/sparse-text` |
| Saved aligned model | `colmap/aligned/sparse/0` and `colmap/aligned/sparse-text` |
| Brush handoff for imported media | `brush-dataset` |
| Brush handoff for View Capture | `brush/dataset` |
| Training exports and log | `output/<job name>` |
| Import source/timing record | `ffmpeg-media.json` |

Back up definitions and projects before upgrading, and keep one installed plugin copy with its matching Core library. New controls retain defaults when unconnected. **SP COLMAP Match** replaces the old Exhaustive Match name while keeping its component ID and Exhaustive default.

Existing View Capture **Folder** connections become **Root** connections at the same socket. Point them at the destination project root, not its images subfolder. Video Frames migrates older media/model connections and removes retired FPS/Refresh sockets. In older dataset definitions, reconnect the project Dataset to Brush Dataset and use Aligned Sparse Model only as its optional model override. Check sockets before running; place a fresh component if an older instance does not migrate correctly.

## Logs and recovery

Enable **Terminal** before a run for live output where supported. Closing a terminal does not cancel its task; use Cancel. Preserve Status, Code and a short log excerpt for support. Media imports and viewport captures retain files already written after cancellation; completed Brush exports may also remain. A pipeline keeps successful earlier stages if a later stage fails.

Read [troubleshooting](Troubleshooting.md) and [privacy](Privacy.md) before sharing datasets or logs. Native macOS validation of the new interactive alignment, playback and capture workflows remains a release testing step.
