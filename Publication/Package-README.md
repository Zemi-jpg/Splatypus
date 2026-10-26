# Splatypus

Photogrammetry and Gaussian splat training workflows for Grasshopper in Rhino 8. Splatypus connects FFmpeg media import, COLMAP camera reconstruction, alignment, Brush training and colored point-cloud preview.

**Author:** Zeming Li · **Support:** [lizeming2021@gmail.com](mailto:lizeming2021@gmail.com) · **Website:** [zeming.li](https://zeming.li/) · **License:** MIT

This README describes the first public release, **0.1.0**, and is the complete package guide. The [quick-start PDF](Splatypus-Quick-Start.pdf) provides a shorter walkthrough.

- [Requirements](#requirements)
- [Installation](#installation)
- [Example and first workflow](#example-and-first-workflow)
- [External tools](#external-tools)
- [Media import](#media-import)
- [COLMAP and the active dataset](#colmap-and-the-active-dataset)
- [Alignment and geometry](#alignment-and-geometry)
- [Training and preview](#training-and-preview)
- [Troubleshooting](#troubleshooting)
- [Privacy and local data](#privacy-and-local-data)
- [Licenses and credits](#licenses-and-credits)

## Requirements

- **Rhino 8.34.26223.11001 or later**, with Grasshopper and **.NET 8**. Rhino 7 and .NET Framework mode are unsupported. If necessary, use Rhino's `SetDotNetRuntime` command to select .NET Core, then restart.
- **Windows x64** is the primary release target. A separate macOS build is provided. Automatic Mac tool installation targets **Apple Silicon**; automatic installation does not support Intel Macs or Windows ARM.
- FFmpeg with matching ffprobe, COLMAP and Brush are separate local applications. Use SP Download Tools or provide their executable paths. Brush needs a compatible graphics device; CPU COLMAP does not make Brush a CPU trainer.

## Installation

### Package Manager

Run `_PackageManager` in Rhino, search **Splatypus**, install and restart Rhino. Open Grasshopper, find the Splatypus tab and place **SP Hello**. Connect Status to a Panel; the expected result is `SP is loaded: Ready`.

Yak packages use Rhino's package directory. On Windows, press Win + R and open:

```text
%APPDATA%\McNeel\Rhinoceros\packages\8.0\Splatypus
```

The version subfolder contains the plugin, this README, the PDF and the Examples folder. It is separate from Grasshopper's usual Components Folder. Use either Yak or a manual installation, and keep one installed copy.

### Local Yak package

Close Rhino. Open PowerShell in the folder containing the Windows package and run:

```powershell
& "C:\Program Files\Rhino 8\System\Yak.exe" install ".\splatypus-0.1.0-rh8_34-win.yak"
```

Restart Rhino. For macOS, use the matching `mac.yak` and Rhino's Yak tool at `/Applications/Rhino 8.app/Contents/Resources/bin/yak`. Local installation does not publish a package.

### Manual ZIP and uninstalling

Unblock the downloaded ZIP in Windows Properties if that option is present, then extract. In Grasshopper, open **File > Special Folders > Components Folder**. Close Rhino and copy both `Splatypus.gha` and `Splatypus.Core.dll` into a Splatypus subfolder there. Keep the README, PDF and Examples alongside them. Use the build matching your platform, and restart Rhino.

Install both libraries together. Uninstall Yak packages through Package Manager, then restart Rhino. Save your working definition outside the installed package folder. Uninstalling leaves project data and downloaded tools on disk.

## Example and first workflow

Open [Examples/Splatypus_Workflow.gh](Examples/Splatypus_Workflow.gh) after installing the plugin. Disable the Grasshopper solver while checking its saved settings. The example's local Parent Folder is blank; set your own project folder, media source, executable paths and output locations. Review Create, Download, Run and Replace inputs before enabling the solver. Save a working copy outside the installed package folder.

The example contains the current workflow components and connections. Photographs, video, trained models and external tools are not bundled.

1. **SP Hello:** check that the plugin loads.
2. **SP Project:** choose a writable Parent Folder and Project Name; enable Create. Connect its Root to downstream components.
3. **SP Download Tools:** install tools or use your own executable paths.
4. **SP Media Import:** import photographs/video and inspect the images.
5. **SP COLMAP Pipeline:** reconstruct cameras and sparse points. Alternatively, use the individual COLMAP stages.
6. Optionally undistort the images and align the model.
7. Connect **Root -> SP Active Dataset -> SP Brush Dataset**; enable Prepare.
8. **SP Brush Train:** start with a short training test and a new Job Name.
9. **SP Splat Preview:** inspect an exported PLY as colored points.

Use Buttons for Run and Cancel, or reset a held toggle to False before starting another job. Do not run two reconstruction sequences simultaneously on the same project, or change its dataset during training.

## External tools

SP Download Tools is under **01 Setup**. Leave Folder blank for the default per-user tools folder, or choose a writable folder. On Windows, CUDA=False selects CPU COLMAP; CUDA=True selects the NVIDIA CUDA variant. Press Download and wait for Ready=True. Connect the returned FFmpeg, COLMAP and Brush paths to the later Exe inputs. Startup checks do not certify GPU training.

| Platform | FFmpeg + ffprobe | COLMAP | Brush |
| --- | --- | --- | --- |
| Windows x64 | 8.0 essentials | 3.11.1 CPU/CUDA | 0.3.0 |
| Apple Silicon macOS | 7.1.1 | 3.11.1 CPU | 0.3.0 |

Opening a definition discovers existing tools without starting a download. Valid installs are reused; missing or incomplete installs can be repaired by pressing Download again. Changing Folder or CUDA cancels an active tool installation and refreshes the paths. Select a writable Folder if permissions prevent installation.

Set Terminal=True before Download for live progress. The saved log is under `Tools Folder/logs`; closing the terminal does not cancel installation. Use Cancel. On macOS, terminal automation may require permission; processing continues even if a terminal cannot open.

Downloads use GitHub and its release CDN. Apple Silicon additionally uses checksum-pinned micromamba 2.3.3 and private conda-forge environments. No system PATH or shell initialization changes are needed. Do not move installed Mac environments because they contain absolute paths. Unsupported automatic-install platforms can use manually supplied native executables where suitable tool builds exist.

## Media import

Connect FFmpeg Exe and Root to SP Media Import. Choose Media Sources and FPS. ffprobe must be beside FFmpeg. With Run=False, Estimated JPEGs reports approximately one image per still photograph plus video duration multiplied by FPS. Actual counts can differ at clip boundaries; Count reports the completed import's actual output.

The estimate updates automatically when FPS, Media Sources, FFmpeg Exe or Project Root changes. If files inside the source folder change, refresh the estimate using the component's right-click menu on Windows, or its Refresh input on macOS. On Windows, choose **Refresh media estimate**; on macOS, connect a Button to **Refresh Estimate**. This refresh updates the displayed estimate; Run reads the current source files without requiring it. An unavailable estimate does not prevent an explicit import. Import runs in the background; input changes affect the next run. Cancel retains images already written. Status and Log show progress; the complete log is `ffmpeg-import.log` in the project folder.

Use overlapping, sharp photographs from varied viewpoints. Motion, reflections and textureless surfaces are difficult. Start with a small capture; exhaustive matching becomes expensive as the image count grows. Keep original media backed up outside the project.

## COLMAP and the active dataset

SP COLMAP Pipeline, under **03 COLMAP**, runs Extract, Match, Sparse and Export by default. Undistort defaults to False. Connect COLMAP Exe and Root, then press Run. Disable stages only when their prerequisites already exist. Replace permits overwriting an existing sparse reconstruction. Progress reports selected stages completed; Terminal follows `colmap/pipeline.log`. Cancel stops the current process and skips later stages; earlier successful stages remain committed.

For inspection between stages, run Feature Extract, Exhaustive Match, Sparse Reconstruct and Export separately. Use Model Report, Camera Planes and Sparse Point Cloud to inspect the result. Sparse reconstruction has no automatic real-world scale.

Each project has one active base image/model pair. **Undistort Images** replaces both images and camera calibration together, refreshes the text model and clears stale feature data. COLMAP can omit unregistered images. Importing new media invalidates the previous reconstruction; reconstruct again before training.

Keep **Root -> Active Dataset -> Brush Dataset** connected. Active Dataset refreshes after project operations; use Update after external edits. Re-prepare Brush Dataset after changing reconstruction, undistortion or its optional aligned override. Failed or cancelled replacements aim to retain the prior valid data; maintain independent backups.

| Project data | Location under Root |
| --- | --- |
| Active images | `images` |
| Base camera model and points | `colmap/sparse/0` |
| Base readable text model | `colmap/sparse-text` |
| Separate aligned model | `colmap/aligned/sparse/0` |
| Aligned text model | `colmap/aligned/sparse-text` |
| Linked Brush dataset | `brush-dataset/images` and `brush-dataset/sparse` |
| Training exports and log | `output/<job name>` |

## Alignment and geometry

SP Geometry Translator copies input geometry and applies translation, world-axis X/Y/Z rotation, then uniform scale. Angles are degrees; rotation and scale use the moved centre. Connect its ordered **Transforms** output to **SP Align COLMAP Model > Xforms**. Use positive scale for alignment.

One model needs one transform sequence. Identical branches are used once; different sequences produce an error. Select the intended branch rather than flattening duplicate sequences. Release any Data Dam before pressing Write.

Alignment reads the base model and writes a **separate** model with transformed points and camera poses, preserving intrinsics. Repeated writes start from the base, so transformations do not accumulate. Keep the source previews connected to the base; preview **Aligned Text Model** separately. Connect **Aligned Sparse Model** to Brush Dataset's **Sparse Model Override** to train that branch. Write alignment again after reconstruction or undistortion changes the base.

Camera-plane Size affects display rectangles, not saved camera poses. Compare points and camera centres when checking alignment.

## Training and preview

SP Brush Dataset prepares links to the active images and model; leave Sparse Model Override empty for the base model. Links require their targets to remain available and a suitable filesystem. Windows can fall back to directory junctions. Moving a project or deleting linked data can break the handoff.

Connect Brush Exe, Dataset and Root to SP Brush Train. Choose a new Job Name to retain earlier runs. The default is 30,000 steps; begin with a short test. Viewer=False avoids opening Brush's additional viewer. **Replace=True deletes that job's previous exports before training.**

Training runs in the background, but still shares graphics resources with Rhino. Input changes affect the next run. Cancel stops the owned process; completed checkpoints can remain. Closing the definition or removing the component cancels its job. Read Status, Code and the full `brush-training.log` under `output/<job name>`. Panels show a bounded log preview.

SP Splat Preview reads binary little-endian PLY files as colored point clouds. Max defaults to 150,000 points; lower it for smoother interaction. Stored coordinates are preserved. Gaussian shape, transparency and view-dependent shading are not rendered.

Rotate for Visualization optionally writes an additional `_rgs.ply`, rotated +90 degrees around X, for a compatible external RhinoGaussianSplatting workflow. The original PLY remains available. Previewing the rotated file does not apply another rotation. No third-party Rhino renderer is bundled.

## Troubleshooting

| Problem | What to check |
| --- | --- |
| No Splatypus tab | Restart Rhino; verify Rhino 8.34+, .NET 8, the correct platform and both plugin libraries. Read Grasshopper's loading errors. |
| Duplicate components | Use one installation method and keep one installed copy. Check both Yak and manual installation folders for duplicate files. |
| Package absent from search | Check internet access and refresh the Package Manager search. Local Yak installation is also available. |
| Run does nothing | Reset the toggle to False, then use a Button or False-to-True change. |
| Tool download fails | Read Status/Log; check Folder permissions and HTTPS access. Apple Silicon also needs conda-forge. |
| ffprobe missing | Keep matching ffprobe beside the selected FFmpeg. |
| CUDA error | Use CPU COLMAP or check the NVIDIA driver and CUDA build. This does not change Brush requirements. |
| Few reconstructed cameras | Check overlap, sharpness, texture and scene movement; inspect COLMAP logs. |
| Dataset links fail | Check writable folders, filesystem link support and whether the target files still exist. |
| Brush fails or runs out of memory | Check GPU/driver compatibility, read the training log, reduce workload and try Viewer=False. |
| Wrong scale or orientation | Inspect the base points/cameras, align using a reference and prepare the intended dataset again. |
| Slow point preview | Lower Max points. |

Set Terminal=True before a job for live output where supported. Closing that terminal does not cancel the process; use Cancel. For support, include Splatypus/Rhino/.NET/OS versions, GPU and tool versions, settings, Status/Code, a short reproduction and a redacted log excerpt.

## Privacy and local data

Splatypus launches local FFmpeg, COLMAP and Brush processes and reads/writes the selected folders. The inspected plugin code has no publisher telemetry, account login or automatic upload of images, models, definitions or logs. Downloads occur when you trigger Download Tools; upstream providers receive normal network information such as your IP address. Rhino, Food4Rhino and external tools have their own policies.

Projects, exports, tool receipts and logs remain on disk without an automatic retention period. Replace operations can overwrite project data or job exports; keep backups. If you choose to send a support report, remove private paths, usernames, filenames and scene information from logs, and share only media you have permission to distribute.

## Licenses and credits

**Copyright (c) 2026 Zeming Li.** Splatypus and the included example are distributed under the [MIT License](LICENSE.txt). Publisher/contact details are also in [NOTICE.txt](NOTICE.txt).

External tools are downloaded separately and retain their own licenses and supporting notices:

| Software | License and source |
| --- | --- |
| FFmpeg + ffprobe | LGPL-2.1-or-later, with optional GPL parts affecting the selected build. The exact archive's configuration/licenses govern. [Licensing](https://ffmpeg.org/legal.html), [source](https://ffmpeg.org/download.html#get-sources), [Windows 8.0 build](https://github.com/GyanD/codexffmpeg/releases/tag/8.0). |
| COLMAP 3.11.1 | BSD-3-Clause; copyright 2023 ETH Zurich and UNC Chapel Hill. Dependencies are separately licensed. [License](https://github.com/colmap/colmap/blob/3.11.1/COPYING.txt), [tagged source](https://github.com/colmap/colmap/tree/3.11.1). |
| Brush 0.3.0 | Apache-2.0; dependencies retain their own licenses. [License](https://github.com/ArthurBrussee/brush/blob/v0.3.0/LICENSE), [tagged source](https://github.com/ArthurBrussee/brush/tree/v0.3.0). |
| micromamba 2.3.3 | mamba project BSD-3-Clause; shipped components/dependencies have their own notices. [License](https://github.com/mamba-org/mamba/blob/main/LICENSE), [release](https://github.com/mamba-org/micromamba-releases/releases/tag/2.3.3-0). |
| conda-forge environments | Resolved package metadata and license files govern the exact dependency builds. [FFmpeg recipe](https://github.com/conda-forge/ffmpeg-feedstock), [COLMAP recipe](https://github.com/conda-forge/colmap-feedstock). |

Splatypus launches these applications as separate processes and does not relicense them. External binaries, Rhino SDK assemblies and .NET runtime files are not bundled. Rhino supplies the host assemblies at runtime. Compile-time references are Grasshopper/RhinoCommon 8.34.26223.11001 and System.Drawing.Common 8.0.0; these references do not grant redistribution rights to host software. Downloaded tools retain their extracted license files. Redistributing their binaries requires checking the exact build's license, notices and any corresponding-source obligations.

The listing artwork uses Splatypus's platypus mark and component icons. No third-party scene images or official reference logos are selected for publication. Rhino, Rhinoceros, Grasshopper and Food4Rhino identify McNeel products/services; FFmpeg is a trademark of Fabrice Bellard. Other names and marks belong to their owners. References describe interoperability and imply no endorsement or partnership.

Support: [lizeming2021@gmail.com](mailto:lizeming2021@gmail.com). Website: [zeming.li](https://zeming.li/).
