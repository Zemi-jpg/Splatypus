# Component icons

20 original component glyphs, including the hidden COLMAP Model Report. The supplied FFmpeg, COLMAP, and Brush logos informed the colours and tool motifs. These are component-specific interpretations, not official replacement logos.

- `svg/`: editable vector masters on a 24 × 24 grid.
- `png24/`: transparent production icons embedded by both Grasshopper projects.
- `png48/` and `png96/`: larger transparent exports for documentation and future UI use.
- `review/component-icons.png`: labelled contact sheet, with enlarged artwork and actual-size samples on light and dark backgrounds.
- `review/index.html`: locally viewable review sheet.
- `manifest.json`: component names and asset mapping.
- `generate.cjs`: reproducible source generator; run with Node.js and the `sharp` package (optionally pass the package path as the first argument).

## Visual system

Rounded strokes around 1.65 px; small optical adjustments per glyph. FFmpeg uses green with its folded diagonal motif. COLMAP uses slate facets and red feature points. Brush uses its blue handle, pale ferrule, and red paint tip. Shared project and transformation actions use teal. Each component has a distinct silhouette; colour is a secondary cue. Backgrounds remain transparent, including cutouts under action arrows and checkmarks.

The cached resource loader in `Source/Grasshopper/GH.ComponentIcons.cs` detaches each bitmap from its resource stream. All 20 component classes expose their assigned icon. The assembly uses Hello Splat as its provisional icon; no new plugin name has been chosen.

The artwork is embedded in the build and requires no external asset directory at runtime. No component identifiers or input/output definitions are changed. Native Grasshopper display, particularly on macOS, still requires a runtime visual check.
