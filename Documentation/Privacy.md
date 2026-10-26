# Privacy and local data

Prepared 2026-10-06 for the current source implementation. Review this notice when network behavior changes.

## Processing

Splatypus starts local FFmpeg, COLMAP and Brush processes and reads/writes selected project folders. The inspected plugin code contains no publisher analytics, account login, activation service, or automatic upload of images, models, definitions or logs. This describes Splatypus; Rhino, Food4Rhino and third-party tools have their own policies.

## Network requests

Pressing Download in SP Download Tools initiates HTTPS downloads from GitHub and its release CDN. On Apple Silicon, micromamba also resolves/install packages from conda-forge via `conda.anaconda.org`. Those providers receive ordinary network information such as an IP address and download requests. Downloads are triggered by the user; opening a definition checks local tools without starting a download.

Rhino Package Manager contacts McNeel's package services when installing/updating the plugin. Food4Rhino may process account and download information independently. Splatypus does not receive that information through the inspected code.

## Files and retention

Projects store images, camera/point models, database files, dataset links, PLY exports and logs under the selected Root. External tools are installed under the selected Tools Folder or the platform's per-user local application data location under `RhinoSplat/Tools`. Download logs and receipts remain there. Splatypus does not impose an automatic retention period.

Processing and Replace settings can overwrite current project data or a training job's previous exports. Keep source media and important exports backed up. Uninstalling the plugin does not delete projects or downloaded tools.

## Support

Nothing is sent to the publisher automatically. If you choose to send a bug report, logs may disclose paths, usernames, filenames, command arguments and scene information. Redact private details and use media you have permission to share. Support contact: [lizeming2021@gmail.com](mailto:lizeming2021@gmail.com), Zeming Li. Website: [zeming.li](https://zeming.li/). Publisher support-data handling still requires review before release.
