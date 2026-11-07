# COLMAP mesh reconstruction

Find **SP COLMAP Mesh Reconstruction** (nickname **SP Mesh**) under **Splatypus → 03 COLMAP**.

Connect the COLMAP executable and Project Root / Active Dataset after sparse reconstruction, then press **Run**. The **Mesh** output previews directly in Grasshopper and can be baked into Rhino. This is a photogrammetric surface reconstructed from photographs and cameras; it does not mesh trained Gaussian splats.

## Methods

| Method | Surface reconstruction | Requirements |
| --- | --- | --- |
| 0 — Dense Poisson (default) | Undistort → depth maps → fusion → Poisson | CUDA-enabled COLMAP and an NVIDIA GPU |
| 1 — Dense Delaunay | Undistort → depth maps → fusion → Delaunay | CUDA plus COLMAP built with CGAL |
| 2 — Sparse Delaunay | Mesh sparse reconstruction directly | COLMAP built with CGAL; no CUDA required, much less detail |

Poisson suits clean, dense point clouds. Delaunay tolerates more outliers but generally produces less smooth surfaces. Sparse mode is an option for the current CPU/macOS setup if that COLMAP build includes CGAL.

This follows [COLMAP's documented command sequence](https://colmap.github.io/cli.html) and the [3.11.1 command implementation](https://github.com/colmap/colmap/blob/3.11.1/src/colmap/exe/mvs.cc), matching Download Tools. Current COLMAP documents a newer `mesh_simplifier`; the component uses [Rhino's native reducer](https://developer.rhino3d.com/api/rhinocommon/rhino.geometry.mesh/reduce) so optimization also works with the existing 3.11.1 installation.

## Settings

| Input | Default | Effect |
| --- | --- | --- |
| Target Faces | 100,000 | Desired triangle count. 0 retains all faces. Actual count and any shortfall are reported. |
| Max Image Size | 1,600 | Longest image side for dense reconstruction. Lower values save GPU memory and reduce detail. Range 320–8,192. |
| Poisson Depth | 10 | Higher values retain finer detail and require much more memory. Range 6–14; Poisson only. |
| Poisson Trim | 10 | Raise to remove more low-density surface; lower if too much surface disappears. Poisson only. |
| Reduction Accuracy | 5 | Range 1–10. Higher values take longer for more accurate reduction. |
| Refine Only | False | Reduce the saved raw mesh again; skip reconstruction. |
| Sparse Model Override | Empty | Optional Aligned Sparse Model from Align Model; otherwise uses the base model. |
| Show Terminal | False | Open the live command log. |
| Cancel | False | Stop the current operation. |

100,000 faces is a starting point for interactive Rhino geometry, not a universal performance guarantee. The target may not be reached while preserving appearance. Reducing final faces does not reduce the resources needed to build the original mesh; use Image Size and Depth for that.

To tune a result, enable **Refine Only**, change **Target Faces** or **Accuracy**, reset Run to False, then press Run. Every refinement starts from the original mesh, avoiding cumulative reduction. Exe is unnecessary in this mode. Other reconstruction settings and the current camera model are ignored: after changing photographs or alignment, disable Refine Only and reconstruct again.

## Results

- **Mesh**: reduced Rhino geometry with vertex colors when present. Texture-atlas generation is not included.
- **File**: `colmap/mesh/mesh.ply`, matching the reduced output.
- **Raw**: `colmap/mesh/raw.ply`, retaining full detail.
- **Before / After**: actual triangle counts.
- **Progress**: completed stages, not a duration estimate.
- **Log / Status / Code**: diagnostics. Live output: `colmap/mesh-reconstruction.log`.

Dense images, depth maps and fused points are retained inside `colmap/mesh`. Reconstruction stages separate files and preserves the active images/cameras. Success replaces the previous mesh workspace; refinement replaces only `mesh.ply`. Failure/cancellation retains prior results, with recovery details if filesystem rollback itself fails. Poisson's temporary files use the writable staging workspace rather than the tool installation.

Coordinates and scale match the selected sparse model, without automatic rotation or unit conversion. Use the aligned override for the same coordinates as the aligned sparse preview; it must reference matching images. Standard Grasshopper components can also transform the output.

Jobs run in the background and capture settings at start. A held True does not restart them. Reopening a saved True requires resetting it to False. Project operations share the dataset lock. Removing the component or closing its document cancels its job.

## Validation

`Tests/RhinoSplat.ColmapMesh.SmokeTest.csproj` covers commands with a fake executable, reduction handoff, refinement, errors, cancellation, locking, overrides, cleanup, validation, PLY formats and background sessions. Its `--real-poisson <colmap-exe> <output-folder>` mode runs actual COLMAP on an oriented sphere. `Tests/RhinoSplat.MeshGeometry.SmokeTest.csproj` checks Rhino conversion and reduction against that PLY.

Full dense reconstruction on representative photographs and native macOS execution still need hands-on validation.

Development validation on Windows: 35 mesh checks and 35 existing dataset checks passed. Real COLMAP 3.11.1 generated a 32,116-triangle sphere, and Rhino reduced it to 1,000 triangles on a background thread while preserving colors, position and scale. Both platform builds compile against the pinned Rhino 8.34 SDK.
