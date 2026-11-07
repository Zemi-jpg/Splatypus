# Splat preview coordinates

SP Splat Preview now reads each PLY position as `(x, y, z)` without implicit axis conversion. Previously it used `(x, -z, -y)`, a reflection with determinant -1, despite claiming to preserve file coordinates.

The explicit Rotate for Visualization exporter remains separate: it writes a separate _rgs.ply for RhinoGaussianSplatting using the proper +90-degree X rotation `(x, -z, y)`. Previewing that file now displays its stored coordinates without converting it again. Previewing the original Brush file displays its original coordinates.

This build includes the background Brush training update and Cancel input. Neither existing PLY files nor their saved coordinates are modified by the preview fix. Reload/recompute the preview after restarting Rhino with the updated plugin.
