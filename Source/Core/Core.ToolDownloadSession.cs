namespace RhinoSplat.Core;

public sealed record ToolDownloadState(ToolDownloadResult? Result, bool Busy, string Status);

/// <summary>UI-owned state; all network, filesystem and process work runs on a worker.</summary>
public sealed class ToolDownloadSession : IDisposable
{
    private readonly Func<ToolDownloadRequest, bool, Action<string>?, CancellationToken, Task<ToolDownloadResult>> _runner;
    private Task<ToolDownloadResult>? _task;
    private CancellationTokenSource? _cancellation;
    private ToolDownloadRequest? _request;
    private ToolDownloadRequest? _workingRequest;
    private ToolDownloadResult? _result;
    private string _progress = "Checking installed tools…";
    private int _changed;
    private bool _previousRun, _pendingInstall, _disposed;
    public event Action? Changed;
    public bool HasChanges => Volatile.Read(ref _changed) != 0 || _task?.IsCompleted == true;

    public ToolDownloadSession() : this(new ToolDownloadInstaller().RunAsync) { }
    internal ToolDownloadSession(Func<ToolDownloadRequest, bool, Action<string>?, CancellationToken, Task<ToolDownloadResult>> runner) => _runner = runner;

    public ToolDownloadState Update(ToolDownloadRequest request, bool download, bool cancel)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        Interlocked.Exchange(ref _changed, 0);
        var edge = download && !_previousRun;
        _previousRun = download;
        // Terminal is a display preference for the next Download, not a change to installed tools.
        var requestChanged = !SameTools(_request, request);
        if (requestChanged)
        {
            _request = request;
            _result = null;
            _pendingInstall = false;
            Cancel();
        }
        if (edge && (_task is null || requestChanged)) _pendingInstall = true;
        // A click during the initial read-only check is kept; repeat clicks during an install are ignored.
        else if (edge && SameTools(_workingRequest, request) && !_installing) _pendingInstall = true;
        if (cancel) { _pendingInstall = false; Cancel(); }
        if (_task?.IsCompleted == true)
        {
            var result = _task.GetAwaiter().GetResult();
            if (SameTools(_workingRequest, request)) _result = result;
            _task = null;
            _cancellation?.Dispose();
            _cancellation = null;
        }
        if (_task is null && (_result is null || _pendingInstall) && !cancel)
        {
            var install = _pendingInstall;
            _pendingInstall = false;
            _installing = install;
            _workingRequest = request;
            _result = null;
            _progress = install ? "Starting tool installation…" : "Checking installed tools…";
            var cancellation = new CancellationTokenSource();
            _cancellation = cancellation;
            _task = Task.Run(async () =>
            {
                try { return await _runner(request, install, Report, cancellation.Token).ConfigureAwait(false); }
                catch (OperationCanceledException) { return new ToolDownloadResult("", "", "", "", "Cancelled. Press Download to retry.", true); }
                catch (Exception e) { return new ToolDownloadResult("", "", "", "", e.Message); }
            });
            _ = _task.ContinueWith(_ => Report("Finished"), CancellationToken.None, TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
        }
        return new(_result, _task is not null, _task is not null ? Volatile.Read(ref _progress) : _result?.Status ?? "Press Download to install tools.");
    }

    private bool _installing;
    private static bool SameTools(ToolDownloadRequest? first, ToolDownloadRequest second) =>
        first is not null && first.Folder == second.Folder && first.ColmapCuda == second.ColmapCuda;
    private void Report(string text)
    {
        if (_disposed) return;
        Volatile.Write(ref _progress, text);
        Interlocked.Exchange(ref _changed, 1);
        Changed?.Invoke();
    }
    private void Cancel() { try { _cancellation?.Cancel(); } catch (ObjectDisposedException) { } }
    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Changed = null;
        Cancel();
        var cancellation = _cancellation;
        if (_task is not null) _ = _task.ContinueWith(_ => cancellation?.Dispose(), TaskScheduler.Default);
        else cancellation?.Dispose();
    }
}
