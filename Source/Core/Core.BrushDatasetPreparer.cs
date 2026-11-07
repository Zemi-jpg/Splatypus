using System.Diagnostics;

namespace RhinoSplat.Core;

public sealed record BrushDatasetResult(bool Succeeded, string DatasetPath, string Status);

public static class BrushDatasetPreparer
{
    public static BrushDatasetResult Prepare(string projectRoot, string? sparseModelOverride = null)
    {
        try
        {
            var active = new ActiveDataset(projectRoot);
            using var lease = DatasetLease.Acquire(active.Root);
            active.Validate();
            var hasOverride = !string.IsNullOrWhiteSpace(sparseModelOverride);
            var model = hasOverride ? Path.GetFullPath(sparseModelOverride!) : active.Sparse;
            ActiveDataset.EnsureWithin(model, active.Colmap);
            ActiveDataset.ValidatePair(active.Images, model);
            var dataset = File.Exists(Path.Combine(active.Root, "capture-project.json"))
                ? Path.Combine(active.Root, "brush", "dataset")
                : Path.Combine(active.Root, "brush-dataset");
            Directory.CreateDirectory(Path.GetDirectoryName(dataset)!);
            var sparse = Path.GetDirectoryName(active.Sparse)!;
            // Link both directories, so existing Brush wiring follows every successful replacement.
            if (Targets(Path.Combine(dataset, "images"), active.Images)
                && (hasOverride ? Targets(Path.Combine(dataset, "sparse", "0"), model) : Targets(Path.Combine(dataset, "sparse"), sparse)))
                return new(true, dataset, "Brush is ready with the selected model and active images.");
            using var transaction = new DatasetTransaction(active);
            var prepared = Path.Combine(transaction.Work, "brush-dataset");
            Directory.CreateDirectory(prepared);
            CreateDirectoryLink(Path.Combine(prepared, "images"), active.Images);
            if (hasOverride)
            {
                Directory.CreateDirectory(Path.Combine(prepared, "sparse"));
                CreateDirectoryLink(Path.Combine(prepared, "sparse", "0"), model);
            }
            else CreateDirectoryLink(Path.Combine(prepared, "sparse"), sparse);
            transaction.Commit(CancellationToken.None, (prepared, dataset));
            return new(true, dataset, hasOverride
                ? "Brush is linked to the separate model and active images. Later alignment writes update this model link."
                : "Brush is linked to the active images and base model. Undistortion updates this dataset automatically.");
        }
        catch (Exception error)
        {
            return new(false, string.Empty, "Could not prepare Brush dataset: " + error.Message);
        }
    }

    // Fail clearly for old callers instead of silently accepting obsolete, mismatched sources.
    public static BrushDatasetResult Prepare(string projectRoot, string? sparseModelOverride, bool replaceExisting, string? undistortedWorkspace = null)
    {
        if (!string.IsNullOrWhiteSpace(undistortedWorkspace))
            return new(false, string.Empty, "Separate Undistorted input is retired. Undistortion replaces the active dataset automatically.");
        return Prepare(projectRoot, sparseModelOverride);
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
    private static void CreateJunction(string linkPath, string targetPath)
    {
        var info = new ProcessStartInfo
        {
            FileName = Environment.GetEnvironmentVariable("ComSpec") ?? "cmd.exe",
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };
        // Quoted environment expansion keeps spaces and cmd metacharacters in paths literal.
        info.Environment["SPLATYPUS_LINK"] = linkPath;
        info.Environment["SPLATYPUS_TARGET"] = targetPath;
        info.Arguments = "/d /v:off /c mklink /J \"%SPLATYPUS_LINK%\" \"%SPLATYPUS_TARGET%\"";
        using var process = Process.Start(info) ?? throw new InvalidOperationException("Could not start cmd.exe to create the image junction.");
        var error = process.StandardError.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0 || !Directory.Exists(linkPath))
            throw new IOException($"Could not create image directory junction. {error}".Trim());
    }

}
