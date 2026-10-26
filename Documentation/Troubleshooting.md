# Troubleshooting and support

| Symptom | Check |
| --- | --- |
| No Splatypus tab | Restart Rhino; check Rhino 8.34+, .NET 8, the correct platform GHA, and that Splatypus.Core.dll is beside it. Check Grasshopper's loading errors. |
| Duplicate component or assembly errors | Use one installation method and keep one installed copy. Check both Yak and manual installation folders for duplicates. |
| Package not listed | Publication must have happened first. Enable Include pre-releases for beta versions. Check Rhino/service release compatibility and network access. |
| Tool downloads fail | Read Status and Log; verify writable Folder and HTTPS access to GitHub/release CDN. Apple Silicon also needs conda-forge. Retry with a Button after resetting Download. |
| CUDA error in sparse reconstruction | Use the CPU COLMAP variant with CUDA=False, or install a suitable NVIDIA driver/tool configuration. CUDA=False does not make Brush a CPU trainer. |
| ffprobe not found | Use matching ffprobe beside the chosen ffmpeg; Download Tools supplies both. |
| No cameras or very few matches | Inspect image overlap, sharpness, scene movement and texture. Review COLMAP logs; avoid hundreds of redundant frames for the first test. |
| Brush Dataset link error | Keep targets available and use a writable local filesystem supporting directory links. Windows falls back to junctions if symbolic links are unavailable. Read the component error for path/permission details. |
| Brush exits or GPU memory is exhausted | Verify GPU compatibility with the pinned Brush build, inspect training log, reduce the input workload and try Viewer=False. |
| A second Run does nothing | Reset the toggle to False and then True, or use a Button. Reopening definitions with saved True can require resetting Run. |
| Wrong orientation or scale | Reconstruction is not automatically scaled to Rhino units. Inspect points/cameras, align the active model and prepare the Brush dataset again. Avoid applying an external renderer rotation twice. |
| Slow preview | Reduce Max points; splat preview is a colored point cloud. |

## Report a problem

Contact **Zeming Li** at [lizeming2021@gmail.com](mailto:lizeming2021@gmail.com). Project website: [zeming.li](https://zeming.li/).

Include the Splatypus version, Rhino version and .NET runtime, OS and CPU architecture, GPU/driver, tool versions, component and settings, a short reproduction, Status/Code, and relevant log excerpt. Mention manual or Package Manager installation. A minimal definition with private paths removed is useful.

Logs can contain absolute paths, usernames, filenames and command arguments. Review/redact them before sharing. Share a small dataset only if you have permission. Do not send full private project folders by default.

For macOS reports, include the processor architecture and whether the problem occurs during loading, tool installation, reconstruction or training. Package verification is separate from native workflow testing; see the publication validation report.
