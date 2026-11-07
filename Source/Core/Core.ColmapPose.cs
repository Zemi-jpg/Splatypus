using System.Numerics;

namespace RhinoSplat.Core;

public readonly record struct ColmapPose(
    Quaternion RotationWorldToCamera,
    Vector3 TranslationWorldToCamera)
{
    // COLMAP uses x_camera = R * x_world + t.
    public Vector3 CameraCenter => Vector3.Transform(
        -TranslationWorldToCamera,
        Quaternion.Inverse(RotationWorldToCamera));
}
