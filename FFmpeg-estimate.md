# Live JPEG estimate

`SP Media Import` now appends two outputs to its existing outputs:

- **Estimated JPEGs / Estimate**: the approximate count for the current Media Sources and FPS inputs.
- **Probe Status / Probe**: estimate details, or the reason a count is unavailable.

Connect `Estimate` to a Panel and adjust the FPS slider with Run set to False. The estimate updates in the background after a short pause in slider movement. Press Run only when ready to extract. The existing `Count` output reports the actual count when the latest import finishes.

Extraction also runs in the background, outside Grasshopper's solution. `Status` shows that it is running. Completion requests an output update on the UI thread; there is no recurring Grasshopper solution timer. Unchanged document recomputes do not restart estimation or extraction. `Cancel` stops the active import and retains JPEGs already written. Use Buttons for Run and Cancel. Pressing Run while busy is ignored; changing FPS or sources affects the next import, while the current import keeps its original settings. Closing/switching the document or removing the component cancels its background work. Reset a held Run toggle to False before starting again after returning to the document.

The `Log` output shows at most 50 recent lines (about 6,000 characters), so a connected Panel does not need to lay out thousands of diagnostic lines. The complete log remains in `ffmpeg-import.log` in the project folder. The JPEG list remains complete.

Video durations are read with `ffprobe.exe` beside the selected `ffmpeg.exe`, using [FFmpeg's metadata interface](https://ffmpeg.org/ffprobe.html). Durations are cached in memory, so changing FPS does not launch another probe. Right-click the component and choose **Refresh media estimate** after changing files inside a source folder without changing the component inputs. A file size or modification-time change invalidates its cached duration when refreshed. Folder sources use the same expansion, duplicate removal and project-image exclusion as the importer.

Each video contributes its duration times FPS, rounded to the nearest integer; each still image contributes one. Video timestamps and clip boundaries can make actual extraction counts differ. If any video cannot be probed, Estimate is empty and Probe explains why. Probing creates no project folders, JPEGs or logs. A failed probe does not disable an explicitly requested import.

Build with `dotnet build .\RhinoSplat.Grasshopper.csproj --configuration Debug --no-restore`. Deploy **both** `RhinoSplat.gha` and `RhinoSplat.Core.dll` from the Grasshopper build output, then restart Rhino. If a component saved in an older definition does not show the appended outputs and Cancel input, replace that instance with a fresh `SP Media Import` component.

The dependency-free smoke harness checks real FFmpeg extraction, controlled metadata/error cases, and the background controller with stalled child processes. All 53 checks passed, covering prompt start/cancel/poll calls, suppression of repeated imports and stale estimates, process cleanup, bounded log previews, and successful asynchronous completion:

```powershell
dotnet run --project .\Tests\RhinoSplat.FFmpeg.SmokeTest.csproj -- "C:\path\to\ffmpeg.exe"
```

The harness generates small synthetic media in a unique temporary directory and prints its location.

The installed plugin also passed a live Rhino 8 / Grasshopper check on 2026-09-15 using a synthetic video producing 693 JPEGs, with JPEGs, Log, and Status connected to Panels. There were three solutions in total (initial input and two background completions), then zero additional solutions during ten seconds after both results were ready. The UI timer delivered 93 ticks during that interval, with a maximum gap of 132 ms; canvas zoom remained responsive afterward. The report is saved in `bin/LiveCompletionTest/report.json`, and the repeatable Rhino script is `Tests/LiveGrasshopperCompletion.py`. This isolated check does not profile downstream components in an existing definition.
