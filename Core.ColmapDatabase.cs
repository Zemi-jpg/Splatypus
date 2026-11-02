using System.Runtime.InteropServices;

namespace RhinoSplat.Core;

public sealed record ColmapImagePrior(string Name, double? X, double? Y)
{
    public bool HasPosition => X.HasValue && Y.HasValue && double.IsFinite(X.Value)
        && double.IsFinite(Y.Value) && (X != 0 || Y != 0);
}

/// <summary>Reads existing COLMAP priors using the OS SQLite library; never writes the database.</summary>
public static class ColmapDatabase
{
    public static IReadOnlyList<ColmapImagePrior> ReadImages(string path)
    {
        var api = Api.Instance.Value;
        IntPtr database = IntPtr.Zero;
        try
        {
            if (api.Open(Path.GetFullPath(path), out database, 1, IntPtr.Zero) != 0)
                throw new IOException("Could not read COLMAP's database.");
            // 3.11 stores positions as three doubles in pose_priors; older versions used images.prior_t*.
            const string current = "SELECT images.name, pose_priors.position FROM images LEFT JOIN pose_priors USING(image_id) ORDER BY images.name";
            if (api.Prepare(database, current, -1, out var statement, IntPtr.Zero) == 0)
                return Read(statement, false);
            if (statement != IntPtr.Zero) api.Finalize(statement);
            const string legacy = "SELECT name, prior_tx, prior_ty FROM images ORDER BY name";
            if (api.Prepare(database, legacy, -1, out statement, IntPtr.Zero) != 0)
            {
                if (statement != IntPtr.Zero) api.Finalize(statement);
                throw new IOException("Unsupported COLMAP position-prior schema. Use Exhaustive or Sequential matching.");
            }
            return Read(statement, true);
        }
        finally { if (database != IntPtr.Zero) api.Close(database); }

        List<ColmapImagePrior> Read(IntPtr statement, bool legacy)
        {
            var images = new List<ColmapImagePrior>();
            try
            {
                int code;
                while ((code = api.Step(statement)) == 100)
                {
                    var name = Marshal.PtrToStringUTF8(api.Text(statement, 0)) ?? "";
                    double? x = null, y = null;
                    if (legacy && api.Type(statement, 1) != 5 && api.Type(statement, 2) != 5)
                    { x = api.Double(statement, 1); y = api.Double(statement, 2); }
                    else if (!legacy && api.Bytes(statement, 1) == 24)
                    {
                        var bytes = new byte[24]; Marshal.Copy(api.Blob(statement, 1), bytes, 0, bytes.Length);
                        x = BitConverter.ToDouble(bytes, 0); y = BitConverter.ToDouble(bytes, 8);
                    }
                    images.Add(new(name, x, y));
                }
                if (code != 101) throw new IOException("Could not finish reading COLMAP image positions; the database may be busy.");
                return images;
            }
            finally { api.Finalize(statement); }
        }
    }

    private sealed class Api
    {
        internal static readonly Lazy<Api> Instance = new(() => new Api());
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int OpenDelegate([MarshalAs(UnmanagedType.LPUTF8Str)] string path, out IntPtr database, int flags, IntPtr vfs);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int PointerDelegate(IntPtr value);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int PrepareDelegate(IntPtr database, [MarshalAs(UnmanagedType.LPUTF8Str)] string sql, int length, out IntPtr statement, IntPtr tail);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate IntPtr ColumnPointer(IntPtr statement, int column);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate int ColumnInteger(IntPtr statement, int column);
        [UnmanagedFunctionPointer(CallingConvention.Cdecl)] internal delegate double ColumnDouble(IntPtr statement, int column);
        internal readonly OpenDelegate Open;
        internal readonly PointerDelegate Close, Step, Finalize;
        internal readonly PrepareDelegate Prepare;
        internal readonly ColumnPointer Text, Blob;
        internal readonly ColumnInteger Bytes, Type;
        internal readonly ColumnDouble Double;
        private Api()
        {
            var library = NativeLibrary.Load(OperatingSystem.IsWindows() ? "winsqlite3.dll"
                : OperatingSystem.IsMacOS() ? "/usr/lib/libsqlite3.dylib" : "libsqlite3.so.0");
            T Function<T>(string name) where T : Delegate => Marshal.GetDelegateForFunctionPointer<T>(NativeLibrary.GetExport(library, name));
            Open = Function<OpenDelegate>("sqlite3_open_v2"); Close = Function<PointerDelegate>("sqlite3_close");
            Step = Function<PointerDelegate>("sqlite3_step"); Finalize = Function<PointerDelegate>("sqlite3_finalize");
            Prepare = Function<PrepareDelegate>("sqlite3_prepare_v2"); Text = Function<ColumnPointer>("sqlite3_column_text");
            Blob = Function<ColumnPointer>("sqlite3_column_blob"); Bytes = Function<ColumnInteger>("sqlite3_column_bytes");
            Type = Function<ColumnInteger>("sqlite3_column_type"); Double = Function<ColumnDouble>("sqlite3_column_double");
        }
    }
}
