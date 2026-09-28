from pathlib import Path
root=Path(__file__).resolve().parent.parent
p=root/'Core.BrushDatasetPreparer.cs';s=p.read_text(encoding='utf-8-sig')
junction=s[s.index('    private static void CreateJunction'):s.index('    private static void CreateHardLinkedTree')]
s='''using System.Diagnostics;

namespace RhinoSplat.Core;

public sealed record BrushDatasetResult(bool Succeeded, string DatasetPath, string Status);

public static class BrushDatasetPreparer
{
    public static BrushDatasetResult Prepare(string projectRoot)
    {
        try
        {
            var active = new ActiveDataset(projectRoot);
            using var lease = DatasetLease.Acquire(active.Root);
            active.Validate();
            var dataset = Path.Combine(active.Root, "brush-dataset");
            var sparse = Path.GetDirectoryName(active.Sparse)!;
            // Link both directories, so existing Brush wiring follows every successful replacement.
            if (Targets(Path.Combine(dataset, "images"), active.Images) && Targets(Path.Combine(dataset, "sparse"), sparse))
                return new(true, dataset, "Brush is ready with the current active dataset.");
            using var transaction = new DatasetTransaction(active);
            var prepared = Path.Combine(transaction.Work, "brush-dataset");
            Directory.CreateDirectory(prepared);
            CreateDirectoryLink(Path.Combine(prepared, "images"), active.Images);
            CreateDirectoryLink(Path.Combine(prepared, "sparse"), sparse);
            transaction.Commit(CancellationToken.None, (prepared, dataset));
            return new(true, dataset, "Brush is linked to the active images and model. Undistortion and alignment update this same dataset.");
        }
        catch (Exception error)
        {
            return new(false, string.Empty, "Could not prepare Brush dataset: " + error.Message);
        }
    }

    // Fail clearly for old callers instead of silently accepting obsolete, mismatched sources.
    public static BrushDatasetResult Prepare(string projectRoot, string? sparseModelOverride, bool replaceExisting, string? undistortedWorkspace = null)
    {
        if (!string.IsNullOrWhiteSpace(sparseModelOverride) || !string.IsNullOrWhiteSpace(undistortedWorkspace))
            return new(false, string.Empty, "Separate Sparse and Undistorted inputs are retired. Connect only the active Dataset.");
        return Prepare(projectRoot);
    }

    private static bool Targets(string link, string target)
    {
        if (!Directory.Exists(link)) return false;
        var resolved = new DirectoryInfo(link).ResolveLinkTarget(true);
        return resolved is not null && string.Equals(Path.TrimEndingDirectorySeparator(resolved.FullName),
            Path.TrimEndingDirectorySeparator(target), OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);
    }
    private static void CreateDirectoryLink(string link, string target)
    {
        try { Directory.CreateSymbolicLink(link, target); return; }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
        if (!OperatingSystem.IsWindows()) throw new IOException("The project volume must support directory links.");
        CreateJunction(link, target);
    }
''' + junction + '}\n'
p.write_text(s,encoding='utf-8')
