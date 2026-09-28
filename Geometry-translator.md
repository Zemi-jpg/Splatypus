# SP Geometry Translator

Find **SP Geometry Translator** in **Splatypus > 04 Realignment**. Inputs match the supplied Python component: CenPt, Geo, MoveX, MoveY, MoveZ, DegreeX, DegreeY, DegreeZ, ScaleFactor. Outputs are Geo and Transforms.

Geo is a list of copied geometry. The component applies translation, world-axis X rotation, Y rotation, Z rotation, and uniform scale in that order. Angles are degrees. The centre follows every transformation, so rotation and scale use the moved centre. Defaults are origin, zero movement/rotation, and scale 1. Null geometry entries are omitted. Invalid numbers or failed geometry transformations produce a Grasshopper error without publishing partial results.

Connect Transforms directly to **SP Align COLMAP Model > Xforms**. It contains the five transforms in application order, including identity steps. Use positive scale for COLMAP alignment; zero and negative scale are not valid for that alignment component. Geometry branches follow normal Grasshopper list matching; use a single branch of geometry and one set of controls when producing a single alignment sequence.

The toolbar uses 01 Setup, 02 dataset, 03 COLMAP, 04 Realignment, 05 Training, and 06 Preview. Component names and canvas nicknames use SP. Component IDs are retained. SP COLMAP Model Report is hidden from the toolbar but remains available to existing definitions. See the active-dataset guide for changes to dataset connections.

The regression harness checks the moved pivot, degree conventions, rotation order, composition for alignment, unchanged inputs, native point clouds, curves and Breps. Run `Tests/RunGeometryTranslatorTests.py` using Rhino's `_-RunPythonScript` command after building `Tests/RhinoSplat.GeometryTranslator.SmokeTest.csproj`; native geometry tests require Rhino's runtime.

Validation: all 15 native geometry checks passed inside Rhino on 2026-09-15. The plugin builds without warnings or errors. The toolbar inventory has 19 components across the six groups, including the retained COLMAP Model Report. Test output is packaged as `test-report.txt`.

Close Rhino before installation, then run `powershell -NoProfile -ExecutionPolicy Bypass -File C:\Users\basar\RhinoSplatDev\Install-GeometryTranslator.ps1`. The script backs up the previous plugin files and verifies both installed files. Restart Rhino to see the new component and toolbar groups.
