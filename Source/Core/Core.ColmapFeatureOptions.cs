namespace RhinoSplat.Core;

public sealed record ColmapFeatureOptions(int MaxImageSize = 3200, int MaxNumFeatures = 8192)
{
    public void Validate()
    {
        if (MaxImageSize <= 0) throw new ArgumentException("Extraction Max Image Size must be greater than zero.");
        if (MaxNumFeatures <= 0) throw new ArgumentException("Maximum Features must be greater than zero.");
    }
}
