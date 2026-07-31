using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using Mono.Addins;

[assembly: Addin("OpenSimWeather", "0.3.4")]
[assembly: AddinDependency("OpenSim.Region.Framework", OpenSim.VersionInfo.VersionNumber)]

namespace OpenSimWeather
{
    class Info
    {
        public const string VersionNumber = "0.3.4";

        public static string AssemblyDirectory
        {
            get
            {
                string location = Assembly.GetExecutingAssembly().Location;
                return Path.GetDirectoryName(location);
            }
        }

    }
}
