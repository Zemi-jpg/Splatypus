using System.Diagnostics;

namespace RhinoSplat.Core;

/// <summary>One project-owned image/model pair; temporary work is never published as a dataset.</summary>
public sealed class ActiveDataset
{
    public static event Action<string>? Changed;
    internal static void NotifyChanged(string root)
    {
        foreach (var callback in Changed?.GetInvocationList() ?? Array.Empty<Delegate>())
            try { ((Action<string>)callback)(root); } catch { /* A UI refresh cannot invalidate a committed model. */ }
    }
    public ActiveDataset(string root)
    {
        if (string.IsNullOrWhiteSpace(root)) throw new ArgumentException("A project dataset is required.");
        Root = Path.TrimEndingDirectorySeparator(Path.GetFullPath(root));
    }
    public string Root { get; }
    public string Images => Path.Combine(Root, "images");
    public string Colmap => Path.Combine(Root, "colmap");
    public string Sparse => Path.Combine(Colmap, "sparse", "0");
    public string Text => Path.Combine(Colmap, "sparse-text");
    public static readonly string[] ModelFiles = { "cameras.bin", "images.bin", "points3D.bin" };
    public static readonly string[] TextFiles = { "cameras.txt", "images.txt", "points3D.txt" };
    public void Validate() => ValidatePair(Images, Sparse);
    public static void ValidatePair(string images, string sparse)
    {
        if (!Directory.Exists(images) || !Directory.EnumerateFiles(images, "*", SearchOption.AllDirectories).Any())
            throw new IOException("The active dataset's images are missing or empty.");
        foreach (var name in ModelFiles)
            if (!File.Exists(Path.Combine(sparse, name)) || new FileInfo(Path.Combine(sparse, name)).Length == 0)
                throw new IOException($"The active camera model is incomplete: {name} is missing or empty.");
    }
    public static void ValidateText(string text, string images)
    {
        foreach (var name in TextFiles)
            if (!File.Exists(Path.Combine(text, name))) throw new IOException($"Model conversion did not produce {name}.");
        var registered = 0;
        foreach (var fields in ReadImagePoses(Path.Combine(text, "images.txt")))
        {
            var image = Path.GetFullPath(Path.Combine(images, fields[9]));
            EnsureWithin(image, images);
            if (!File.Exists(image)) throw new IOException($"Registered image is missing: {fields[9]}");
            registered++;
        }
        if (registered == 0) throw new IOException("The camera model has no registered images.");
    }
    public static IEnumerable<string[]> ReadImagePoses(string path)
    {
        using var reader = File.OpenText(path);
        while (reader.ReadLine() is { } line)
        {
            if (line.StartsWith('#') || string.IsNullOrWhiteSpace(line)) continue;
            var fields = line.Split((char[]?)null, 10, StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length != 10) throw new IOException("The exported image poses are invalid.");
            if (reader.ReadLine() is null) throw new IOException("An image observation line is missing.");
            yield return fields;
        }
    }
    internal static void EnsureWithin(string path, string parent)
    {
        var relative = Path.GetRelativePath(parent, path);
        if (Path.IsPathRooted(relative) || relative == ".." || relative.StartsWith(".." + Path.DirectorySeparatorChar))
            throw new IOException("Dataset paths must stay inside their owning folder.");
    }
    internal void CheckOwnedPath(string path)
    {
        EnsureWithin(path, Root);
        for (var current = new DirectoryInfo(path); current is not null; current = current.Parent)
        {
            if (current.Exists && (current.Attributes & FileAttributes.ReparsePoint) != 0)
                throw new IOException($"The active dataset must use project-owned folders, not directory links: {current.FullName}");
            if (string.Equals(current.FullName, Root, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)) break;
        }
    }
    public static string ResolveText(string datasetOrText)
    {
        var path = Path.GetFullPath(datasetOrText);
        return Directory.Exists(Path.Combine(path, "colmap")) ? new ActiveDataset(path).Text : path;
    }
}

/// <summary>Serializes pipeline, single-step tools and training, including across Rhino processes.</summary>
public sealed class DatasetLease : IDisposable
{
    private static readonly AsyncLocal<DatasetLease?> Current = new();
    private readonly DatasetLease? previous;
    private FileStream? file;
    private bool changed;
    private readonly string root;
    private DatasetLease(string root, FileStream? file) { this.root = root; this.file = file; previous = Current.Value; }
    public static DatasetLease Acquire(string root)
    {
        var dataset = new ActiveDataset(root);
        var existing = Current.Value;
        if (existing?.file is not null && string.Equals(existing.root, dataset.Root,
                OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            return new DatasetLease(dataset.Root, null);
        dataset.CheckOwnedPath(dataset.Colmap);
        Directory.CreateDirectory(dataset.Colmap);
        FileStream stream;
        try { stream = new FileStream(Path.Combine(dataset.Colmap, ".dataset.lock"), FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None); }
        catch (IOException) { throw new IOException("Another dataset operation or training job is already running for this project. Wait for it to finish."); }
        var lease = new DatasetLease(dataset.Root, stream);
        Current.Value = lease;
        return lease;
    }
    public void Dispose()
    {
        if (file is null) return;
        file.Dispose(); file = null;
        Current.Value = previous;
        if (changed) ActiveDataset.NotifyChanged(root);
    }
    internal static void MarkChanged(string root)
    {
        if (Current.Value is { } owner) owner.changed = true;
        else ActiveDataset.NotifyChanged(root);
    }
}

/// <summary>Stages and replaces directories as one recoverable operation; retains recovery files if rollback fails.</summary>
public sealed class DatasetTransaction : IDisposable
{
    private readonly ActiveDataset dataset;
    private bool retain;
    public DatasetTransaction(ActiveDataset dataset)
    {
        this.dataset = dataset;
        dataset.CheckOwnedPath(dataset.Colmap);
        Work = Path.Combine(dataset.Colmap, ".dataset-stage-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(Work);
    }
    public string Work { get; }
    public void Commit(CancellationToken token, params (string? Source, string Target)[] replacements)
    {
        foreach (var (source, target) in replacements)
        {
            dataset.CheckOwnedPath(target);
            if (source is not null) { ActiveDataset.EnsureWithin(source, Work); if (!Directory.Exists(source) && !File.Exists(source)) throw new IOException("Staged output is missing."); }
        }
        token.ThrowIfCancellationRequested();
        var applied = new List<(string? Source, string Target, string Backup, bool Installed)>();
        try
        {
            foreach (var (source, target) in replacements)
            {
                var backup = Path.Combine(Work, "backup-" + applied.Count);
                if (Directory.Exists(target) || File.Exists(target)) Move(target, backup);
                applied.Add((source, target, backup, false));
                if (source is not null)
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                    Move(source, target);
                    applied[^1] = (source, target, backup, true);
                }
            }
            DatasetLease.MarkChanged(dataset.Root);
        }
        catch (Exception error)
        {
            try
            {
                foreach (var entry in applied.AsEnumerable().Reverse())
                {
                    if (entry.Installed) Move(entry.Target, entry.Source!);
                    if (Directory.Exists(entry.Backup) || File.Exists(entry.Backup)) Move(entry.Backup, entry.Target);
                }
            }
            catch (Exception rollback)
            {
                retain = true;
                throw new IOException($"Dataset replacement and rollback failed. Recovery files retained at {Work}. {rollback.Message}", error);
            }
            throw;
        }
    }
    private static void Move(string from, string to)
    {
        if (Directory.Exists(from)) Directory.Move(from, to); else File.Move(from, to);
    }
    internal static void DeleteTree(string path)
    {
        var directory = new DirectoryInfo(path);
        if (!directory.Exists) return;
        if ((directory.Attributes & FileAttributes.ReparsePoint) != 0) { directory.Delete(); return; }
        foreach (var child in directory.EnumerateDirectories()) DeleteTree(child.FullName);
        foreach (var file in directory.EnumerateFiles()) file.Delete();
        directory.Delete();
    }
    public void Dispose()
    {
        if (retain) return;
        try { ActiveDataset.EnsureWithin(Work, dataset.Colmap); DeleteTree(Work); } catch (IOException) { } catch (UnauthorizedAccessException) { }
    }
}

internal sealed class DatasetColmapException(int exitCode, string message) : IOException(message)
{
    public int ExitCode { get; } = exitCode;
}

internal static class DatasetColmapProcess
{
    public static Task<string> Run(string executable, string logPath, CancellationToken token, params string[] args)
        => RunInDirectory(executable, Path.GetDirectoryName(executable)!, logPath, token, args);

    public static async Task<string> RunInDirectory(string executable, string workingDirectory, string logPath, CancellationToken token, params string[] args)
    {
        token.ThrowIfCancellationRequested();
        var info = new ProcessStartInfo(executable) { WorkingDirectory = workingDirectory, RedirectStandardOutput = true, RedirectStandardError = true, UseShellExecute = false, CreateNoWindow = true };
        info.Environment["PATH"] = Path.GetDirectoryName(executable) + Path.PathSeparator + Environment.GetEnvironmentVariable("PATH");
        foreach (var arg in args) info.ArgumentList.Add(arg);
        using var process = new Process { StartInfo = info };
        process.Start();
        using var registration = token.Register(() => { try { if (!process.HasExited) process.Kill(true); } catch (InvalidOperationException) { } catch (System.ComponentModel.Win32Exception) { } });
        var stdout = ColmapLiveLog.ReadAndAppendAsync(process.StandardOutput, logPath);
        var stderr = ColmapLiveLog.ReadAndAppendAsync(process.StandardError, logPath);
        await process.WaitForExitAsync().ConfigureAwait(false);
        var log = (await stdout.ConfigureAwait(false)) + Environment.NewLine + await stderr.ConfigureAwait(false);
        token.ThrowIfCancellationRequested();
        if (process.ExitCode != 0) throw new DatasetColmapException(process.ExitCode, $"COLMAP {args[0]} failed (exit {process.ExitCode}). {log}");
        return log;
    }
    public static async Task<string> Export(string exe, string sparse, string text, string log, CancellationToken token)
    {
        Directory.CreateDirectory(text);
        return await Run(exe, log, token, "model_converter", "--input_path", sparse, "--output_path", text, "--output_type", "TXT");
    }
}
