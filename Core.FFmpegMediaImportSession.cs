namespace RhinoSplat.Core;

public sealed record FFmpegMediaRequest(string Executable, string ProjectRoot,
    IReadOnlyList<string> Sources, double FramesPerSecond, bool ReplaceExisting, bool ShowTerminal);

public sealed record FFmpegMediaSessionState(FFmpegMediaEstimateResult? Estimate,
    FFmpegMediaImportResult? Import, bool IsEstimating, bool IsImporting, bool IsCancelling)
{
    public bool IsBusy => IsEstimating || IsImporting;
}

/// <summary>
/// Owned and polled by the UI thread. All filesystem/probe/import work runs in workers;
/// Update never waits for a worker. Workers only return results, never touch UI state.
/// </summary>
public sealed class FFmpegMediaImportSession : IDisposable
{
    private readonly FFmpegMediaEstimator _estimator = new();
    private FFmpegMediaRequest? _estimateRequest;
    private Task<FFmpegMediaEstimateResult>? _estimateTask;
    private Task<FFmpegMediaImportResult>? _importTask;
    private CancellationTokenSource? _estimateCancellation;
    private CancellationTokenSource? _importCancellation;
    private FFmpegMediaEstimateResult? _estimate;
    private FFmpegMediaImportResult? _import;
    private bool _previousRun;
    private bool _cancelling;
    private bool _disposed;

    public bool IsBusy => _estimateTask is not null || _importTask is not null;
    public bool HasCompletedWork => _estimateTask?.IsCompleted == true || _importTask?.IsCompleted == true;
    // Raised on a worker thread; the host must marshal publication to its UI thread.
    public event Action? ResultAvailable;

    public void InvalidateEstimate() => _estimateRequest = null;

    public FFmpegMediaSessionState Update(FFmpegMediaRequest request, bool run, bool cancel,
        bool refreshEstimate = false)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        CollectCompleted();
        var risingEdge = run && !_previousRun;
        _previousRun = run;

        var changed = !SameEstimateInputs(request, _estimateRequest);
        if (changed || (refreshEstimate && _estimateTask is null))
        {
            _ = CancelInBackground(_estimateCancellation);
            _estimateRequest = Snapshot(request);
            _estimate = null;
            var input = _estimateRequest;
            var cancellation = new CancellationTokenSource();
            _estimateCancellation = cancellation;
            _estimateTask = Task.Run(async () =>
            {
                try
                {
                    // Coalesce rapid slider changes; cancelled requests never start ffprobe.
                    await Task.Delay(150, cancellation.Token).ConfigureAwait(false);
                    return await _estimator.EstimateAsync(input.Executable, input.ProjectRoot,
                        input.Sources, input.FramesPerSecond, cancellation.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return new FFmpegMediaEstimateResult(null, true, "Media estimate was cancelled.");
                }
                catch (Exception exception)
                {
                    return new FFmpegMediaEstimateResult(null, false, $"Could not estimate media: {exception.Message}");
                }
            });
            DisposeWhenComplete(_estimateTask, cancellation);
        }

        if (cancel && _importTask is not null)
        {
            _cancelling = true;
            _ = CancelInBackground(_importCancellation);
        }
        // Pressing Run while busy is ignored, including during cancellation.
        // FPS/source changes and scheduled polls never restart an import.
        if (risingEdge && !cancel && _importTask is null)
        {
            var input = Snapshot(request);
            var cancellation = new CancellationTokenSource();
            _importCancellation = cancellation;
            _cancelling = false;
            _import = null;
            _importTask = Task.Run(async () =>
            {
                try
                {
                    return await FFmpegMediaImporter.RunAsync(input.Executable, input.ProjectRoot,
                        input.Sources, input.FramesPerSecond, input.ReplaceExisting, input.ShowTerminal,
                        cancellation.Token).ConfigureAwait(false);
                }
                catch (Exception exception)
                {
                    return new FFmpegMediaImportResult(false, cancellation.IsCancellationRequested,
                        string.Empty, Array.Empty<string>(), exception.ToString(), "Media import failed; inspect the log.");
                }
            });
            DisposeWhenComplete(_importTask, cancellation);
        }

        return new(_estimate, _import, _estimateTask is not null, _importTask is not null, _cancelling);
    }

    private void CollectCompleted()
    {
        // Reading Result is safe only after completion. Never wait during a GH solution.
        if (_estimateTask is { IsCompletedSuccessfully: true })
        {
            _estimate = _estimateTask.Result;
            _estimateTask = null;
            _estimateCancellation = null;
        }
        if (_importTask is { IsCompletedSuccessfully: true })
        {
            _import = _importTask.Result;
            _importTask = null;
            _importCancellation = null;
            _cancelling = false;
        }
    }

    private static FFmpegMediaRequest Snapshot(FFmpegMediaRequest input) =>
        input with { Sources = input.Sources.ToArray() };

    private static bool SameEstimateInputs(FFmpegMediaRequest left, FFmpegMediaRequest? right) =>
        right is not null && left.Executable == right.Executable && left.ProjectRoot == right.ProjectRoot
        && left.FramesPerSecond.Equals(right.FramesPerSecond)
        && left.Sources.SequenceEqual(right.Sources, StringComparer.Ordinal);

    private static async Task CancelInBackground(CancellationTokenSource? cancellation)
    {
        if (cancellation is null) return;
        // Set the cancellation flag immediately; CancelAsync runs the process-kill
        // callbacks asynchronously so the caller never waits for process shutdown.
        try { await cancellation.CancelAsync().ConfigureAwait(false); }
        catch (ObjectDisposedException) { }
    }

    private void DisposeWhenComplete(Task task, CancellationTokenSource cancellation) =>
        _ = task.ContinueWith(_ =>
        {
            cancellation.Dispose();
            ResultAvailable?.Invoke();
        }, CancellationToken.None,
            TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ResultAvailable = null;
        _ = CancelInBackground(_estimateCancellation);
        _ = CancelInBackground(_importCancellation);
        _estimateTask = null;
        _importTask = null;
    }
}
