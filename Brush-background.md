# Background Brush training

SP Brush Train now runs its complete job outside Grasshopper's document solution. Press Run once and continue editing Rhino or the Grasshopper definition. Changing inputs during training affects the next run. Recomputes and a held Run toggle do not restart the job; additional Run presses while busy are ignored.

The appended Cancel input stops the owned Brush process and retains checkpoints already exported. Use a Button. Removing the component or closing/unloading its document cancels its job. Reset Run to False before starting again after returning to a document.

Only completion requests a Grasshopper output update; there is no repeating document-solution timer. Log displays a cached tail of up to 50 lines / 6,000 characters. The full log remains in `brush-training.log` in the export directory. Terminal and Brush's optional viewer remain available for live progress. Outputs are published when the Brush process exits, including the optional visualization PLY conversion.

Training still shares GPU resources with Rhino. This change removes the Grasshopper wait; it does not cap Brush GPU use or guarantee unchanged viewport speed. Viewer=False avoids opening the additional Brush viewer.

Install both binaries with Rhino closed and restart Rhino. Existing input positions and the component ID are unchanged. Place a fresh SP Brush Train if an older saved component does not expose Cancel.

Validation: all 18 controller/launcher and visualization checks passed using controlled subprocesses, covering responsive start/cancel/recompute calls, immutable job inputs, cancellation, checkpoint retention, completion/failure, bounded logs, and process cleanup. The Grasshopper build succeeds without warnings or errors. GPU performance with a real Brush training job has not been benchmarked.

Rotate for Visualization creates an additional _rgs.ply for RhinoGaussianSplatting, using +90 degrees around X. The original Brush PLY remains unchanged. There is no separate PLY output: find both files in Export, and the visualization path is reported in Status.
