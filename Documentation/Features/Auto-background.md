# Automatic splat backdrop (0.2.0)

**SP Auto Background**, in **Splatypus → 06 Preview**, estimates background colors from a Gaussian-splat PLY. It helps transparent scan boundaries blend with a backdrop while native Rhino geometry remains in Rendered mode. It changes the viewport fill only; it does not add lights, modify the environment used for lighting, or edit the splat data.

## Connect it

1. Connect the PLY displayed by your splat renderer to **Splat PLY**. Use the coordinate-matched `_rgs.ply` when appropriate. If you moved, rotated or scaled the imported splat object, supply that same **Transform**.
2. Connect the same **Frames** used by SP View Capture. Match **Lens** (35 mm by default) and **Aspect** (capture Width / Height, 1.5 by default).
3. For a gradient, connect **Bottom → View Capture Background** and **Top → View Capture Background Top**. For a solid fill, connect **BG → Background** and leave Background Top unconnected.
4. Set **Preview=True** to see the gradient in the active Rhino viewport. Keep that viewport in Rendered mode and keep the external splat renderer's relighting off. Preview uses a temporary copy of its display mode, changing only the background fill; the original display-mode definition remains untouched. Preview=False restores the normal mode. Preview also stops when the component is hidden, locked or removed, or its GH document is inactive. Temporary modes are hidden from menus and removed afterwards; startup removes abandoned generated background modes.
5. Capture a small batch and inspect its boundaries before capturing the full sequence.

Without Frames, the component samples the active viewport when it solves. Recompute after changing that camera. With Frames, it fits one stable pair of colors across up to 64 evenly spaced cameras to avoid changing background color from image to image. Live preview uses that same fixed gradient; it does not continuously resample while orbiting.

During SP View Capture, the live-preview override yields to the capture component's background inputs. Wire the colors explicitly so saved images use the intended backdrop. Input values remain fixed for each capture batch.

## How the estimate works

The reader takes at most **50,000 vertices** by default, spread across the file, and caches them while the path, file size, modification time and sample limit remain unchanged. It accepts standard ASCII, little-endian and big-endian PLY with scalar vertex properties and either Gaussian `f_dc_0/1/2` or byte-range `red/green/blue` colors. Gaussian opacity is interpreted as a logit. Invalid coordinates/colors and nearly transparent splats are ignored. Export SPZ/SOG/other formats to PLY first; compressed PLY variants need conversion to standard PLY.

Colors near the outer 30% of each camera image contribute to the fit, with separate upper and lower groups. A smoothed dominant-color histogram reduces isolated color outliers and avoids averaging unrelated sky and ground colors. If boundary coverage is sparse, visible interior colors supply a fallback and Status reports it. If no colors are visible, check the PLY/Frames alignment and Transform; no fabricated color is returned. The estimate uses splat centers and baked DC colors; it does not reproduce Gaussian footprints, spherical-harmonic view dependence, renderer clipping/cropping, occlusion or foreground design geometry.

This is an estimated backdrop, not recovered scenery. Large gaps, interiors or views with several different boundary colors may still need a manually chosen background. A gradient can reduce contrast at transparent edges but cannot make them independent of the background. Keep the original PLY and the renderer's baked appearance; do not use this component to compensate for unwanted relighting.

## Build and verification

The Windows development build is under `bin/AutoBackground/windows`. Close Rhino and use the existing installer with that source folder, or copy **Splatypus.gha** and **Splatypus.Core.dll** together into the existing Splatypus Grasshopper installation. Keep one installed copy. The macOS build is cross-compiled; native macOS viewport behavior still needs verification.

Core checks: `dotnet run --project Tests/RhinoSplat.SplatBackground.SmokeTest.csproj -c Release`.
Native checks: `Tests/RhinoSplat.AutoBackgroundRhino.SmokeTest.csproj`; run `Tests/RunAutoBackgroundTests.py` through Rhino's `_-RunPythonScript` for camera projection, live preview mode settings/restoration and saved capture sockets. It uses a temporary floating viewport and restores the active GH document. Capture integration checks in `Tests/RhinoSplat.ViewCapture.SmokeTest.csproj` also cover gradient PNG output and migration of older saved capture definitions.
