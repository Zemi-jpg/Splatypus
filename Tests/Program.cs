using System.Diagnostics;
using System.Globalization;
using RhinoSplat.Core;

if (args.FirstOrDefault() == "-hide_banner")
{
    var source = args[Array.IndexOf(args, "-i") + 1];
    File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "imports.txt"),
        $"{Environment.ProcessId}\t{source}\n");
    await Task.Delay(TimeSpan.FromSeconds(30));
    return;
}

// A tiny ffprobe stand-in lets us verify cache hits, bad metadata and cancellation.
// Only this test helper writes a call log; the production estimator never does.
if (args.FirstOrDefault() == "-v")
{
    File.AppendAllText(Path.Combine(AppContext.BaseDirectory, "calls.txt"), Environment.ProcessId + "\n");
    var response = File.ReadAllText(args[^1]);
    if (response == "sleep") await Task.Delay(TimeSpan.FromSeconds(30));
    Console.WriteLine(response);
    return;
}

if (args.Length != 1 || !File.Exists(args[0]))
    throw new ArgumentException("Pass the full path to the installed ffmpeg.exe.");
var ffmpeg = Path.GetFullPath(args[0]);
var testRoot = Path.Combine(Path.GetTempPath(), "RhinoSplat-FFmpeg-Tests", Guid.NewGuid().ToString("N"));
Directory.CreateDirectory(testRoot);
Console.WriteLine($"Test fixtures: {testRoot}");
var passed = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
    passed++;
    Console.WriteLine($"PASS {message}");
}

async Task RunFfmpeg(params string[] arguments)
{
    var startInfo = new ProcessStartInfo(ffmpeg)
    {
        UseShellExecute = false, CreateNoWindow = true,
        RedirectStandardError = true, RedirectStandardOutput = true
    };
    foreach (var argument in new[] { "-v", "error", "-nostdin", "-n" }.Concat(arguments))
        startInfo.ArgumentList.Add(argument);
    using var process = Process.Start(startInfo)!;
    var error = process.StandardError.ReadToEndAsync();
    var output = process.StandardOutput.ReadToEndAsync();
    await Task.WhenAll(process.WaitForExitAsync(), error, output);
    if (process.ExitCode != 0) throw new InvalidOperationException(await error);
}

var media = Path.Combine(testRoot, "media with spaces");
Directory.CreateDirectory(Path.Combine(media, "nested"));
var clip = Path.Combine(media, "clip.mp4");
var shortClip = Path.Combine(media, "nested", "short.mp4");
var still = Path.Combine(media, "still.jpg");
await RunFfmpeg("-f", "lavfi", "-i", "color=c=red:s=64x64:r=4:d=2.25", "-c:v", "mpeg4", clip);
await RunFfmpeg("-f", "lavfi", "-i", "color=c=blue:s=64x64:r=4:d=0.75", "-c:v", "mpeg4", shortClip);
await RunFfmpeg("-f", "lavfi", "-i", "color=c=green:s=64x64", "-frames:v", "1", still);
var project = Path.Combine(testRoot, "uncreated project");
var estimator = new FFmpegMediaEstimator();
var inputs = new[] { media, clip, still }; // Overlapping inputs must not count twice.
var before = Directory.GetFiles(testRoot, "*", SearchOption.AllDirectories);
var result = await estimator.EstimateAsync(ffmpeg, project, inputs, 2, default);
Check(result.EstimatedImages == 8, "Mixed recursive inputs are deduplicated and rounded per video. " + result.Status);
Check(!Directory.Exists(project) && before.SequenceEqual(Directory.GetFiles(testRoot, "*", SearchOption.AllDirectories)),
    "Estimating does not create the project, logs or images.");
var actual = await FFmpegMediaImporter.RunAsync(ffmpeg, project, inputs, 2, false, false, default);
Check(actual.Succeeded && actual.ImportedImages.Count == result.EstimatedImages,
    "Estimate matches real FFmpeg extraction for fractional-duration fixtures. " + actual.Status);
var higher = await estimator.EstimateAsync(ffmpeg, project, inputs, 4, default);
Check(higher.EstimatedImages == 13, "FPS changes recalculate the expected JPEG count.");
var actualHigher = await FFmpegMediaImporter.RunAsync(ffmpeg, Path.Combine(testRoot, "higher fps"), inputs, 4, false, false, default);
Check(actualHigher.Succeeded && actualHigher.ImportedImages.Count == higher.EstimatedImages,
    "Changed-FPS estimate matches real extraction.");

var nestedProject = Path.Combine(media, "project");
Directory.CreateDirectory(Path.Combine(nestedProject, "images"));
File.Copy(still, Path.Combine(nestedProject, "images", "rs_media_previous.jpg"));
var excluded = await estimator.EstimateAsync(ffmpeg, nestedProject, new[] { media }, 2, default);
Check(excluded.EstimatedImages == 8, "Existing project images are excluded from recursive inputs.");
foreach (var fps in new[] { 0.0, -1, double.NaN, double.PositiveInfinity, 0.00000001, double.MaxValue })
    Check(!(await estimator.EstimateAsync(ffmpeg, project, new[] { clip }, fps, default)).EstimatedImages.HasValue,
        $"Invalid or overflowing FPS {fps} has no misleading count.");
var oldCulture = CultureInfo.CurrentCulture;
try
{
    CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo("de-CH");
    Check((await estimator.EstimateAsync(ffmpeg, project, new[] { clip }, 2.5, default)).EstimatedImages == 6,
        "Decimal FPS parsing is independent of the user's locale.");
}
finally { CultureInfo.CurrentCulture = oldCulture; }
var badFile = Path.Combine(testRoot, "corrupt.mp4");
File.WriteAllText(badFile, "not a video");
Check(!(await estimator.EstimateAsync(ffmpeg, project, new[] { still, badFile }, 2, default)).EstimatedImages.HasValue,
    "Corrupt videos do not produce a partial count presented as a total.");
var audio = Path.Combine(testRoot, "audio.m4a");
await RunFfmpeg("-f", "lavfi", "-i", "sine=duration=0.2", "-c:a", "aac", audio);
Check((await estimator.EstimateAsync(ffmpeg, project, new[] { audio }, 2, default)).Status.Contains("No video stream"),
    "Audio-only files are rejected even when container duration is known.");
Check(!(await estimator.EstimateAsync(ffmpeg, project, new[] { Path.Combine(testRoot, "absent.mp4") }, 2, default)).EstimatedImages.HasValue,
    "Missing sources invalidate the estimate.");

var fakeTools = Path.Combine(testRoot, "probe tools");
Directory.CreateDirectory(fakeTools);
foreach (var file in Directory.GetFiles(AppContext.BaseDirectory))
    File.Copy(file, Path.Combine(fakeTools, Path.GetFileName(file)));
File.Copy(Path.Combine(fakeTools, "RhinoSplat.FFmpeg.SmokeTest.exe"), Path.Combine(fakeTools, "ffprobe.exe"));
var fakeFfmpeg = Path.Combine(fakeTools, "ffmpeg.exe");
File.Copy(Path.Combine(fakeTools, "RhinoSplat.FFmpeg.SmokeTest.exe"), fakeFfmpeg);
var fakeVideo = Path.Combine(testRoot, "controlled.mp4");
File.WriteAllText(fakeVideo, "{\"streams\":[{\"duration\":\"2.25\"}],\"format\":{\"duration\":\"20\"}}");
var controlled = new FFmpegMediaEstimator();
Check((await controlled.EstimateAsync(fakeFfmpeg, project, new[] { fakeVideo }, 2, default)).EstimatedImages == 5,
    "Video stream duration takes precedence over longer container duration.");
Check((await controlled.EstimateAsync(fakeFfmpeg, project, new[] { fakeVideo }, 4, default)).EstimatedImages == 9
    && File.ReadAllLines(Path.Combine(fakeTools, "calls.txt")).Length == 1,
    "Dragging FPS reuses video metadata without launching ffprobe again.");
File.WriteAllText(fakeVideo, "{\"streams\":[{\"duration\":\"3.2500\"}]}");
Check((await controlled.EstimateAsync(fakeFfmpeg, project, new[] { fakeVideo }, 4, default)).EstimatedImages == 13
    && File.ReadAllLines(Path.Combine(fakeTools, "calls.txt")).Length == 2,
    "Changed source metadata invalidates the duration cache.");
File.WriteAllText(fakeVideo, "{\"streams\":[{}],\"format\":{\"duration\":\"1.5\"}}");
Check((await controlled.EstimateAsync(fakeFfmpeg, project, new[] { fakeVideo }, 2, default)).EstimatedImages == 3,
    "Container duration is used when video duration is unavailable.");
File.WriteAllText(fakeVideo, "{\"streams\":[{\"duration\":\"N/A\"}],\"format\":{\"duration\":\"N/A\"}}");
Check(!(await controlled.EstimateAsync(fakeFfmpeg, project, new[] { fakeVideo }, 2, default)).EstimatedImages.HasValue,
    "Unavailable durations leave the estimated count empty.");
File.WriteAllText(fakeVideo, "invalid JSON");
Check(!(await controlled.EstimateAsync(fakeFfmpeg, project, new[] { fakeVideo }, 2, default)).EstimatedImages.HasValue,
    "Malformed probe output is reported without throwing to the component.");
File.WriteAllText(fakeVideo, "sleep");
using (var cancellation = new CancellationTokenSource(TimeSpan.FromSeconds(1)))
{
    var watch = Stopwatch.StartNew();
    var cancelled = await controlled.EstimateAsync(fakeFfmpeg, project, new[] { fakeVideo }, 2, cancellation.Token);
    Check(cancelled.WasCancelled && !cancelled.EstimatedImages.HasValue && watch.Elapsed.TotalSeconds < 5,
        "An active probe can be cancelled promptly.");
}
var timedOut = await controlled.EstimateAsync(fakeFfmpeg, project, new[] { fakeVideo }, 2, default);
Check(!timedOut.EstimatedImages.HasValue && timedOut.Status.Contains("longer than 10 seconds"),
    "A stalled probe times out with an actionable status.");
var noProbe = Path.Combine(testRoot, "no probe");
Directory.CreateDirectory(noProbe);
var noProbeFfmpeg = Path.Combine(noProbe, "ffmpeg.exe");
File.WriteAllText(noProbeFfmpeg, "test validation placeholder; never executed");
Check((await controlled.EstimateAsync(noProbeFfmpeg, project, new[] { still }, 2, default)).EstimatedImages == 1,
    "Still-image estimates do not require ffprobe.");
Check((await controlled.EstimateAsync(noProbeFfmpeg, project, new[] { clip }, 2, default)).Status.Contains("ffprobe was not found"),
    "Missing ffprobe is explained for video inputs.");
using (var cancellation = new CancellationTokenSource())
{
    cancellation.Cancel();
    Check((await controlled.EstimateAsync(ffmpeg, project, inputs, 2, cancellation.Token)).WasCancelled,
        "An already-cancelled request does not start probing.");
}
async Task WaitUntil(Func<bool> condition, string message, int timeoutMilliseconds = 5000)
{
    var watch = Stopwatch.StartNew();
    while (!condition())
    {
        if (watch.ElapsedMilliseconds > timeoutMilliseconds) throw new TimeoutException(message);
        await Task.Delay(30);
    }
}

async Task<FFmpegMediaSessionState> WaitForSession(FFmpegMediaImportSession session,
    FFmpegMediaRequest request, bool run, Func<FFmpegMediaSessionState, bool> condition)
{
    FFmpegMediaSessionState? state = null;
    await WaitUntil(() => { state = session.Update(request, run, false); return condition(state); },
        "Background session did not reach the expected state.");
    return state!;
}

bool ProcessIsRunning(int id)
{
    try { using var process = Process.GetProcessById(id); return !process.HasExited; }
    catch (ArgumentException) { return false; }
}

// Exercise the exact controller used by SolveInstance with an intentionally stalled
// child process. No UI driver is needed to prove Update returns before the child exits.
var probeCalls = Path.Combine(fakeTools, "calls.txt");
var callsBefore = File.ReadAllLines(probeCalls).Length;
var request = new FFmpegMediaRequest(fakeFfmpeg, Path.Combine(testRoot, "background estimate"),
    new[] { fakeVideo }, 2, false, false);
using (var session = new FFmpegMediaImportSession())
{
    var watch = Stopwatch.StartNew();
    var initial = session.Update(request, false, false);
    Check(initial.IsEstimating && watch.ElapsedMilliseconds < 500,
        "The UI-facing estimate call returns immediately for a stalled probe.");
    await WaitUntil(() => File.ReadAllLines(probeCalls).Length > callsBefore, "Slow probe did not start.");
    var oldProbeId = int.Parse(File.ReadAllLines(probeCalls)[^1]);
    var replacementVideo = Path.Combine(testRoot, "replacement.mp4");
    File.WriteAllText(replacementVideo, "{\"streams\":[{\"duration\":\"1.5\"}]}");
    request = request with { Sources = new[] { replacementVideo }, FramesPerSecond = 4 };
    var changed = session.Update(request, false, false);
    Check(changed.Estimate is null, "Changing source/FPS clears any stale estimate immediately.");
    var updated = await WaitForSession(session, request, false, state => !state.IsEstimating);
    Check(updated.Estimate?.EstimatedImages == 6, "Only the latest background estimate is published.");
    await WaitUntil(() => !ProcessIsRunning(oldProbeId), "Superseded probe was not stopped.");
    Check(!ProcessIsRunning(oldProbeId), "Changing inputs cancels the obsolete probe process.");
    var callsAfter = File.ReadAllLines(probeCalls).Length;
    for (var index = 0; index < 10; index++) session.Update(request, false, false);
    Check(!session.IsBusy && File.ReadAllLines(probeCalls).Length == callsAfter,
        "Publishing/polling a completed estimate does not create a probe feedback loop.");
    Check(!Directory.Exists(request.ProjectRoot), "Background estimates still create no project files.");
}

var slowImage = Path.Combine(testRoot, "slow-image.jpg");
File.WriteAllText(slowImage, "test still image; fake FFmpeg deliberately stalls");
var mutableSources = new[] { slowImage };
request = new FFmpegMediaRequest(fakeFfmpeg, Path.Combine(testRoot, "background cancellation"),
    mutableSources, 2, false, false);
var importCalls = Path.Combine(fakeTools, "imports.txt");
using (var session = new FFmpegMediaImportSession())
{
    var watch = Stopwatch.StartNew();
    var initial = session.Update(request, true, false);
    Check(initial.IsImporting && initial.Import is null && watch.ElapsedMilliseconds < 500,
        "The UI-facing import call returns immediately instead of waiting for FFmpeg.");
    mutableSources[0] = still;
    await WaitUntil(() => File.Exists(importCalls), "Slow import did not start.");
    var firstCall = File.ReadAllLines(importCalls)[0].Split('\t');
    Check(firstCall[1] == slowImage, "A running import uses an immutable snapshot of the submitted sources.");
    session.Update(request, false, false);
    session.Update(request with { FramesPerSecond = 4 }, true, false);
    await Task.Delay(250);
    Check(File.ReadAllLines(importCalls).Length == 1,
        "FPS changes and repeated Run presses cannot launch a second import while busy.");
    watch.Restart();
    var cancelling = session.Update(request, true, true);
    Check(cancelling.IsCancelling && watch.ElapsedMilliseconds < 500,
        "Cancel returns promptly without blocking the UI on process shutdown.");
    var cancelled = await WaitForSession(session, request, true, state => !state.IsImporting);
    Check(cancelled.Import?.WasCancelled == true, "Cancellation reaches the running FFmpeg process and publishes its result.");
    await WaitUntil(() => !ProcessIsRunning(int.Parse(firstCall[0])), "Cancelled FFmpeg process is still running.");
    for (var index = 0; index < 10; index++) session.Update(request, true, false);
    Check(File.ReadAllLines(importCalls).Length == 1,
        "A held Run toggle does not restart an import after cancellation.");
}

var disposeRequest = request with { ProjectRoot = Path.Combine(testRoot, "dispose running import"), Sources = new[] { slowImage } };
var disposalSession = new FFmpegMediaImportSession();
disposalSession.Update(disposeRequest, true, false);
await WaitUntil(() => File.ReadAllLines(importCalls).Length == 2, "Disposal test import did not start.");
var disposedProcess = int.Parse(File.ReadAllLines(importCalls)[^1].Split('\t')[0]);
var disposeWatch = Stopwatch.StartNew();
disposalSession.Dispose();
Check(disposeWatch.ElapsedMilliseconds < 500, "Removing the component can dispose its session without waiting.");
await WaitUntil(() => !ProcessIsRunning(disposedProcess), "Disposed session left its process running.");
Check(!ProcessIsRunning(disposedProcess), "Removing/closing the component stops its owned FFmpeg process.");

var realRequest = new FFmpegMediaRequest(ffmpeg, Path.Combine(testRoot, "real background import"),
    inputs, 2, false, false);
using (var session = new FFmpegMediaImportSession())
{
    var resultNotifications = 0;
    session.ResultAvailable += () => Interlocked.Increment(ref resultNotifications);
    session.Update(realRequest, true, false);
    var finished = await WaitForSession(session, realRequest, true, state => !state.IsBusy);
    Check(finished.Import?.Succeeded == true && finished.Import.ImportedImages.Count == 9,
        "Real background extraction publishes completed JPEGs. " + finished.Import?.Status);
    // A nested project fixture added one still under a different project's output folder.
    // It is a legitimate input here, so this request has one more JPEG than the earlier test.
    var importResult = finished.Import;
    var resultStayedStable = true;
    for (var index = 0; index < 10; index++)
        resultStayedStable &= ReferenceEquals(session.Update(realRequest, true, false).Import, importResult);
    Check(resultStayedStable, "Completed import remains stable across repeated polls with Run held True.");
    Check(!session.IsBusy, "The background session becomes idle after completion.");
    await WaitUntil(() => Volatile.Read(ref resultNotifications) == 2, "Completion notifications were not delivered.");
    Check(!session.HasCompletedWork, "Published results do not remain marked for another completion update.");
    var notificationsAfterCompletion = Volatile.Read(ref resultNotifications);
    for (var index = 0; index < 100; index++) session.Update(realRequest, true, false);
    await Task.Delay(400);
    Check(!session.IsBusy && Volatile.Read(ref resultNotifications) == notificationsAfterCompletion,
        "Unchanged recomputes after Done launch no probes, imports, or completion notifications.");
    session.InvalidateEstimate();
    session.Update(realRequest, true, false);
    var refreshed = await WaitForSession(session, realRequest, true, state => !state.IsBusy);
    Check(refreshed.Estimate?.EstimatedImages == 9 && ReferenceEquals(refreshed.Import, importResult),
        "Explicit estimate refresh rescans media without repeating a completed import.");
}

using (var cancellation = new CancellationTokenSource())
{
    cancellation.Cancel();
    var retainedImage = actual.ImportedImages[0];
    var cancelled = await FFmpegMediaImporter.RunAsync(ffmpeg, project, inputs, 2, true, false, cancellation.Token);
    Check(cancelled.WasCancelled && File.Exists(retainedImage),
        "An already-cancelled Replace request cannot delete existing imported JPEGs.");
}
var largeLog = string.Join("\r\n", Enumerable.Range(0, 7600).Select(index => $"frame={index:D5} ordinary ffmpeg diagnostic line {new string('x', 40)}"))
    + "\r\nFINAL IMPORT ERROR OR SUMMARY";
var logPreview = FFmpegLogPreview.Create(largeLog, Path.Combine(project, "images"));
Check(logPreview.Length < 6400 && logPreview.Split('\n').Length <= 54,
    "A log of 7,600 lines produces a bounded Panel preview.");
Check(logPreview.Contains("FINAL IMPORT ERROR OR SUMMARY") && logPreview.Contains(Path.Combine(project, "ffmpeg-import.log")),
    "The preview retains final diagnostics and identifies the full log file.");
Check(FFmpegLogPreview.Create(new string('x', 1_000_000), string.Empty).Length < 6400,
    "Even a single enormous log line cannot flood a Grasshopper Panel.");
Check(FFmpegLogPreview.Create("short log", string.Empty) == "short log",
    "Small logs are preserved without unnecessary truncation.");
Check(!actual.Log.Contains("does not contain an image sequence pattern"),
    "Still-image extraction no longer emits a spurious image-sequence warning.");
Console.WriteLine($"All {passed} FFmpeg checks passed.");
