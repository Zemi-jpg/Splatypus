# Install Splatypus

## Requirements and supported platforms

Use Rhino 8.34.26223.11001 or newer with Grasshopper and .NET 8. If Rhino is in .NET Framework mode, use Rhino's `SetDotNetRuntime` command to select .NET Core and restart; verify the runtime in Rhino's system information. Splatypus targets .NET 8 and does not support Rhino 7.

Windows x64 is the primary release target. A separate macOS package is available; automated build and archive checks do not establish native Mac workflow test coverage. The automatic Mac tool installer supports Apple Silicon only. Neither Windows ARM nor Intel Mac automatic tool installation is supported.

## Rhino Package Manager

Run `_PackageManager` and search for Splatypus. If it is unavailable in your package catalog, use the [food4Rhino downloads](https://www.food4rhino.com/en/app/splatypus) or the [versioned GitHub packages](../dist/publication/).

1. Back up saved Grasshopper definitions and project folders.
2. Choose Package Manager or manual installation and keep one installed copy.
3. Open Rhino and run `_PackageManager`. Search **Splatypus** and select version **0.1.0**.
4. Select the compatible version, click Install, then restart Rhino.
5. Open Grasshopper and find the Splatypus tab. Place SP Hello and connect Status to a Panel. Expected output: `SP is loaded: Ready`.

Package Manager installs the platform-specific plugin and Core library together. Download Tools is a separate user-triggered step; Package Manager does not install FFmpeg, COLMAP or Brush.

## Local Yak or ZIP installation

For a local `.yak`, a developer can use Rhino's bundled Yak CLI: `yak install <absolute-path-to-package.yak>`, then restart Rhino. Use the Windows (`win`) or Mac (`mac`) package matching the machine. Local installation does not make a package public or searchable.

For a manual ZIP, unblock the downloaded ZIP in Windows Properties if the checkbox is present, then extract. Open Grasshopper's Components Folder, close Rhino, create a Splatypus subfolder and copy **both** Splatypus.gha and Splatypus.Core.dll there. Restart. Keep one installed copy. Never copy the Windows GHA onto a Mac.

## Tools

Place SP Download Tools under 01 Setup. Use a writable Folder, or leave it blank for the per-user RhinoSplat/Tools location. On Windows select CUDA=False for CPU COLMAP, or CUDA=True when using an NVIDIA CUDA installation. Connect a Button to Download and wait for Ready=True. Connect the returned executable paths to later Exe inputs.

Startup validation does not verify training GPU compatibility. Manual tool paths are accepted. FFprobe must sit beside FFmpeg. See [Download Tools](../Download-tools.md) for versions, network sources, checksums, recovery and the Apple Silicon package environments.

## Update and uninstall

Manage the installed release in Package Manager and restart Rhino after a change. Do not overlay manual files onto a Package Manager installation. Update the GHA and Core library as a matched pair.

Uninstall Splatypus from Package Manager's Installed tab. Projects, exports and external tool installations are stored separately and remain on disk. Delete those separately only if you no longer need them. The default per-user Tools folder is managed by the plugin.

Source: [McNeel PackageManager](https://docs.mcneel.com/rhino/8/help/en-us/commands/packagemanager.htm), [Yak CLI](https://developer.rhino3d.com/guides/yak/yak-cli-reference/).
