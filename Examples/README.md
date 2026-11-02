# Splatypus example workflow

Open **Splatypus_Workflow.gh** in Grasshopper after installing Splatypus 0.2.0 for Rhino 8.34+ with .NET 8. This is the publisher-supplied SP_020 working template, copied unchanged into the package's existing Examples location.

1. Disable the Grasshopper solver before opening the definition so you can review the saved settings first.
2. Set your own writable Parent Folder and Project Name in SP Project. Replace any saved local paths with locations on your machine.
3. Select your own photograph or video source and output locations. Media, trained models and external tool binaries are not bundled with this example.
4. Use SP Download Tools or supply your installed FFmpeg, COLMAP and Brush executable paths. Review the platform and GPU settings.
5. Check Run, Create, Download and Replace inputs before enabling the solver. Run the workflow in stages and follow Status and Log outputs.

Save a working copy outside the installed package folder so package updates do not replace your edits. See the included user guide for dataset preparation, alignment, training and troubleshooting.

The example has been checked for archive readability and packaging integrity. Opening and running it in the release build still forms part of native Rhino validation.

Copyright (c) 2026 Zeming Li. Distributed under the package's MIT License.

Support: [lizeming2021@gmail.com](mailto:lizeming2021@gmail.com). Website: [zeming.li](https://zeming.li/).
