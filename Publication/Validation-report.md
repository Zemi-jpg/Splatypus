# Package validation report

Splatypus 0.2.0. Prepared 2 November 2026 for publisher upload.

| Check | Result |
| --- | --- |
| Windows/macOS release builds | Passed with zero warnings and zero errors |
| Runtime and host SDK | .NET 8; Grasshopper and RhinoCommon 8.34.26223.11001 |
| Assembly version | 0.2.0.0 in each GHA and Core library |
| Package version and identity | Splatypus 0.2.0; existing library GUID retained |
| Mac dependencies | No direct Windows Forms reference |
| Component icons | 26 embedded resources per GHA, matching the component inventory; mesh reconstruction excluded |
| SP_020 example | Readable Grasshopper archive; 229 objects; all 30 Splatypus component instances match release source IDs |
| Template bytes | All packages match the supplied SP_020/Splatypus_Workflow.gh without modification |
| Template SHA-256 | `7ea9336782631a3d519eb9bec896570734a528e99d79a3023c521bf6bf4b25f0` |
| Package structure | Same nine file paths as the platform-specific 0.1.0 archives |
| Documentation | Consolidated 0.2.0 README and eight-page quick-start PDF; every PDF page visually inspected |
| README local links | Point to packaged files or internal sections |
| License and notices | MIT, copyright 2026 Zeming Li; support and website retained |
| Archive consistency | Explicit file allowlist; matching Yak/ZIP payload bytes except Yak-normalized manifests |
| Checksums | SHA-256 list covers all four 0.2.0 archives |
| Previous release | Four 0.1.0 archives retained and checked against their original SHA-256 records |

Existing smoke suites passed: Quick Align (3,020), matching (13), video playback and refinement (62), background estimation (12), COLMAP pipeline (29), and Brush background training (24): **3,160 checks total**. Controlled subprocess tests verify orchestration; these runs do not establish reconstruction or training quality on a real scene.

This package-preparation run did not install into the user's Rhino profile or execute the complete template in native Rhino. Native macOS workflow validation remains outstanding. No Food4Rhino/Yak upload or GitHub publication was performed.
