# Undistortion in the active dataset workflow

Run **SP COLMAP Undistort Images** after reconstruction. It replaces the project's active images and camera model together. You may then align that corrected model with **Align Model**.

Keep one connection: **Project Root → Active Dataset → Brush Dataset → Brush Train**. Enable Brush Dataset's Prepare input. There is no separate Dense/Undistorted output or Sparse override to select. The Brush handoff follows the active dataset automatically.

See [COLMAP and active dataset workflow](Colmap-pipeline.md) for overwrite behavior, controls, migration and verification.

