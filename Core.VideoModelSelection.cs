using System.Globalization;
using System.Text.RegularExpressions;

namespace RhinoSplat.Core;

public static class VideoModelSelection
{
    public const string RevisionFile = "source-revision.txt";

    // The binary pair is authoritative; re-exporting identical text must not retire an alignment.
    public static string Revision(string root)
    {
        var dataset = new ActiveDataset(root);
        var files = new[] { Path.Combine(dataset.Sparse, "cameras.bin"), Path.Combine(dataset.Sparse, "images.bin") };
        if (!files.All(File.Exists)) files = new[] { Path.Combine(dataset.Text, "cameras.txt"), Path.Combine(dataset.Text, "images.txt") };
        return string.Join("\n", files.Select(path =>
        {
            var info = new FileInfo(path);
            return info.Exists ? Path.GetFileName(path) + ":" + info.Length.ToString(CultureInfo.InvariantCulture) + ":" + info.LastWriteTimeUtc.Ticks.ToString(CultureInfo.InvariantCulture) : "missing";
        }));
    }

    public static string Resolve(string root, out string status)
    {
        var dataset = new ActiveDataset(root);
        var aligned = AlignedTextModels(dataset.Colmap)
            .OrderByDescending(path => File.GetLastWriteTimeUtc(Path.Combine(path, "images.txt")))
            .ThenByDescending(path => File.GetLastWriteTimeUtc(Path.Combine(path, "cameras.txt")))
            .ThenBy(path => path, StringComparer.Ordinal)
            .FirstOrDefault();
        if (aligned is not null)
        {
            // Existing alignments are authoritative for playback. Copied files, text
            // exports and old revision records must not silently select the base model.
            status = $"Using aligned model: {aligned}";
            return aligned;
        }
        status = $"Using base model (no complete aligned text model found): {dataset.Text}";
        return dataset.Text;
    }

    public static string Resolve(string root, string? modelOverride, out string status)
    {
        if (string.IsNullOrWhiteSpace(modelOverride)) return Resolve(root, out status);
        var supplied = Path.GetFullPath(modelOverride.Trim());
        if (File.Exists(supplied)) supplied = Path.GetDirectoryName(supplied)!;
        string? selected = null;
        if (Complete(supplied)) selected = supplied;
        else if (Directory.Exists(Path.Combine(supplied, "colmap")))
        {
            var candidate = Resolve(supplied, out _);
            if (Complete(candidate)) selected = candidate;
        }
        else selected = new[] { Path.Combine(supplied, "text"), Path.Combine(supplied, "sparse-text") }.FirstOrDefault(Complete);
        if (selected is null) throw new InvalidDataException($"Specified Model has no complete COLMAP text camera model: {supplied}. Select a folder containing images.txt and cameras.txt, or export a binary model to text first.");
        status = $"Using specified model: {selected}";
        return selected;
    }

    public static bool IsAlignedFolder(string name) =>
        Regex.IsMatch(name, @"^(aligned|alignment)(?:$|[\s_.-]|model)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static IEnumerable<string> AlignedTextModels(string colmap)
    {
        if (!Directory.Exists(colmap)) yield break;
        var pending = new Stack<(string Path, bool Aligned)>();
        pending.Push((colmap, false));
        var options = new EnumerationOptions { IgnoreInaccessible = true, AttributesToSkip = FileAttributes.ReparsePoint };
        while (pending.Count > 0)
        {
            var (path, aligned) = pending.Pop();
            if (aligned && Complete(path)) yield return path;
            foreach (var child in Directory.EnumerateDirectories(path, "*", options))
            {
                var name = Path.GetFileName(child);
                // Do not let temporary or archived models supersede the live output.
                if (name.StartsWith('.') || Regex.IsMatch(name, @"(?:^|[\s_.-])(backup|backups|archive|archives|staging|stage|tmp|temp)(?:$|[\s_.-])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)) continue;
                pending.Push((child, aligned || IsAlignedFolder(name)));
            }
        }
    }

    private static bool Complete(string path) => new[] { "images.txt", "cameras.txt" }
        .All(name => { var info = new FileInfo(Path.Combine(path, name)); return info.Exists && info.Length > 0; });
}
