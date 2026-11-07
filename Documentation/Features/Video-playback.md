# Video frames and Rhino playback (0.2.0 development)

Two components in **Splatypus → 06 Preview** reuse the frames extracted by **SP Media Import**. They do not extract a second set or copy the JPEG files.

## SP Video Frames

Connect **Dataset** from SP Active Dataset (or the SP Project root). The **Frames** output contains one ordered branch per source video. For a dataset with 15 photos and four videos, it contains four branches and no still photographs. **Videos** lists the source paths in the same order; its zero-based list index selects a video in Playback.

**Times** and **Registered** have the same branches and items as Frames. Times are seconds from the start of the extracted sequence; Registered indicates whether COLMAP reconstructed that frame's camera. Missing cameras do not remove frames or compress time. An empty source video keeps an empty branch, so subsequent video indices remain stable after a partial import.

Connect **Tracks** directly to SP Video Playback. Frame paths are available before reconstruction; playback requires a COLMAP text model containing `images.txt` and `cameras.txt`. With **Model** left empty, the component searches under `colmap` for existing aligned text models, including `aligned/sparse-text`, `aligned model/text`, `aligned_model/text`, and nested layouts. An aligned model with both nonempty camera files takes priority over the base model, regardless of base timestamps or older revision records. If several aligned models exist, the newest `images.txt` wins. Backup, archive and staging folders are excluded. If none is complete, it uses `colmap/sparse-text`. **Status shows the selected folder's full path.** Transforming preview geometry alone does not write camera poses: save the alignment with SP Align Model first.

The optional **Model** input takes priority over automatic discovery. Supply a text model folder, its parent containing `text` or `sparse-text`, or a project dataset root. A specified model must contain camera image names matching the dataset's extracted frames. Missing or incomplete overrides report an error rather than silently selecting a different model. Binary-only models must first be exported to COLMAP text. Clear Model to return to automatic discovery.

The component watches dataset files and updates automatically after import, reconstruction, undistortion or alignment. There is no Refresh button. Frames lists the JPEGs currently present in the active images folder. If an upstream operation removes frames, those files cannot be replayed; remaining frames retain their original sequence times. Keep the original media and import record with the project.

New imports save `ffmpeg-media.json` beside `images`, recording each source's identity, output prefix and actual extraction FPS. Videos with identical names in different folders stay separate. The record also identifies files produced before an import fails or is cancelled. It contains original source paths; the source videos do not need to remain at those paths for playback.

For older imports without this record, connect the original **mixed media folder or files** to the optional **Media** input. Photographs are filtered automatically, folders are searched recursively, and overlapping inputs are deduplicated. Videos follow their original numeric import order; each video's frames follow numeric frame order. Both `sp_media_` and older `rs_media_` filenames are supported. Same-name videos keep separate groups, although their original folders cannot always be recovered without an import record. Ambiguous single files that also match photograph names are excluded with an explanation; reimporting records exact provenance.

There is no FPS input. Recorded extraction timing is used automatically when available. Older imports use a default pace of two extracted frames per second; **Speed** in Video Playback controls how fast the animation runs. Exact source-video duration is not required. Existing saved Video Frames components retain their media connection, rename it to Media, and remove the retired FPS and Refresh sockets. Older Text Model connections become the optional Model input; definitions from the two-input version gain an empty Model input.

## SP Video Playback

1. Connect Tracks from Video Frames; leave the list intact.
2. Select **Video Index** (0 for the first source, 1 for the second, and so on).
3. Activate the Rhino model viewport to use, then set **Play=True**.
4. Set Play=False to pause. Change **Time** to seek; enable **Preview** to see the sought view while paused.
5. Press a Button connected to **Restore View** to stop and return to the captured view. Toggle Play off before starting again.

**Speed=1** uses the recorded or default pace, 0.5 plays at half speed, and 2 plays twice as fast. **Loop** repeats the sequence with a cut from the end to the beginning. Without Loop, playback holds at the end; toggle Play off/on to restart. **Smooth=True** softens jitter in both camera positions and rotations, then follows a continuous cubic position curve and spherical cubic rotation curve. Intermediate poses can shift slightly; the first and last poses stay fixed, with eased start/stop motion. Camera files remain unchanged. False follows the original positions with straight segments and interpolated rotation. Smooth curves can overshoot between widely separated poses; use False when that is undesirable.

The component outputs the current **Camera** plane, **Time**, **Duration**, and extracted **Frame** path. A camera plane's X axis points right, Y points up, and its normal points backward. Playback requests updates approximately every 16 milliseconds (up to about 60 Hz), using real elapsed time. Actual redraw speed depends on the Rhino scene; slow redraws skip ahead rather than slowing the timeline. No Grasshopper Timer is needed.

Frames without reconstructed poses use interpolation between the available cameras. Before the first and after the last registered pose, the view holds that nearest pose. A video with one pose is stationary; a video with no poses cannot play. Camera calibration sets the vertical field of view; horizontal framing adapts to the Rhino viewport's aspect ratio. Lens distortion and off-centre principal points are not reproduced. Prefer an undistorted reconstruction when accurate framing matters.

Only one playback component controls a viewport at a time. A new player stops the previous player. Reopening a definition does not resume playback with a saved True toggle; switch Play off before starting. Closing/unloading a definition, switching the active document, or locking the player stops its scheduled playback. Model viewports are supported; layout views are not.

This is live Rhino camera playback. It does not display the source video over the viewport, render Gaussian splats, or export a movie. Keep the sparse model, aligned geometry or other Rhino scene you want to view visible in the same coordinate system as the selected camera model.

## Validation

Core checks: `dotnet run --project Tests/RhinoSplat.VideoPlayback.SmokeTest.csproj -c Release`.

The FFmpeg smoke tests also check that real extraction creates the source record and excludes photographs. Native Rhino checks use `Tests/RhinoSplat.VideoPlaybackRhino.SmokeTest.csproj` and `Tests/RunVideoPlaybackTests.py` in a separate Rhino test instance. Build both Grasshopper projects for Windows/macOS compatibility; native macOS playback still requires a manual check.
