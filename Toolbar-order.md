# Splatypus toolbar order

The components use increasing Grasshopper exposure levels within each group to preserve the requested workflow order, rather than alphabetical ordering.

| Group | Components in order |
| --- | --- |
| 01 Setup | SP Hello; SP Project; SP Download Tools |
| 02 dataset | SP FFmpeg Setup; SP Media Import |
| 03 COLMAP | SP COLMAP Pipeline; SP COLMAP Setup; SP COLMAP Feature Extract; SP COLMAP Exhaustive Match; SP COLMAP Sparse Reconstruct; SP COLMAP Undistort Images; SP COLMAP Export Model |
| 04 Realignment | SP Sparse Point Cloud; SP Camera Planes; SP Geometry Translator; SP Align COLMAP Model |
| 05 Training | SP Active Dataset; SP Brush Dataset; SP Brush Train |
| 06 Preview | SP Splat Preview |

SP COLMAP Model Report is hidden from the interface. Component IDs are retained. The active-dataset update removes retired model-output and override sockets; reconnect older definitions as described in [the workflow guide](Colmap-pipeline.md).

Install both plugin binaries with Rhino closed and restart Rhino. Grasshopper may separate exposure levels with dividers and collapse icons when the toolbar is narrow; the full panel retains the workflow order.
