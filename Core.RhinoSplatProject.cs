using System;
using System.IO;
using System.Linq;

namespace RhinoSplat.Core;

public sealed class RhinoSplatProject
{
    private RhinoSplatProject(string name, string rootPath)
    {
        Name = name;
        RootPath = rootPath;
    }

    public string Name { get; }
    public string RootPath { get; }
    public string ImagesPath => Path.Combine(RootPath, "images");
    public string ColmapPath => Path.Combine(RootPath, "colmap");
    public string BrushPath => Path.Combine(RootPath, "brush");
    public string OutputPath => Path.Combine(RootPath, "output");

    public static RhinoSplatProject Create(string parentDirectory, string projectName)
    {
        if (string.IsNullOrWhiteSpace(parentDirectory))
            throw new ArgumentException("A parent directory is required.", nameof(parentDirectory));

        var safeName = SanitizeName(projectName);
        var rootPath = Path.Combine(Path.GetFullPath(parentDirectory), safeName);
        return new RhinoSplatProject(safeName, rootPath);
    }

    public void EnsureLayout()
    {
        var parent = Directory.GetParent(RootPath)?.FullName
            ?? throw new InvalidOperationException("The project root has no parent directory.");

        EnsureDirectory(parent, "parent folder");
        EnsureDirectory(RootPath, "project root");
        EnsureDirectory(ImagesPath, "images folder");
        EnsureDirectory(ColmapPath, "COLMAP folder");
        EnsureDirectory(BrushPath, "Brush folder");
        EnsureDirectory(OutputPath, "output folder");
    }

    private static void EnsureDirectory(string path, string label)
    {
        if (File.Exists(path))
            throw new InvalidOperationException($"The {label} is an existing file: {path}");

        Directory.CreateDirectory(path);
        if (!Directory.Exists(path))
            throw new IOException($"Could not create the {label}: {path}");
    }

    private static string SanitizeName(string name)
    {
        if (string.IsNullOrWhiteSpace(name))
            throw new ArgumentException("A project name is required.", nameof(name));

        var invalid = Path.GetInvalidFileNameChars();
        var sanitized = new string(name.Trim().Select(c => invalid.Contains(c) ? '_' : c).ToArray());
        return string.IsNullOrWhiteSpace(sanitized) ? "SplatypusProject" : sanitized;
    }
}
