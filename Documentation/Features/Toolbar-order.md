# Splatypus toolbar order

All visible components use the same Grasshopper exposure level so each category lists components alphabetically by full display name. Category numbers retain their existing order.

| Group | Components in order |
| --- | --- |
| 01 Setup | SP Download Tools; SP Hello; SP Project |
| 02 dataset | SP FFmpeg Setup; SP Media Import |
| 03 COLMAP | SP COLMAP Export Model; SP COLMAP Feature Extract; SP COLMAP Match; SP COLMAP Pipeline; SP COLMAP Setup; SP COLMAP Sparse Reconstruct; SP COLMAP Undistort Images |
| 04 Realignment | SP Align COLMAP Model; SP Camera Planes; SP Geometry Translator; SP Quick Align; SP Sparse Point Cloud |
| 05 Training | SP Active Dataset; SP Brush Dataset; SP Brush Train |
| 06 Preview | SP Auto Background; SP Splat Preview; SP Video Frames; SP Video Playback; SP View Capture |

SP COLMAP Model Report is hidden from the interface. Component IDs are retained. The active-dataset update removes retired model-output and override sockets; reconnect older definitions as described in [the workflow guide](Colmap-pipeline.md).

Install both plugin binaries with Rhino closed and restart Rhino. Grasshopper may collapse icons when the toolbar is narrow; open the full category menu to see the alphabetical list. Quick Align has a dedicated three-point mapping icon, distinct from Align COLMAP Model.
