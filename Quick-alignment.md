# Quick three-point alignment (0.2.0 development)

Find **SP Quick Align** in **Splatypus > 04 Realignment**. This provides a starting alignment before fine adjustment with **SP Geometry Translator**.

## Pick and preview

1. Connect **SP Sparse Point Cloud → Cloud** to **Quick Align → Cloud**. Keep the previews connected to the original active dataset.
2. Connect **SP Camera Planes → Planes** and optionally **Centres** to **Quick Align → Cameras**. These geometries must use the same source coordinates as the cloud. Tree branches are preserved.
3. Hide the upstream cloud/camera previews to see the result clearly. Quick Align temporarily displays its own blue source cloud while picking, including when the upstream preview is hidden.
4. Double-click the Quick Align component. In Rhino, click a sparse point highlighted in gold, then snap to its corresponding reference point on native Rhino geometry. Repeat in the order **source 1 / reference 1 / source 2 / reference 2 / source 3 / reference 3**. Use Rhino object snaps for accurate reference positions; typed reference coordinates also work.
5. Choose distinct, widely separated features forming a triangle. Zoom or orbit to distinguish overlapping points. The picker uses Rhino's viewport pick aperture and returns an actual sparse point; clicking empty space requests another pick.
6. After the third pair, Cloud and Cameras preview transformed copies. **Error** reports the RMS matching distance in Rhino document units. A noisy triangle yields a best fit, so all three points need not coincide exactly.

The fit uses translation, a proper rotation and positive uniform scale. It does not distort the model with shear, nonuniform scale or reflection. The source geometry is never baked or modified.

**Esc** cancels at any prompt, preserving the previous completed alignment. Double-click again to pick on the **currently aligned cloud** and refine its position, rotation and scale. Each completed pass replaces the stored pairs while **Xform** remains the complete transform from the original model to the latest result. There is no need to merge successive Quick Align passes.

Click the **Reset** button on the component (or choose Reset at the first sparse-point prompt) to restore the original cloud and cameras. Explicit Reset publishes an identity Xform, allowing the original alignment to be written downstream. Reset does not clear Geometry Translator's sliders. These changes support Grasshopper Undo. Pairs and reset state are saved with the Grasshopper document. If the source cloud's coordinates, order or point count change, the previous pairs are withheld until you repick or Reset. Before the first alignment or explicit Reset, geometry passes through for preview and Xform is empty.

## Fine adjustment and writing the model

For quick alignment alone, connect **Quick Align → Xform** to **SP Align COLMAP Model → Xforms**, supply the same base Dataset and COLMAP executable, then press **Write**. The writer transforms **all** sparse points and camera poses in the base model, including points/cameras omitted by preview limits. Intrinsics and image observations are preserved.

For fine adjustment:

1. Feed Quick Align's Cloud and Cameras into **SP Geometry Translator → Geo**. Set its pivot in the already aligned coordinates.
2. Connect **Quick Align → Xform** to **Geometry Translator → Previous**. This records the earlier alignment for saving; it is not applied again to the already-aligned geometry or pivot.
3. Connect **Geometry Translator → Transforms** directly to **Align Model → Xforms** (an existing Data Dam may remain between them). It now contains Quick Align's complete alignment followed by the five fine-adjustment transforms. The writer applies identical branches only once; do not flatten duplicate branches or prepend Quick Align again with Merge. An unconnected Previous input preserves the original five-transform behavior.
4. Press Write. The saved result should match the fine-adjusted cloud and camera centres. Keep the original Dataset as the source; do not feed the aligned output back into Quick Align.

Alignment writes the existing separate `colmap/aligned` branch. Repeating Write applies the complete sequence to the original base rather than compounding it. Compare camera centres and orientation when checking the saved result: camera-plane rectangle size is only a preview setting. See [alignment writing](Alignment-branches.md) and [fine adjustment](Geometry-translator.md).

## Implementation and checks

The [Gumball Selector](https://github.com/dfytz1/Gumball/blob/master/GeometrySelect.vb) was inspected for its viewport-selection approach. This implementation uses RhinoCommon `GetPoint` and native `PickContext.PickFrustumTest` with a temporary display conduit. No Gumball code or dependency is included, and no general gumball editing feature is added.

Run numerical and COLMAP pose/point regression checks with:

```powershell
dotnet run --project Tests/RhinoSplat.QuickAlign.SmokeTest.csproj -c Release
```

Build `Tests/RhinoSplat.QuickAlignRhino.SmokeTest.csproj -c Release`, then run `Tests/RunQuickAlignTests.py` in a separate Rhino instance to check native cloud/camera copying, serialization and orthographic/perspective viewport hit testing. The harness opens and disposes a temporary Rhino document. Its report is `bin/QuickAlign/rhino-test-report.txt`.

Interactive acceptance: pick three pairs; cancel during a replacement and check the previous output; reset and Undo; save/reopen the definition; change the source cloud; compare the written model with the preview after fine adjustment. Native macOS interaction should also be checked before publishing 0.2.0.

For the guided interactive check, run `Tests/RunQuickAlignInteractive.py` in a separate visible Rhino instance. In its Top view, match source `(0,0,0)`, `(10,0,0)`, `(0,10,0)` to native reference points `(30,40,50)`, `(30,60,50)`, `(10,40,50)` respectively, then press Esc when the replacement picking session starts. Enable Rhino's Point object snap for the references. The report is `bin/QuickAlign/interactive-test-report.txt`.

Validation on Windows, 2026-10-26: both plugin projects build without warnings; 3,020 numerical/COLMAP checks and 20 native Rhino checks pass. An interactive source pick and native reference snap succeeded. The remaining interactive steps were cancelled when the desktop capture tool failed; full three-pair completion, reset/Undo and native macOS interaction remain manual release checks. The existing 0.1.0 packages and release metadata have not been replaced.

Refinement update validation, 2026-10-28: 48 native Rhino checks pass, including repeated refinement from aligned coordinates, accumulated transforms, serialization, the Reset button mouse handler, Reset Undo/Redo, legacy Translator socket migration, and cloud/camera preview agreement with the combined COLMAP transform. The 10 alignment-input checks also pass. Windows and macOS builds complete without warnings. Native macOS interaction and a complete manual multi-pass picking session remain manual checks.
