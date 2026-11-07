using System;
using System.Collections.Generic;
using System.IO;
using System.Numerics;

// RhinoSplat.Core stays independent from Rhino and Grasshopper. Both plug-ins
// will reference it later, while it remains directly testable from the CLI.

var project = new RhinoSplatProject(
    name: "FirstSmokeTest",
    rootPath: Path.Combine(Path.GetTempPath(), "RhinoSplat", "FirstSmokeTest"));

project.InitializeFolders();

var colmapPose = new ColmapPose(
    RotationWorldToCamera: Quaternion.Identity,
    TranslationWorldToCamera: new Vector3(2, 0, 0));

var cameraCenter = colmapPose.CameraCenter;
SmokeAssert.Equal(new Vector3(-2, 0, 0), cameraCenter,
    "COLMAP camera centre must be -R^T * t.");

var alignment = SimilarityTransform.Create(
    scale: 2.0f,
    rotation: Quaternion.Identity,
    translation: new Vector3(10, 0, 0));

SmokeAssert.Equal(new Vector3(6, 0, 0), alignment.ApplyPoint(cameraCenter),
    "Similarity alignment must transform camera centres consistently.");

var command = ColmapCommand.FeatureExtraction(
    colmapExe: "C:\\Tools\\COLMAP\\colmap.exe",
    databasePath: Path.Combine(project.RootPath, "database.db"),
    imagePath: project.ImagesPath);

SmokeAssert.Equal("feature_extractor", command.Arguments[0],
    "The first COLMAP argument should select the command.");
SmokeAssert.Equal(5, command.Arguments.Count,
    "COLMAP arguments must be structured, not shell-concatenated.");

Console.WriteLine("RhinoSplat.Core smoke test passed.");
Console.WriteLine($"Project root: {project.RootPath}");
Console.WriteLine($"Camera centre: {cameraCenter}");
Console.WriteLine($"Aligned centre: {alignment.ApplyPoint(cameraCenter)}");

public sealed class RhinoSplatProject
{
    public RhinoSplatProject(string name, string rootPath)
    {
        Name = string.IsNullOrWhiteSpace(name)
            ? throw new ArgumentException("A project name is required.", nameof(name))
            : name;
        RootPath = Path.GetFullPath(rootPath);
    }

    public string Name { get; }
    public string RootPath { get; }
    public string ImagesPath => Path.Combine(RootPath, "images");
    public string ColmapPath => Path.Combine(RootPath, "colmap");
    public string BrushPath => Path.Combine(RootPath, "brush");

    public void InitializeFolders()
    {
        Directory.CreateDirectory(RootPath);
        Directory.CreateDirectory(ImagesPath);
        Directory.CreateDirectory(ColmapPath);
        Directory.CreateDirectory(BrushPath);
    }
}

public readonly record struct ColmapPose(
    Quaternion RotationWorldToCamera,
    Vector3 TranslationWorldToCamera)
{
    // COLMAP stores world-to-camera: x_camera = R * x_world + t.
    public Vector3 CameraCenter => Vector3.Transform(
        -TranslationWorldToCamera,
        Quaternion.Inverse(RotationWorldToCamera));
}

public readonly record struct SimilarityTransform(
    float Scale,
    Quaternion Rotation,
    Vector3 Translation)
{
    public static SimilarityTransform Create(float scale, Quaternion rotation, Vector3 translation)
    {
        if (scale <= 0)
            throw new ArgumentOutOfRangeException(nameof(scale), "Scale must be positive.");

        return new SimilarityTransform(scale, Quaternion.Normalize(rotation), translation);
    }

    public Vector3 ApplyPoint(Vector3 point) => Vector3.Transform(point * Scale, Rotation) + Translation;
}

public sealed record ProcessCommand(string FileName, IReadOnlyList<string> Arguments);

public static class ColmapCommand
{
    public static ProcessCommand FeatureExtraction(string colmapExe, string databasePath, string imagePath) => new(
        colmapExe,
        new[] { "feature_extractor", "--database_path", databasePath, "--image_path", imagePath });
}

public static class SmokeAssert
{
    public static void Equal<T>(T expected, T actual, string message) where T : IEquatable<T>
    {
        if (!expected.Equals(actual))
            throw new InvalidOperationException($"{message} Expected {expected}; got {actual}.");
    }
}
