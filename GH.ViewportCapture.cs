using System.Drawing;
using System.Diagnostics;
using System.Buffers.Binary;
using System.Text;
using System.Text.Json;
using Rhino;
using Rhino.Display;
using Rhino.DocObjects;
using Rhino.Geometry;

namespace RhinoSplat.Grasshopper;

internal sealed record CapturedCamera(Plane Frame, double Fx, double Fy, double Cx, double Cy);

/// <summary>UI-thread-only live camera animation. Each step returns to Rhino's event loop.</summary>
internal sealed class ViewportCaptureSession : IDisposable
{
    private static bool _capturing;
    private readonly RhinoDoc _document;
    private readonly RhinoView _view;
    private readonly ViewportInfo _original;
    private readonly Point3d _target;
    private readonly Size _size;
    private readonly Size _originalSize;
    private readonly Rectangle _originalScreenRectangle;
    private readonly bool _originalTitleVisible;
    private readonly DisplayModeDescription _mode;
    private readonly DisplayModeDescription _originalMode;
    private readonly Color _background;
    private readonly Color? _backgroundTop;
    private readonly double _settleSeconds;
    private Guid _captureModeId;
    private ViewportInfo? _pendingProjection;
    private readonly Stopwatch _settleClock = new();
    private bool _commandPending, _commandEnded;
    private Rhino.Commands.Result _commandResult;
    private int _liveDraws;
    private readonly Plane[] _frames;
    private readonly double _lens;
    private readonly List<CapturedCamera> _cameras = new();
    private readonly string _stage;
    private bool _disposed;

    public string DatasetPath { get; private set; }
    public int Count => _cameras.Count;
    public int Total => _frames.Length;
    public bool Complete { get; private set; }
    public string Phase { get; private set; } = "Waiting to move the live camera";
    public static bool IsCapturing => _capturing;
    public bool IsSaving => _commandPending && !_commandEnded;
    public Size CaptureSize => _size;

    internal static bool IsCaptureMode(DisplayModeDescription mode) =>
        mode.EnglishName.StartsWith("SP Capture ", StringComparison.Ordinal)
        && Guid.TryParseExact(mode.EnglishName[11..], "N", out _);

    internal static void CleanupAbandonedModes()
    {
        if (_capturing) return;
        var abandoned = DisplayModeDescription.GetDisplayModes().Where(IsCaptureMode).ToArray();
        var ids = abandoned.Select(m => m.Id).ToHashSet();
        foreach (var doc in RhinoDoc.OpenDocuments())
            foreach (var view in doc.Views.GetViewList(ViewTypeFilter.Model))
                if (ids.Contains(view.ActiveViewport.DisplayMode.Id))
                {
                    view.ActiveViewport.DisplayMode = DisplayModeDescription.GetDisplayMode(DisplayModeDescription.RenderedId);
                    view.Redraw();
                }
        foreach (var mode in abandoned)
            if (!DisplayModeDescription.DeleteDisplayMode(mode.Id)
                && DisplayModeDescription.GetDisplayMode(mode.Id) is not null)
                throw new InvalidOperationException("Rhino could not remove temporary display mode " + mode.EnglishName);
    }

    internal static Size FitCaptureSize(Size requested, Size available)
    {
        var a = requested.Width; var b = requested.Height;
        while (b != 0) { var remainder = a % b; a = b; b = remainder; }
        var unitWidth = requested.Width / a; var unitHeight = requested.Height / a;
        var scale = Math.Min(a, Math.Min(available.Width / unitWidth, available.Height / unitHeight));
        if (scale < 1) throw new ArgumentException("The requested aspect ratio cannot fit this viewport in whole pixels. Enlarge the viewport or use a simpler ratio, such as 3:2 or 16:9.");
        return new Size(unitWidth * scale, unitHeight * scale);
    }

    internal static DisplayModeDescription FindMode(string name)
    {
        var mode = DisplayModeDescription.GetDisplayModes().FirstOrDefault(m =>
            string.Equals(m.EnglishName, name.Trim(), StringComparison.OrdinalIgnoreCase)
            || string.Equals(m.LocalName, name.Trim(), StringComparison.OrdinalIgnoreCase)
            || Guid.TryParse(name, out var id) && m.Id == id);
        if (mode is null) throw new ArgumentException("Display Mode must be an installed Rhino mode name or ID (for example Rendered or Shaded).");
        // A single raster pass cannot guarantee convergence for a progressive renderer.
        if (mode.Id == DisplayModeDescription.RaytracedId || mode.DisplayAttributes.RealtimeDisplayId != Guid.Empty)
            throw new ArgumentException("Progressive/Raytraced modes are not supported for batch capture. Choose Rendered, Shaded or another raster display mode.");
        return mode;
    }

    internal ViewportCaptureSession(RhinoDoc document, RhinoView view, IReadOnlyList<Plane> frames,
        int width, int height, double lens, string displayMode, Color background, string folder, double settleSeconds = 1.0, Color? backgroundTop = null)
    {
        if (_capturing) throw new InvalidOperationException("Another SP View Capture is running. Wait for it or cancel it first.");
        CleanupAbandonedModes();
        if (frames.Count == 0 || frames.Any(f => !f.IsValid)) throw new ArgumentException("Frames must contain valid camera planes: origin at the camera, X right, Y up, looking along negative Z.");
        if (width < 16 || height < 16 || width > 16384 || height > 16384 || (long)width * height > 67108864)
            throw new ArgumentException("Width and Height must be 16–16384 pixels, with at most 64 megapixels per capture.");
        if (!double.IsFinite(lens) || lens < 1 || lens > 1000) throw new ArgumentException("Lens must be between 1 and 1000 mm (35 mm equivalent).");
        if (background.A != 255) throw new ArgumentException("Background must be opaque. Use a solid color for consistent training captures.");
        if (backgroundTop is { A: not 255 }) throw new ArgumentException("Background Top must be opaque.");
        if (!double.IsFinite(settleSeconds) || settleSeconds < 0.1 || settleSeconds > 60)
            throw new ArgumentException("Wait must be between 0.1 and 60 seconds per camera frame.");
        if (string.IsNullOrWhiteSpace(folder) || !Path.IsPathFullyQualified(folder)) throw new ArgumentException("Folder must be an absolute saving path.");
        if (folder.IndexOfAny(new[] { '"', '\r', '\n' }) >= 0) throw new ArgumentException("Folder cannot contain quotes or line breaks.");
        if (view is RhinoPageView || view.ActiveViewport.LockedProjection) throw new InvalidOperationException("Activate an unlocked Rhino model viewport before capturing.");
        _mode = FindMode(displayMode);
        _originalMode = view.ActiveViewport.DisplayMode;
        _background = background; _backgroundTop = backgroundTop; _settleSeconds = settleSeconds;
        _document = document; _view = view; _frames = frames.ToArray(); _lens = lens;
        _originalSize = view.Size;
        _originalScreenRectangle = view.ScreenRectangle;
        _size = FitCaptureSize(new Size(width, height), _originalScreenRectangle.Size);
        _originalTitleVisible = view.TitleVisible;
        _original = new ViewportInfo(view.ActiveViewport); _target = view.ActiveViewport.CameraTarget;
        DatasetPath = Path.GetFullPath(folder);
        _stage = DatasetPath;
        try
        {
            foreach (var relative in new[] { "images", "colmap", Path.Combine("brush", "dataset") })
            {
                var destination = Path.Combine(DatasetPath, relative);
                if (File.Exists(destination) || Directory.Exists(destination) &&
                    ((File.GetAttributes(destination) & FileAttributes.ReparsePoint) != 0 || Directory.EnumerateFileSystemEntries(destination).Any()))
                    throw new IOException($"Root already contains data in {relative}. Choose a fresh project root to avoid overwriting an existing dataset.");
            }
            foreach (var name in new[] { "transforms.json", "capture-project.json", "capture-progress.txt" })
                if (File.Exists(Path.Combine(DatasetPath, name))) throw new IOException("Root contains a previous capture. Choose a fresh project root.");
            Directory.CreateDirectory(Path.Combine(DatasetPath, "images"));
            Directory.CreateDirectory(Path.Combine(DatasetPath, "colmap"));
            Directory.CreateDirectory(Path.Combine(DatasetPath, "brush"));
            File.WriteAllText(Path.Combine(DatasetPath, "capture-project.json"), "{\"schema\":1,\"brushDataset\":\"brush/dataset\"}");
            TraceProgress("Waiting for the first UI capture callback");
        }
        catch { _original.Dispose(); throw; }
        _capturing = true;
        DisplayPipeline.DrawForeground += OnLiveDraw;
    }

    internal bool CaptureNext()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (Complete) return true;
        if (IsSaving) return false;
        if (RhinoDoc.ActiveDoc?.RuntimeSerialNumber != _document.RuntimeSerialNumber
            || _document.Views.Find(_view.ActiveViewportID) is null)
            throw new InvalidOperationException("The capture viewport was closed or the active Rhino document changed.");
        var frame = _frames[Count];
        var viewport = _view.ActiveViewport;
        if (viewport.LockedProjection) throw new InvalidOperationException("The capture camera was locked. Unlock it and start again.");
        if (_pendingProjection is null)
        {
            if (_captureModeId == Guid.Empty)
            {
                TraceProgress("Preparing live viewport display mode and size");
                _captureModeId = DisplayModeDescription.CopyDisplayMode(_mode.Id, "SP Capture " + Guid.NewGuid().ToString("N"));
                if (_captureModeId == Guid.Empty) throw new InvalidOperationException("Rhino could not create the temporary capture display mode.");
                var liveMode = DisplayModeDescription.GetDisplayMode(_captureModeId);
                liveMode.InMenu = false;
                liveMode.AllowObjectAssignment = false;
                var attributes = liveMode.DisplayAttributes;
                if (_backgroundTop is { } gradientTop) attributes.SetFill(gradientTop, _background);
                else attributes.SetFill(_background);
                attributes.IgnoreHighlights = true;
                attributes.DisableConduits = false;
                attributes.ViewSpecificAttributes.UseDocumentGrid = false;
                attributes.ViewSpecificAttributes.DrawGrid = false;
                attributes.ViewSpecificAttributes.DrawGridAxes = false;
                attributes.ViewSpecificAttributes.DrawWorldAxes = false;
                DisplayModeDescription.UpdateDisplayMode(liveMode);
                viewport.DisplayMode = DisplayModeDescription.GetDisplayMode(_captureModeId);
                // A screen capture cannot render outside the visible client area.
                // Only shrink within the original viewport; never enlarge a docked
                // child window across adjacent Rhino toolbars or other viewports.
                _view.Size = _size;
                _view.TitleVisible = false;
            }
            _document.Views.ActiveView = _view;
            using var projection = new ViewportInfo(viewport);
            projection.ChangeToPerspectiveProjection(RhinoMath.UnsetValue, true, _lens);
            projection.FrustumAspect = (double)_size.Width / _size.Height;
            projection.Camera35mmLensLength = _lens;
            var forward = -frame.ZAxis;
            var bridge = Vector3d.CrossProduct(projection.CameraDirection, forward);
            if (!bridge.Unitize()) bridge = frame.XAxis;
            TraceProgress($"Moving live camera to frame {Count + 1} / {Total}");
            if (!projection.SetCameraLocation(frame.Origin) || !projection.SetCameraUp(bridge)
                || !projection.SetCameraDirection(forward) || !projection.SetCameraUp(frame.YAxis)
                || !viewport.SetViewProjection(projection, false))
                throw new InvalidOperationException("Rhino could not apply a capture camera.");
            _pendingProjection = new ViewportInfo(viewport);
            _liveDraws = 0;
            _settleClock.Restart();
            _view.Redraw();
            TraceProgress($"Waiting {_settleSeconds:0.##} seconds for the live renderer, frame {Count + 1} / {Total}");
            return false;
        }

        // Never pump events or sleep inside a draw callback. Return to the normal
        // UI loop so renderer idle handlers and asynchronous GPU updates can run.
        if (_document.Views.ActiveView?.ActiveViewportID != _view.ActiveViewportID
            || !_originalScreenRectangle.Contains(_view.ScreenRectangle)
            || viewport.Size != _size || viewport.DisplayMode.Id != _captureModeId
            || viewport.CameraLocation.DistanceTo(_pendingProjection.CameraLocation) > 1e-8
            || (viewport.CameraDirection - _pendingProjection.CameraDirection).Length > 1e-8
            || (viewport.CameraUp - _pendingProjection.CameraUp).Length > 1e-8
            || Math.Abs(viewport.Camera35mmLensLength - _pendingProjection.Camera35mmLensLength) > 1e-6)
            throw new InvalidOperationException("The live capture view changed. Keep its camera, size and display mode unchanged and pause other camera animation during capture.");
        if (_liveDraws == 0 && _settleClock.Elapsed.TotalSeconds > 30)
            throw new InvalidOperationException("Rhino has not drawn the live capture view. Make Rhino and its capture viewport visible, then start again.");
        if (_liveDraws == 0 || _settleClock.Elapsed.TotalSeconds < _settleSeconds)
        {
            _view.Redraw();
            return false;
        }
        var path = Path.Combine(_stage, "images", ImageName(Count));
        if (!_commandPending)
        {
            if (RhinoApp.InCommand != 0) throw new InvalidOperationException("Finish the active Rhino command before capturing views.");
            if (File.Exists(path) || Directory.Exists(path)) throw new IOException("The capture image path already exists: " + path);
            TraceProgress($"Saving frame {Count + 1} / {Total} at {_size.Width} x {_size.Height} with Rhino ScreenCaptureToFile");
            var command = $"_-ScreenCaptureToFile \"{path}\" _Enter";
            _commandPending = true; _commandEnded = false;
            Rhino.Commands.Command.EndCommand += OnCaptureCommandEnded;
            // Outside a script-runner command Rhino may queue this script and
            // return immediately. Its EndCommand event is the completion signal.
            if (!RhinoApp.RunScript(_document.RuntimeSerialNumber, command, false))
            {
                _commandPending = false;
                Rhino.Commands.Command.EndCommand -= OnCaptureCommandEnded;
                throw new InvalidOperationException("Rhino could not start ScreenCaptureToFile.");
            }
            return false;
        }
        Rhino.Commands.Command.EndCommand -= OnCaptureCommandEnded;
        _commandPending = false;
        if (_commandResult != Rhino.Commands.Result.Success || !File.Exists(path))
            throw new InvalidOperationException("Rhino ScreenCaptureToFile did not save the image. The command may have been cancelled or the renderer could not capture the live view.");
        using (var bitmap = new Bitmap(path))
        {
            // ScreenCaptureToFile includes the symmetric native window frame.
            // Remove only that border: never resample the calibrated view pixels.
            var dx = bitmap.Width - _size.Width; var dy = bitmap.Height - _size.Height;
            if (dx < 0 || dy < 0 || dx > 32 || dy > 32 || dx % 2 != 0 || dy % 2 != 0)
                throw new InvalidOperationException($"Rhino screen capture returned {bitmap.Size}, expected {_size} plus its window border.");
            if (dx != 0 || dy != 0)
            {
                using var cropped = bitmap.Clone(new Rectangle(dx / 2, dy / 2, _size.Width, _size.Height), bitmap.PixelFormat);
                cropped.Save(path + ".crop.png", System.Drawing.Imaging.ImageFormat.Png);
            }
        }
        if (File.Exists(path + ".crop.png")) File.Move(path + ".crop.png", path, true);
        // Most scene images are already large enough. Avoid reading and writing
        // a full-resolution image again just to add metadata for tiny PNGs.
        if (new FileInfo(path).Length < 16387) PadTrainingPng(File.ReadAllBytes(path), path);
        _pendingProjection.GetFrustum(out var left, out var right, out var bottom, out var top, out var near, out _);
        _cameras.Add(new(frame, _size.Width * near / (right - left), _size.Height * near / (top - bottom),
            -left * _size.Width / (right - left), top * _size.Height / (top - bottom)));
        _pendingProjection.Dispose(); _pendingProjection = null;
        TraceProgress("Frame saved");
        if (Count < Total) return false;
        // Once all images exist, a bookkeeping failure must never delete them.

        try
        {
            TraceProgress("Writing camera dataset");
            WriteModel(_stage, _size, _cameras);
            var brush = RhinoSplat.Core.BrushDatasetPreparer.Prepare(DatasetPath);
            if (!brush.Succeeded) throw new IOException(brush.Status);
            TraceProgress("Complete: images, camera model and brush/dataset are ready");
        }
        catch (Exception ex) { throw new IOException($"All {Count} captures are preserved at {_stage}. Camera dataset finalization failed: {ex.Message}", ex); }
        Complete = true;
        return true;
    }

    private void OnCaptureCommandEnded(object? sender, Rhino.Commands.CommandEventArgs e)
    {
        if (e.DocumentRuntimeSerialNumber == _document.RuntimeSerialNumber
            && string.Equals(e.CommandEnglishName, "ScreenCaptureToFile", StringComparison.OrdinalIgnoreCase))
        { _commandResult = e.CommandResult; _commandEnded = true; }
    }

    private void OnLiveDraw(object? sender, DrawEventArgs e)
    {
        if (_pendingProjection is not null && !e.Display.IsInViewCapture
            && e.Viewport.Id == _view.ActiveViewportID
            && e.Viewport.CameraLocation.DistanceTo(_pendingProjection.CameraLocation) < 1e-8
            && (e.Viewport.CameraDirection - _pendingProjection.CameraDirection).Length < 1e-8)
        {
            if (_liveDraws == 0) _settleClock.Restart();
            _liveDraws++;
        }
    }

    internal static string ImageName(int index) => $"sp_capture_{index + 1:D6}.png";

    private void TraceProgress(string phase)
    {
        Phase = phase;
        File.WriteAllText(Path.Combine(_stage, "capture-progress.txt"),
            $"{DateTimeOffset.Now:O}\nSaved {Count} / {Total} images\n{phase}\n");
    }

    private static void PadTrainingPng(byte[] bytes, string path)
    {
        // Pinned Brush 0.3.0 reads exactly 16387 bytes when probing image headers.
        // Small, valid PNGs otherwise fail with early EOF. Add a legal ancillary
        // text chunk before IEND; pixels and all image data remain unchanged.
        const int minimumBytes = 16387;
        if (bytes.Length >= minimumBytes) { File.WriteAllBytes(path, bytes); return; }
        var payload = new byte[Math.Max(32, minimumBytes - bytes.Length - 12)];
        Array.Fill(payload, (byte)' ');
        Encoding.ASCII.GetBytes("Splatypus\0Viewport capture").CopyTo(payload, 0);
        var chunk = new byte[payload.Length + 12];
        BinaryPrimitives.WriteUInt32BigEndian(chunk, (uint)payload.Length);
        Encoding.ASCII.GetBytes("tEXt").CopyTo(chunk, 4); payload.CopyTo(chunk, 8);
        var crc = 0xffffffffU;
        foreach (var b in chunk.AsSpan(4, payload.Length + 4))
        {
            crc ^= b;
            for (var bit = 0; bit < 8; bit++) crc = (crc >> 1) ^ ((crc & 1) != 0 ? 0xedb88320U : 0);
        }
        BinaryPrimitives.WriteUInt32BigEndian(chunk.AsSpan(chunk.Length - 4), ~crc);
        using var file = File.Create(path);
        file.Write(bytes.AsSpan(0, bytes.Length - 12)); file.Write(chunk); file.Write(bytes.AsSpan(bytes.Length - 12));
    }

    internal static void WriteModel(string root, Size size, IReadOnlyList<CapturedCamera> cameras)
    {
        var binary = Path.Combine(root, "colmap", "sparse", "0");
        var text = Path.Combine(root, "colmap", "sparse-text");
        Directory.CreateDirectory(binary); Directory.CreateDirectory(text);
        using var cb = new BinaryWriter(File.Create(Path.Combine(binary, "cameras.bin")));
        using var ib = new BinaryWriter(File.Create(Path.Combine(binary, "images.bin")));
        using var pb = new BinaryWriter(File.Create(Path.Combine(binary, "points3D.bin")));
        using var ct = new StreamWriter(Path.Combine(text, "cameras.txt"), false, new UTF8Encoding(false));
        using var it = new StreamWriter(Path.Combine(text, "images.txt"), false, new UTF8Encoding(false));
        File.WriteAllText(Path.Combine(text, "points3D.txt"), "# View capture has known cameras; no reconstructed sparse points.\n");
        cb.Write((ulong)cameras.Count); ib.Write((ulong)cameras.Count); pb.Write(0UL);
        for (var i = 0; i < cameras.Count; i++)
        {
            var c = cameras[i]; var f = c.Frame; var id = i + 1;
            // Rhino camera planes are right/up/back; COLMAP is right/down/forward.
            var camera = new Plane(f.Origin, f.XAxis, -f.YAxis);
            var q = Quaternion.Rotation(camera, Plane.WorldXY);
            var centre = new Vector3d(f.Origin);
            var tx = -(camera.XAxis * centre); var ty = -(camera.YAxis * centre); var tz = -(camera.ZAxis * centre);
            cb.Write(id); cb.Write(1); // COLMAP PINHOLE model ID.
            cb.Write((ulong)size.Width); cb.Write((ulong)size.Height);
            foreach (var n in new[] { c.Fx, c.Fy, c.Cx, c.Cy }) cb.Write(n);
            ib.Write(id);
            foreach (var n in new[] { q.A, q.B, q.C, q.D, tx, ty, tz }) ib.Write(n);
            ib.Write(id); ib.Write(Encoding.UTF8.GetBytes(ImageName(i))); ib.Write((byte)0); ib.Write(0UL);
            ct.WriteLine(FormattableString.Invariant($"{id} PINHOLE {size.Width} {size.Height} {c.Fx:R} {c.Fy:R} {c.Cx:R} {c.Cy:R}"));
            it.WriteLine(FormattableString.Invariant($"{id} {q.A:R} {q.B:R} {q.C:R} {q.D:R} {tx:R} {ty:R} {tz:R} {id} {ImageName(i)}\n"));
        }
        // Also provide an OpenGL camera-to-world dataset for NeRF-style trainers.
        var first = cameras[0];
        var manifest = new
        {
            w = size.Width, h = size.Height, fl_x = first.Fx, fl_y = first.Fy, cx = first.Cx, cy = first.Cy,
            camera_angle_x = 2 * Math.Atan(size.Width / (2 * first.Fx)),
            camera_angle_y = 2 * Math.Atan(size.Height / (2 * first.Fy)),
            frames = cameras.Select((c, i) => new
            {
                file_path = "images/" + ImageName(i), fl_x = c.Fx, fl_y = c.Fy, cx = c.Cx, cy = c.Cy,
                transform_matrix = new[] {
                    new[] { c.Frame.XAxis.X, c.Frame.YAxis.X, c.Frame.ZAxis.X, c.Frame.OriginX },
                    new[] { c.Frame.XAxis.Y, c.Frame.YAxis.Y, c.Frame.ZAxis.Y, c.Frame.OriginY },
                    new[] { c.Frame.XAxis.Z, c.Frame.YAxis.Z, c.Frame.ZAxis.Z, c.Frame.OriginZ },
                    new[] { 0.0, 0.0, 0.0, 1.0 } }
            }).ToArray()
        };
        File.WriteAllText(Path.Combine(root, "transforms.json"), JsonSerializer.Serialize(manifest, new JsonSerializerOptions { WriteIndented = true }));
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Rhino.Commands.Command.EndCommand -= OnCaptureCommandEnded;
        DisplayPipeline.DrawForeground -= OnLiveDraw;
        try
        {
            if (_document.Views.Find(_view.ActiveViewportID) is not null)
            {
                _view.Size = _originalSize;
                _view.TitleVisible = _originalTitleVisible;
                _view.ActiveViewport.SetViewProjection(_original, false);
                _view.ActiveViewport.SetCameraTarget(_target, false);
                _view.ActiveViewport.DisplayMode = _originalMode;
                _view.Redraw();
            }
        }
        finally
        {
            _pendingProjection?.Dispose(); _pendingProjection = null;
            _original.Dispose(); _capturing = false;
            // Also recover leftovers from interrupted runs; check deletion instead
            // of silently ignoring Rhino's false return value.
            CleanupAbandonedModes();
            // This unique staging folder was created by this session only.
            // Root belongs to the user. Keep partial captures for inspection;
            // never delete a project directory on cancellation or failure.
        }
    }
}
