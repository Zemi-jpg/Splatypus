using System.Text;

namespace RhinoSplat.Core;

/// <summary>A separate, immediately flushed log for each installation attempt.</summary>
internal sealed class ToolDownloadLog : IDisposable
{
    private readonly object _gate = new();
    private readonly StreamWriter _writer;
    internal string? Error { get; private set; }
    public string Path { get; }
    internal ToolDownloadLog(string folder)
    {
        var directory = System.IO.Path.Combine(folder, "logs");
        Directory.CreateDirectory(directory);
        Path = System.IO.Path.Combine(directory, $"install-{DateTime.Now:yyyyMMdd-HHmmss}-{Guid.NewGuid():N}.log");
        _writer = new StreamWriter(new FileStream(Path, FileMode.CreateNew, FileAccess.Write, FileShare.ReadWrite), new UTF8Encoding(true)) { AutoFlush = true };
        Line("Splatypus — Download Tools");
        Line("Closing this terminal does not cancel installation. Use Cancel in Grasshopper.");
    }
    internal void Line(string text) => Append($"{Environment.NewLine}[{DateTime.Now:HH:mm:ss}] {text}{Environment.NewLine}");
    internal void Append(string text)
    {
        lock (_gate)
        {
            if (Error is not null) return;
            try { _writer.Write(text); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Error = "Could not write installation log: " + e.Message; }
        }
    }
    public void Dispose()
    {
        lock (_gate)
            try { _writer.Dispose(); }
            catch (Exception e) when (e is IOException or UnauthorizedAccessException) { Error ??= "Could not finish installation log: " + e.Message; }
    }
}
