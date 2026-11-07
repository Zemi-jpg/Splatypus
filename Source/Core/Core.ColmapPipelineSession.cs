namespace RhinoSplat.Core;

public sealed record ColmapPipelineState(ColmapPipelineResult? Result, ColmapPipelineProgress? Progress,
    bool IsRunning, bool IsCancelling);

/// <summary>UI-thread controller; only immutable progress snapshots cross the worker boundary.</summary>
public sealed class ColmapPipelineSession : IDisposable
{
    private Task<ColmapPipelineResult>? _task;
    private CancellationTokenSource? _cancellation;
    private ColmapPipelineResult? _result;
    private ColmapPipelineProgress? _progress;
    private bool _previousRun, _cancelling, _disposed;
    private bool _showProgress = true;
    public event Action? StateChanged;

    public ColmapPipelineState Update(ColmapPipelineRequest request, bool run, bool cancel, bool showProgress = true)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Volatile.Write(ref _showProgress, showProgress);
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
            var cancellation = new CancellationTokenSource();
            _cancellation = cancellation;
            _result = null;
            Volatile.Write(ref _progress, null);
            _cancelling = false;
            _task = Task.Run(() => ColmapPipelineRunner.RunAsync(request, cancellation.Token, value =>
            {
                Volatile.Write(ref _progress, value);
                if (Volatile.Read(ref _showProgress)) StateChanged?.Invoke();
            }));
            _ = _task.ContinueWith(_ =>
            {
                cancellation.Dispose();
                StateChanged?.Invoke();
            }, CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
        return new(_result, Volatile.Read(ref _progress), _task is not null, _cancelling);
    }

    private static async Task CancelAsync(CancellationTokenSource? source)
    {
        if (source is null) return;
        try { await source.CancelAsync().ConfigureAwait(false); }
        catch (ObjectDisposedException) { }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        StateChanged = null;
        _ = CancelAsync(_cancellation);
        _task = null;
    }
}
