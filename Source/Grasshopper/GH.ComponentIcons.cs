using System;
using System.Collections.Generic;
using System.Drawing;

namespace RhinoSplat.Grasshopper;

/// <summary>Shared, embedded 24-pixel component artwork. Kept alive for the assembly lifetime.</summary>
internal static class ComponentIcons
{
    private static readonly Dictionary<string, Bitmap> Cache = new(StringComparer.Ordinal);

    internal static Bitmap Get(string name)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(name, out var cached))
                return cached;

            var resource = $"RhinoSplat.Icons.{name}.png";
            using var stream = typeof(ComponentIcons).Assembly.GetManifestResourceStream(resource)
                ?? throw new InvalidOperationException($"Missing component icon: {resource}");
            using var source = new Bitmap(stream);
            // Detach the returned bitmap from the disposed resource stream.
            var icon = new Bitmap(source);
            Cache.Add(name, icon);
            return icon;
        }
    }
}
