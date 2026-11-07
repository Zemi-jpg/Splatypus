# Splatypus macOS workflow

This is a Mac-targeted build candidate, compiled on Windows against the pinned Rhino/Grasshopper 8.34.26223.11001 SDK. It targets Rhino 8.34 or newer using .NET 8. Native Rhino/macOS integration and real Mac reconstruction/training have not yet been verified. Gaussian splat viewport rendering is excluded.

## Install the prepared package

1. Use Rhino 8 with its .NET 8 runtime. Close Rhino before copying plugin files.
2. In Grasshopper, use File > Special Folders > Components Folder to identify the actual Libraries folder. If upgrading from RhinoSplat, move its old plugin folder outside the Components Folder first. Both versions share component IDs and must not be installed together.
3. Copy `Splatypus.gha` and `Splatypus.Core.dll` from the extracted ZIP together into a `Splatypus` subfolder there. Keep only one installed copy and restart Rhino. Look for the Splatypus tab and platypus icon. The Mac and Windows versions use the same component IDs.

When working from source, the files are built in `dist/macos`; `bash Scripts/Install-Mac.sh "/absolute/path/to/Grasshopper/Libraries"` installs them.

## External tools

On Apple Silicon, use **01 Setup → SP Download Tools**. Leave Folder blank and connect a Button to Download. It installs pinned FFmpeg (including ffprobe) and CPU COLMAP in private environments, plus the official Brush binary, then returns executable paths for the later components. No Homebrew or system PATH changes are required. Native installation still needs Mac testing; see [Download Tools](Download-tools.md).

For live installation output on Apple Silicon, set Download Tools **Terminal=True** before Download. macOS may ask to allow Terminal automation. The component also saves a log under Tools Folder/logs and returns its path through **Log** when the attempt finishes or is cancelled.

Intel Macs do not have a published Brush 0.3.0 binary, so automatic installation currently reports that limitation. Manual installation remains available. To install FFmpeg (including ffprobe) and COLMAP with Homebrew:

```sh
brew install ffmpeg colmap
command -v ffmpeg
command -v colmap
```

Paste the full executable paths into Grasshopper. Typical locations are `/opt/homebrew/bin` on Apple Silicon and `/usr/local/bin` on Intel. Do not paste the `.app` directory. For Brush, use a native executable from the official release or build the CLI from source, and use its full executable path. Check `brush --help` for the installed version. This integration expects a dataset positional argument and `--total-steps`, `--export-path`, `--export-name`, and optional `--with-viewer`.

References: [FFmpeg package](https://formulae.brew.sh/formula/ffmpeg), [COLMAP package](https://formulae.brew.sh/formula/colmap), [Brush builds and CLI](https://github.com/ArthurBrussee/brush).

## Connect the workflow

1. **SP Project** creates the project folders.
2. **SP FFmpeg Setup → SP Media Import** estimates frames and imports photographs/video into `images`. `ffprobe` must be beside the selected `ffmpeg`. Use Refresh Estimate after changing a source folder's contents. Run and Cancel remain background operations.
3. **SP COLMAP Setup → Feature Extraction → Exhaustive Matching → Sparse Mapper** reconstructs the cameras and sparse points. Run each stage after the previous stage succeeds. On macOS extraction and matching use CPU options discovered from the installed COLMAP command help, supporting both legacy Sift and newer Feature option names.
4. Optionally run **Image Undistorter** to overwrite the active images and model together. Optionally use **Active Dataset → Camera Planes / Sparse Point Cloud → Geometry Translator → Align Model** to overwrite the current model with aligned points and camera poses. Both original and undistorted datasets support alignment.
5. Connect **Project Root → Active Dataset → Brush Dataset** and enable Prepare. This one handoff follows later successful updates. Images and model folders are symbolic links; keep the project on a filesystem supporting links. Separate Sparse and Undistorted overrides are retired. See [the active dataset workflow](Colmap-pipeline.md), including migration of older definitions.
6. **SP Brush Train** starts training and exports PLY files under `output/<job name>`. Start with Viewer=False and Rotate for Visualization=False. Cancellation retains existing checkpoints. Open Brush Viewer is optional and external to Rhino.

Show Terminal opens macOS Terminal following the on-disk log. If macOS prevents Terminal automation, use the component Log output or open the log manually; processing still continues.

For the full COLMAP sequence in one run, use **03 COLMAP → SP COLMAP Pipeline** (COLMAP All). Connect Exe and Root and press Run. Optional stage toggles, stage progress, terminal logs, Replace and Cancel are available. See [COLMAP Pipeline](Colmap-pipeline.md).

7. **SP Splat Preview** loads a Brush binary little-endian PLY as a colored Rhino point cloud. Connect the exported PLY file path to PLY; Max limits the sampled points (150,000 by default). Colors come from the splats' base color coefficients. Coordinates are preserved exactly as stored in the selected file. Lower Max for faster viewport interaction.

The Gaussian splat renderer is absent from this build. Point-cloud preview shows colored splat centers without Gaussian shape, transparency, or view-dependent shading. The existing optional PLY rotation is a file conversion only.

## Build on a Mac

Install the .NET 8 SDK and Rhino 8, then:

```sh
bash Scripts/Build-Mac.sh
```

The script restores the pinned 8.34 SDK from NuGet, builds `Source/RhinoSplat.Grasshopper.Mac.csproj`, and writes `dist/macos`. The first restore requires internet access. Build references are independent of the locally installed Rhino version. No Windows Desktop framework is required. The renderer project is not built.

## Verification

The portable checks cover active image/model replacement, undistortion followed by alignment, linked Brush updates, cancellation and background training. Real COLMAP BIN/TXT conversion is exercised on Windows. These do not verify native macOS execution.

Repeat the portable tests on the Mac:

```sh
dotnet run --project Tests/RhinoSplat.Platform.SmokeTest.csproj
dotnet run --project Tests/RhinoSplat.BrushDataset.SmokeTest.csproj
dotnet run --project Tests/RhinoSplat.BrushBackground.SmokeTest.csproj
```

Then run a small overlapping photo set through all seven stages above. Check camera/point-cloud previews, alignment, undistorted image/model pairing, Brush PLY export, colored splat point-cloud preview, cancellation, and optional live logs. Record Rhino, macOS, COLMAP and Brush versions before treating this candidate as validated for that machine.
