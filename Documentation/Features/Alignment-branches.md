# Geometry Translator and COLMAP alignment

Geometry Translator solves once per geometry branch. Combining a point cloud and camera planes can therefore emit the same transform sequence under both `{0;0;0}` and `{0;1;0}`. The sequence contains five local transforms, or the Previous transforms followed by those five when an earlier alignment is connected.

Previously SP Align COLMAP Model concatenated the branches and applied all ten transforms to the complete model, producing a different result from the geometry preview. For example, scale 16.23 was applied twice, resulting in scale 263.4129.

The alignment component now accepts a single sequence or multiple identical sequences. Identical branches are used once; repeated transforms within a branch are preserved. Different branches produce an error because one COLMAP model requires one alignment. Select the intended branch, or explicitly flatten only when composing all branches sequentially is intentional. Do not flatten duplicate Geometry Translator branches.

Connect the current Active Dataset to the previews and Align Model. Release any Data Dam to pass the latest transforms, then press Write to save a separate aligned model. Alignment always reads the active base model and preserves its camera intrinsics, including those produced by optional undistortion.

Writing again replaces only `colmap/aligned/sparse/0` and `colmap/aligned/sparse-text`. Identical transforms produce the same result; changed transforms apply to the same base, so alignment never accumulates. Keep the base Dataset connected to Geometry Translator's source previews. Connect Aligned Text Model to a separate preview to inspect the saved result, and Aligned Sparse Model to Brush Dataset's optional Sparse Model Override to train from it. Undistortion still automatically replaces the base images and model. If you undistort or reconstruct again, write the alignment again before training from the separate branch. See [the active dataset workflow](Colmap-pipeline.md).

Camera plane Size remains a display setting: regenerated camera planes use their Size input, whereas Geometry Translator also scales their visible rectangles. Compare cloud positions and camera Centres when checking alignment; plane rectangle sizes are not stored as COLMAP camera poses.
