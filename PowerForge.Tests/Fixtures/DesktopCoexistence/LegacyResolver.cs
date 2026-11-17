using System;
using System.IO;
using System.Reflection;
using System.Threading;

namespace DesktopCoexistence
{
    public static class LegacyResolver
    {
        private static Assembly dependency;
        private static int earlierProbes;
        private static int laterProbes;
        private static int probeSequence;

        public static Assembly Dependency { get { return dependency; } }
        public static int EarlierProbes { get { return earlierProbes; } }
        public static int LaterProbes { get { return laterProbes; } }
        public static ResolveEventHandler Handler { get { return Earlier; } }

        public static void Register(string path)
        {
            dependency = Assembly.LoadFrom(path);
            AppDomain.CurrentDomain.AssemblyResolve += Earlier;
        }

        public static void RegisterLater()
        {
            AppDomain.CurrentDomain.AssemblyResolve += Later;
        }

        public static void Unregister()
        {
            AppDomain.CurrentDomain.AssemblyResolve -= Earlier;
            AppDomain.CurrentDomain.AssemblyResolve -= Later;
        }

        public static void ProbeOtherHandlers()
        {
            string name = "DesktopCoexistence.MissingProbe" + Interlocked.Increment(ref probeSequence);
            try
            {
                Assembly.Load(name + ", Version=1.0.0.0, Culture=neutral, PublicKeyToken=null");
                throw new InvalidOperationException("An unrelated missing assembly was unexpectedly resolved.");
            }
            catch (FileNotFoundException) { }
        }

        private static Assembly Earlier(object sender, ResolveEventArgs args)
        {
            string name = new AssemblyName(args.Name).Name;
            if (name.StartsWith("DesktopCoexistence.MissingProbe", StringComparison.Ordinal))
                Interlocked.Increment(ref earlierProbes);
            if (name != "DesktopCoexistence.Dependency") return null;
            // A real earlier handler that ignores requested version, culture, token and requester.
            return dependency;
        }

        private static Assembly Later(object sender, ResolveEventArgs args)
        {
            if (new AssemblyName(args.Name).Name.StartsWith("DesktopCoexistence.MissingProbe", StringComparison.Ordinal))
                Interlocked.Increment(ref laterProbes);
            return null;
        }
    }
}
