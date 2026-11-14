using System.Reflection;

#if LEGACY
[assembly: AssemblyVersion("1.0.0.0")]
#else
[assembly: AssemblyVersion("2.0.0.0")]
#endif

namespace DesktopCoexistence.Dependency
{
#if LEGACY
    public class LegacyOnly { }
#else
    public class CurrentOnly
    {
        public string Value { get { return "current-dependency"; } }
    }
#endif
}
