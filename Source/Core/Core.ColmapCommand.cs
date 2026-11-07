using System.Collections.Generic;

namespace RhinoSplat.Core;

public sealed record ProcessCommand(string FileName, IReadOnlyList<string> Arguments);

public static class ColmapCommand
{
    public static ProcessCommand FeatureExtraction(string executable, RhinoSplatProject project) => new(
        executable,
        new[]
        {
            "feature_extractor",
            "--database_path", System.IO.Path.Combine(project.ColmapPath, "database.db"),
            "--image_path", project.ImagesPath
        });
}
