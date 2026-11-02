# Splatypus component icons

The active set of 26 component icons for Splatypus, selected from the earlier Plan B concept and extended with COLMAP Pipeline. Both Windows and macOS plugin builds embed the active PNGs in `png24/`, excluding the retained experimental mesh icon. The platypus mark is also used for the assembly and Grasshopper tab. The original Plan A artwork and earlier concept ZIP remain archived separately.

## Design

The existing tool references remain visible: green folded diagonals for FFmpeg, grey aperture facets and red feature points for COLMAP, and a blue handle with a red paint tip for Brush.

Three abstract platypus features tie the alternative together:

- **Flattened bill:** folder/document bases, import/export forms, and tool setup marks.
- **Paddle tail:** feature and point-cloud silhouettes, geometry, and the broad Brush handle.
- **Webbed foot:** triangulated reconstruction, camera rays, alignment, and the download tray.

The Hello Splatypus icon and the additional brand mark combine these features into a simplified platypus silhouette. The three dots above it retain the tool-family colours. Each other icon prioritizes its component action with a smaller anatomical reference. The review sheets, manifest, and generator retain the original concept labels; the plugin now uses Splatypus component names.

## Files

- `svg/`: the 24 active editable masters on a 24 × 24 grid.
- `png24/`, `png48/`, `png96/`: transparent PNG exports.
- `brand/`: an additional SVG and 256-pixel PNG of the proposed Splatypus mark.
- `review/splatypus-components.png`: full labelled set, including native-size light/dark previews.
- `review/plan-a-vs-b.png`: all 20 original and alternative icons side by side.
- `review/index.html`: both sheets in a local browser page.
- `manifest.json`: stable asset IDs, current component names, and motif descriptions.
- `generate.cjs`: repeatable vector and export generator. Run with Node.js and Sharp, passing the Sharp module path if needed.
- `generate-mesh.cjs` and mesh artwork are retained development assets excluded from the current builds and publication set.
- `generate-pipeline.cjs`: the COLMAP Pipeline extension, also called by the full generator. Its simplified front-facing platypus head combines a broad bill and two eyes with COLMAP aperture facets and a red center. See `review/colmap-pipeline.png`; the historical comparison sheets retain the original 20 icons.

The generator reads the original set for the comparison sheet; all output stays in this Plan B folder. It does not change the original set or the plugin. Artwork follows the supplied tool references and is an original interpretation, not an official logo replacement.

- `generate-video.cjs`: SP Video Frames, SP Video Playback, and SP View Capture extension, also called by the full generator. Editable SVG masters and transparent 24 / 48 / 96 px PNGs; review at `review/video-components.png` with native-size light/dark samples.
