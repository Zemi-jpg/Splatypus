# Splatypus for Windows — testing package

Requires Rhino 8.34 (8.34.26223.11001) or newer running .NET 8 on Windows. Built against the pinned 8.34 SDK so the package does not require the build machine's newer 8.35 SDK.

## Installation

1. Extract the ZIP. If Windows shows an Unblock checkbox in the ZIP's Properties, unblock it before extracting.
2. In Grasshopper, open File > Special Folders > Components Folder.
3. Close Rhino.
4. If upgrading from RhinoSplat, move its old plugin folder outside the Components Folder first. Splatypus retains the same component IDs, so both versions must not be installed together.
5. Create a Splatypus subfolder in that Components Folder and copy BOTH Splatypus.gha and Splatypus.Core.dll into it. Replace both files together when updating. Keep only one installed copy.
6. Restart Rhino and open Grasshopper. Look for the Splatypus tab and platypus icon.

## Tools and workflow

Use **01 Setup → SP Download Tools**. Leave Folder blank and connect a Button to Download. It installs pinned FFmpeg (including ffprobe), COLMAP and Brush in the background. Connect the FFmpeg, COLMAP and Brush path outputs to the later components' Exe inputs. Set CUDA=False for CPU COLMAP; CUDA=True requires an NVIDIA CUDA GPU. Ready is True when all three tools validate. Existing manual tool paths remain supported.

SP Project > Media Import > COLMAP reconstruction > optional Undistort > optional Align > Active Dataset > Brush Dataset > Brush Train.

Set Download Tools **Terminal=True** before pressing Download to view installation progress live. The **Log** output returns the saved log after completion or cancellation. Closing the terminal does not cancel the installation.

Each project has one current image/model pair. Undistortion replaces images and calibration together; alignment replaces the current points and camera poses. Connect **Project Root → Active Dataset → Brush Dataset**, enable Prepare, and keep that connection for every state. Separate Undistorted/Sparse inputs are retired. Use Buttons for background import/training Run and Cancel.

For one-run reconstruction, use **03 COLMAP → SP COLMAP Pipeline** (COLMAP All). Connect Exe and Root, then press Run. Undistort is optional and defaults to False; Extract, Match, Sparse and Export default to True. Progress, Terminal, Replace and Cancel remain available. See **Colmap-pipeline.md** for details and reconnecting older saved definitions.

SP Splat Preview loads a Brush binary little-endian PLY as a colored point cloud in Rhino. Its Max input defaults to 150,000 points; reduce it for faster viewport interaction. It preserves the coordinates stored in the selected file.

This ZIP contains the Grasshopper workflow and colored point-cloud preview. It does not include a separate Gaussian splat renderer or third-party Rhino rendering plugin.

## Validation

Release build completed with zero warnings and errors. Test on the recipient's Rhino installation with a small dataset before a large training job.
