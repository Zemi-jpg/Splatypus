# Publication asset register

All selected graphics reuse the existing Splatypus vector artwork in Assets/Icons-Splatypus, with new text, framing and diagrams. No third-party scene photograph, stock image, downloaded official logo, font file or product screenshot is included.

| File under assets/ | Dimensions | Use and alt text |
| --- | --- | --- |
| splatypus-icon-64.png | 64 x 64 | Rhino Package Manager icon. Alt: Splatypus platypus mark with three colored dots. |
| splatypus-logo-512.png | 512 x 512 | Square listing image. Alt: Splatypus platypus mark on a light background. |
| splatypus-hero-1600x900.png | 1600 x 900 | Main listing image. Alt: Splatypus for Rhino 8: photogrammetry and Gaussian splat training, with import, reconstruct, train and inspect stages. |
| splatypus-workflow-1600x900.png | 1600 x 900 | Workflow diagram. Alt: Six stages from project and media import through COLMAP, dataset preparation, Brush training and point-cloud inspection, with optional undistortion and alignment. |
| splatypus-workflow-horizontal.png | 3200 x 1120 | Opening PDF overview supplied by the publisher. Alt: Connected media, reconstruction, optional dataset refinement, training and Rhino preview workflow. |
| splatypus-components-1600x1000.png | 1600 x 1000 | Component sheet. Alt: The 21 Splatypus components grouped by setup, dataset, COLMAP, alignment, training and preview. |

The owner supplied **Examples/Splatypus_Workflow.gh** as the demonstration definition. The packaged copy uses Zeming Li's author credit and clears the saved local parent-folder path; the original supplied file is unchanged. The definition and the consolidated README.md are included in both platform Yak packages, manual ZIPs and the publication kit.

The generated listing images have matching editable SVG masters. The publisher-supplied horizontal overview is retained as its original PNG. The transparent package icon is rendered directly from the original brand master. The component names/categories are read from current source into component-inventory.json to match the release components.

Run Generate-assets.cjs with Node and Sharp to regenerate. The supplied dimensions are production-friendly exports, not an assertion of Food4Rhino's current mandatory sizes. Check the live upload form for crop, filesize and format limits; keep masters and make separate resized copies when needed.

The workflow and component sheet are explicitly labeled diagrams/icon sheets. Do not describe them as screenshots or evidence of native integration tests. For additional real demonstration screenshots, capture an owned/permitted dataset in the actual release build, record Rhino/OS, and remove personal paths and unrelated copyrighted imagery.

The platypus mark originates at Assets/Icons-Splatypus/brand/splatypus.svg; component masters originate at Assets/Icons-Splatypus/svg/. The existing README describes tool-inspired references. See Copyright-and-rights.md and ../README.md#licenses-and-credits for license and credit details.
