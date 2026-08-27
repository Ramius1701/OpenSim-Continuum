using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Data.SQLite;

namespace OpenSim.Continuum.Economy
{
    internal static class SQLiteNativeLibrary
    {
        private static int s_configured;

        internal static void Configure()
        {
            if (Interlocked.Exchange(ref s_configured, 1) != 0)
                return;
            try
            {
                NativeLibrary.SetDllImportResolver(typeof(SQLiteConnection).Assembly, Resolve);
            }
            catch (InvalidOperationException)
            {
                // Another OpenSim component already installed the resolver in
                // this process. Reuse it rather than replacing global state.
            }
        }

        private static IntPtr Resolve(string name, System.Reflection.Assembly assembly, DllImportSearchPath? path)
        {
            if (!name.Equals("e_sqlite3", StringComparison.OrdinalIgnoreCase))
                return IntPtr.Zero;
            string file = OperatingSystem.IsWindows() ? "e_sqlite3.dll" :
                OperatingSystem.IsMacOS() ? (RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ?
                    "libe_sqlite3_OSX_arm64.dylib" : "libe_sqlite3_OSX_x64.dylib") :
                (RuntimeInformation.ProcessArchitecture == Architecture.Arm64 ? "libe_sqlite3-arm64.so" : "libe_sqlite3.so");
            string candidate = Path.Combine(AppContext.BaseDirectory, "lib64", file);
            return File.Exists(candidate) ? NativeLibrary.Load(candidate) : IntPtr.Zero;
        }
    }
}
