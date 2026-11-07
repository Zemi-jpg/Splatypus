# SP Download Tools

Find **SP Download Tools** in **Splatypus → 01 Setup**, after SP Project. Connect a Button to **Download**. Downloads, extraction, package installation, and executable checks run outside Grasshopper's solution so Rhino remains responsive.

| Input | Use |
| --- | --- |
| Folder | Leave blank for this machine's per-user `RhinoSplat/Tools` folder, or choose a writable folder. No administrator access is required. |
| CUDA | Windows: True for NVIDIA CUDA; False for CPU COLMAP. macOS always uses CPU COLMAP. This does not change Brush's graphics requirements. |
| Download | A Button or False-to-True toggle starts installation. Valid existing installs are reused. |
| Cancel | Stops the current download/process; completed tools remain available. Press Download again to resume. |
| Terminal | Set True **before** pressing Download to open a live progress terminal (PowerShell on Windows, Terminal on macOS). Changing this preference does not cancel an active installation; it applies to the next Download. |

| Output | Connect to |
| --- | --- |
| FFmpeg | SP FFmpeg Setup → Exe, or SP Media Import → Exe |
| COLMAP | Exe on COLMAP Setup, Feature Extract, Exhaustive Match, Sparse Reconstruct, Undistort, Export, and other COLMAP components |
| Brush | SP Brush Train → Exe |
| Ready | True only when all three tools have passed validation |
| Versions | Selected pinned versions and COLMAP CPU/CUDA variant |
| Status | Progress, completion, missing tools, or installation errors |
| Log | Path to the full log when an installation attempt finishes or is cancelled |

Opening a definition checks previously installed tools without downloading. Paths are rediscovered for the current Folder and platform; they are empty until validated. A failed tool does not erase the paths of other successfully checked tools. Changing Folder/CUDA cancels old work and clears stale outputs. Ordinary recomputes do not repeat downloads or process checks.

Each Download creates a separate immediately flushed log in `Tools Folder/logs`, including download counts, checksum checks, extraction, macOS package-manager output, validation results and errors. Closing the terminal does not stop installation; use Cancel in Grasshopper. The terminal stays open after completion for inspection. If the OS prevents opening a terminal (including macOS Terminal automation permissions), processing continues and the saved log remains available. Existing component input/output connections retain their positions; Terminal and Log are appended.

## Versions and sources

| Platform | FFmpeg + ffprobe | COLMAP | Brush |
| --- | --- | --- | --- |
| Windows x64 | 8.0 essentials | 3.11.1 CUDA or CPU | 0.3.0 |
| Apple Silicon macOS | 7.1.1 | 3.11.1 CPU | 0.3.0 |

Windows uses the [Gyan FFmpeg release](https://github.com/GyanD/codexffmpeg/releases/tag/8.0), [COLMAP release](https://github.com/colmap/colmap/releases/tag/3.11.1), and [Brush release](https://github.com/ArthurBrussee/brush/releases/tag/v0.3.0). The full archives are retained on extraction, including supporting libraries and license files; the downloaded archive itself is removed. SHA-256 checksums are pinned in `Source/Core/Core.ToolDownloadCatalog.cs`. FFmpeg includes matching ffprobe for image-count estimates. COLMAP 3.11.1 keeps the camera/model and command conventions used by this workflow.

macOS downloads the official Apple Silicon Brush archive. A checksum-pinned [micromamba 2.3.3](https://github.com/mamba-org/micromamba-releases/releases/tag/2.3.3-0) installs FFmpeg and COLMAP into separate private environments using only [conda-forge](https://conda-forge.org/). Their top-level versions are pinned; compatible dependency builds are resolved at install time. No Homebrew, shell initialization, administrator access, or system PATH changes are needed. The native package solver checks OS requirements. Do not move installed folders: conda environments contain absolute paths.

**Intel macOS and Windows ARM are not supported by this automatic installer.** Brush 0.3.0 publishes an Apple Silicon Mac binary only. Existing components still accept manually installed native tool paths on Intel Macs. Brush training requires a compatible graphics device; tool startup checks do not certify GPU training or NVIDIA driver compatibility.

## Install and recovery

Installations live in unique version/platform directories under Folder, with a small JSON receipt published after validation. Receipts record installed files and their sizes. Missing or incomplete installs are repaired into new directories; earlier complete directories are left intact. Concurrent components serialize writes to the same folder. Unfinished attempts are cleaned up on ordinary failure/cancellation. If Rhino crashes, unreferenced installation directories can remain and are not reused.

Downloads require HTTPS access to GitHub and its release CDN; Mac installs also need conda-forge. Failure details appear in Status. To retry, release the Download toggle and press it again. A folder permission error can be resolved by selecting a writable Folder.

## Build and verification

Both distribution projects use `RhinoSplat.Sdk.props` to pin McNeel's RhinoCommon and Grasshopper SDK packages to 8.34.26223.11001. This avoids the newer-SDK warning on Rhino 8.34 when packages are built on a machine with Rhino 8.35. Rhino 8.34 or newer must run .NET 8. SDK assemblies are compile-time references and are not included in either ZIP; Rhino supplies them at runtime. The first package restore needs NuGet access.

- Build Windows: `dotnet build Source/RhinoSplat.Grasshopper.csproj -c Release -o dist/windows`.
- Build on a Mac: `bash Scripts/Build-Mac.sh`.
- Install Windows with Rhino closed: `powershell -NoProfile -ExecutionPolicy Bypass -File Scripts/Install-DownloadTools.ps1`.
- Install Mac: copy both files from `dist/macos` to the Grasshopper Libraries/Splatypus folder, or use `Scripts/Install-Mac.sh`.
- Offline behavior tests: `dotnet run --project Tests/RhinoSplat.DownloadTools.SmokeTest.csproj`.
- Windows live package validation: run `Tests/FetchToolFixtures.ps1`, then the smoke test with `--live` and the absolute path of `bin/DownloadToolsVerification`.

Both Grasshopper targets compile on Windows. Tests cover reuse, missing dependencies, repair, checksum failure, traversal, partial network failure, cancellation, concurrency, input changes, and trigger behavior. The actual Windows FFmpeg/ffprobe, both COLMAP variants, and Brush execute successfully; Brush help exposes all options required by the training component. Mac dependency solves were checked for macOS 14 / Apple Silicon and the Brush archive hash/layout was verified. Native macOS installation and execution still need testing on a Mac; a build or dependency solve does not establish native runtime compatibility.
