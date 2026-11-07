namespace RhinoSplat.Core;

public sealed record BrushTrainingRequest(string Executable, string Dataset, string ProjectRoot,
    string JobName, int TotalSteps, bool WithViewer, bool ShowTerminal, bool ReplaceExisting, bool RotateForVisualization,
    int MaxResolution = 1920, int MaxSplats = 10000000);

public sealed record BrushTrainingState(BrushRunResult? Result, bool IsTraining, bool IsCancelling);

/// <summary>UI-thread-owned controller. Workers return results; Update never waits.</summary>
public sealed class BrushTrainingSession : IDisposable
{
    private Task<BrushRunResult>? _task;
    private CancellationTokenSource? _cancellation;
    private BrushRunResult? _result;
    private bool _previousRun, _cancelling, _disposed;
    public bool HasCompletedWork => _task?.IsCompleted == true;
    public event Action? ResultAvailable;

    public BrushTrainingState Update(BrushTrainingRequest request, bool run, bool cancel)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_task is { IsCompletedSuccessfully: true })
        {
            _result = _task.Result;
            _task = null;
            _cancellation = null;
            _cancelling = false;
        }
        var risingEdge = run && !_previousRun;
        _previousRun = run;
        if (cancel && _task is not null)
        {
            _cancelling = true;
            _ = CancelAsync(_cancellation);
        }
        if (risingEdge && !cancel && _task is null)
        {
            // The request contains only immutable strings and value types.
            var input = request;
            var cancellation = new CancellationTokenSource();
            _cancellation = cancellation;
            _result = null;
            _cancelling = false;
            _task = Task.Run(async () =>
            {
                try
                {
                    return await BrushTrainingRunner.RunAsync(input.Executable, input.Dataset, input.ProjectRoot,
                        input.JobName, input.TotalSteps, input.WithViewer, input.ShowTerminal, input.ReplaceExisting,
                        input.RotateForVisualization, cancellation.Token, input.MaxResolution, input.MaxSplats).ConfigureAwait(false);
                }
                catch (OperationCanceledException)
                {
                    return new BrushRunResult(false, true, null, string.Empty, string.Empty, string.Empty, "Brush training was cancelled.");
                }
                catch (Exception exception)
                {
                    return new BrushRunResult(false, false, null, string.Empty, string.Empty, exception.ToString(), "Could not run Brush training; inspect the log.");
                }
            });
            _ = _task.ContinueWith(_ =>
            {
                cancellation.Dispose();
                ResultAvailable?.Invoke();
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
        return new(_result, _task is not null, _cancelling);
    }

    private static async Task CancelAsync(CancellationTokenSource? cancellation)
    {
        if (cancellation is null) return;
        try { await cancellation.CancelAsync().ConfigureAwait(false); }
        catch (ObjectDisposedException) { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        ResultAvailable = null;
        _ = CancelAsync(_cancellation);
        _task = null;
    }
}
