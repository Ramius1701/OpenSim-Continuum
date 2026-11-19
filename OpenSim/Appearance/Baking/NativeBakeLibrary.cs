using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using SkiaSharp;

namespace OpenSimNGC.Appearance.Baking;

// OpenSim plugins share bin without an application deps.json entry for each native asset.
// Resolve the package's matching native binary explicitly; keep the package's RID layout.
internal static class NativeBakeLibrary
{
    private static readonly object Gate = new();
    private static IntPtr native;

    [ModuleInitializer]
    internal static void Initialise()
    {
        try { NativeLibrary.SetDllImportResolver(typeof(SKBitmap).Assembly, Resolve); }
        catch (InvalidOperationException) { } // An existing Skia user owns its resolver.
    }

    private static IntPtr Resolve(string name, Assembly assembly, DllImportSearchPath? searchPath)
    {
        if (name != "libSkiaSharp") return IntPtr.Zero;
        lock (Gate)
        {
            if (native != IntPtr.Zero) return native;
            string os = OperatingSystem.IsWindows() ? "win" : OperatingSystem.IsMacOS() ? "osx" : "linux";
            string arch = RuntimeInformation.ProcessArchitecture.ToString().ToLowerInvariant();
            string file = os == "win" ? "libSkiaSharp.dll" : os == "osx" ? "libSkiaSharp.dylib" : "libSkiaSharp.so";
            NativeLibrary.TryLoad(Path.Combine(AppContext.BaseDirectory, "runtimes", os + "-" + arch, "native", file), out native);
            return native;
        }
    }
}
