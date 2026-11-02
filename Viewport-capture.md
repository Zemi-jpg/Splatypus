# Viewport recapture for splat retraining (0.2.0)

**SP View Capture** is in **Splatypus → 06 Preview**. It records the scene visible in a Rhino model viewport at a list of camera positions and orientations. Use it to capture an existing reconstruction together with native Rhino design geometry, then train a new splat from the combined views.

| Input | Default | Meaning |
| --- | --- | --- |
| Frames | Required to run | One list of camera planes; connect the new Frames output from SP Camera Planes |
| Width / Height | 1920 / 1280 | Maximum output pixels; 3:2 aspect ratio, reduced proportionally to fit the current viewport |
| Display Mode | Rendered | Installed Rhino mode name (English or localized) or mode ID; Shaded also works |
| Lens | 35 | Focal length in mm, 35 mm equivalent, shared by all cameras |
| Background | White | Opaque solid background, or gradient bottom if Background Top is connected |
| Root | Documents/Splatypus/Captures | Destination project root; images, colmap and brush are written directly inside it |
| Run | False | Button or False-to-True transition starts one batch |
| Cancel | False | Stops capture, preserves saved images and restores the view |
| Wait | 1 second | Live renderer settling time after each camera move (0.1–60 seconds); increase for heavy splat scenes |
| Background Top | Unconnected | Optional opaque top color for a two-color gradient; connect Top from SP Auto Background |

For an estimated backdrop matching the scan, use [SP Auto Background](Auto-background.md). Connect its **Bottom → Background** and **Top → Background Top**, or **BG → Background** alone for a solid fill. Share Frames, Lens and capture aspect ratio with the estimator. It samples baked PLY colors near camera boundaries and fits a stable backdrop for the batch; it does not relight splats or reconstruct missing scenery. Existing saved capture components append the optional Top input while retaining all earlier sockets and connections.

Capture uses Root itself as the project: PNGs go directly into images, the known-camera model into colmap/sparse/0 and colmap/sparse-text, and the prepared Brush training dataset into brush/dataset. There is no nested capture folder, temporary capture path or final folder rename. The Dataset output is Root. Brush/dataset links to the project images and camera model, avoiding duplicate image storage. SP Brush Dataset reuses this location for capture projects.

Use a fresh project root, or an initialized project with empty images and colmap folders. Existing images, models and prior capture metadata are rejected rather than overwritten. Saved Folder connections retain their socket but are now interpreted as project Root: update old paths that end in images. Inputs are copied at Run; held True does not repeat a batch. Cancel and failure preserve already saved files; incomplete Images/Dataset outputs are withheld. Use a fresh Root for a new run.

Capture animates the **actual active Rhino viewport**. Each frame has separate steps: move the camera, redraw the live view, return to Rhino's event loop for the Wait interval, then run Rhino's **ScreenCaptureToFile** command. The component waits for command completion before validating the PNG and advancing. The view window is only reduced within its original visible area. Requests larger than that area are reduced to the largest whole-pixel size with exactly the requested aspect ratio. Status reports the actual size, and camera calibration uses those actual dimensions. Enlarge the Rhino viewport before starting if you need more pixels. Keep it visible and avoid navigating, resizing it, switching views or starting other Rhino commands during the batch. A changed view stops capture to avoid saving incorrect camera calibration.

Screen capture saves the live viewport at its actual pixel size, without the separate view-capture render pass. The viewport title is temporarily hidden and restored afterwards; the native window border is cropped without resampling. Keep other windows and the cursor away from the capture area. Output dimensions are verified before the image is accepted; the component does not rescale images after capture.

Wait is a minimum settling interval, not a renderer-specific completion signal. Try 2–3 seconds if a heavy Gaussian renderer still shows a previous frame. Rhino continues processing paint and UI events between steps. Cancel takes effect between steps or when an active native capture command returns; it cannot interrupt a renderer blocked inside Rhino. During capture, `Root/capture-progress.txt` records the last operation (moving, waiting, command capture or dataset writing).

## Camera frames and calibration

A frame's origin is the camera center, X points right, Y points up, and the camera looks along **negative Z**. SP Camera Planes' new **Frames** output uses this convention. Its original **Planes** output remains the preview surfaces with their original COLMAP image-axis convention; use **Frames** for capture. The SP Video Playback Camera output uses the same right/up/back convention.

For custom planes, orient negative Z toward the subject and set Y to the desired image up direction. Plane size does not control the field of view. Flatten multiple frame branches into one list and supply one value for each other control. Image order matches the input list exactly: `sp_capture_000001.png`, `sp_capture_000002.png`, and so on.

The planes retain camera centers, orientation and roll; they do not contain the original camera's intrinsics. Lens and output aspect ratio define a new pinhole camera. A 35 mm default does **not** reproduce every original photograph's framing, distortion or principal point. Match the original calibration when that is necessary. All images in a batch use the same lens and resolution. Saved calibration describes the newly captured images, so future training uses their actual camera model.

## Capture the combined scene

1. Align the reconstruction, design geometry and camera frames in the same Rhino coordinates. Apply the same transforms to the Frames when adjusting geometry.
2. Activate an unlocked Rhino model viewport. Pause SP Video Playback and other camera animation.
3. Make the desired geometry and renderer visible. Hide camera preview surfaces, sparse inspection clouds, plane axes, grids from custom previews and other guides. Deselect Grasshopper geometry if its selection color is unwanted. For material-based design capture, bake geometry or use a material-aware preview.
4. Connect Frames, choose the output settings and Root, then press Run. Progress and Status update as each image is captured. Keep the Rhino and Grasshopper documents active during capture.
5. Inspect the PNGs. Connect **Dataset → SP Brush Dataset → SP Brush Train**. Use the captured Dataset as the new project's Root as well, or choose a separate project Root for training exports. Camera reconstruction and image undistortion are unnecessary for these synthetic pinhole images.

The capture uses Rhino's native **ScreenCaptureToFile** command after updating the live viewport, with display conduits enabled. Native geometry and previews supported by that command are included. A temporary copy of the chosen display mode applies the background and hides grids, axes and Rhino object selection highlighting in the live view. The user's view projection, camera target, window size and display mode are restored after success, failure or cancellation, and the temporary mode is removed. Existing display-mode definitions are not edited. Temporary capture modes are hidden from menus and removed after capture. Grasshopper startup also removes abandoned modes matching the exact generated SP Capture naming pattern; any viewport still using an abandoned mode is switched to Rendered.

**SP Splat Preview is a decimated colored point-cloud proxy.** Capturing it produces that proxy, rather than full Gaussian rasterization. To capture the original splat appearance, use an external Gaussian renderer compatible with Rhino's ScreenCaptureToFile command at the requested resolution. Live camera movement and a settling interval let renderers refresh camera-dependent state before capture; they cannot add capture support to a renderer that lacks it. Verify a small batch and the alignment of splats with native geometry first. Raytraced/progressive display modes are rejected because a fixed wait cannot guarantee convergence. No renderer is bundled by this feature.

## Dataset outputs

Images is the list of completed PNG paths. Dataset is the completed root folder. Progress is 0–100; Status reports progress or an error. Image/Dataset outputs are withheld until the whole batch is complete. Cancellation, closing/removing the component, disabling the solver or changing the active document stops the batch; already saved images are preserved in Root/images.

The completed folder contains:

```text
images/sp_capture_000001.png ...
colmap/sparse/0/cameras.bin
colmap/sparse/0/images.bin
colmap/sparse/0/points3D.bin
colmap/sparse-text/cameras.txt
colmap/sparse-text/images.txt
colmap/sparse-text/points3D.txt
transforms.json
brush/dataset/images -> ../../images
brush/dataset/sparse -> ../../colmap/sparse
capture-project.json
capture-progress.txt
```

COLMAP files describe known PINHOLE cameras in Rhino world coordinates. `transforms.json` stores equivalent OpenGL camera-to-world matrices, image paths and pixel focal lengths for compatible NeRF-style trainers. These files record the new image calibration and preserve frame positions/orientations; no reconstruction or coordinate normalization is performed. Sparse points and feature observations are empty because capture provides cameras directly. Trainers requiring a reconstructed point cloud need their own initialization; this capture does not generate a sparse reconstruction or promise a particular training result.

The pinned Brush 0.3.0 supports random initialization when sparse points are absent. Its image-header probe requires at least 16,387 bytes per image, even for a valid smaller PNG. Small captures include a standard PNG text chunk to satisfy that probe without changing image pixels. Native checks include a short Brush training/export run from the captured camera-only dataset; they do not establish final reconstruction quality or compatibility with every external renderer. macOS capture still requires native platform testing.

Reference: [Rhino ScreenCaptureToFile](https://docs.mcneel.com/rhino/8/help/en-us/commands/viewcapture.htm).
