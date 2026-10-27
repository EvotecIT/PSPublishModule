using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;

namespace PowerForge.Generated {
    /// <summary>Process-wide resolution policy compiled once by the PowerShell host.</summary>
    public static class SharedAssemblyPolicyV2 {
        private static readonly object Sync = new object();
        private static readonly Type ContextType = Type.GetType("System.Runtime.Loader.AssemblyLoadContext, System.Private.CoreLib");
        private static readonly Dictionary<string, List<string>> Payloads = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        private static readonly HashSet<Assembly> SharedInstances = new HashSet<Assembly>();
        private static object sharedContext;
        private static Delegate contextHandler;
        private static bool desktopHandlerRegistered;
        [ThreadStatic] private static HashSet<string> resolving;

        /// <summary>Records exact declared names and selected payload paths without loading optional assemblies.</summary>
        public static void Register(string[] names, string[] directories) {
            lock (Sync) {
                foreach (string name in names) {
                    List<string> paths;
                    if (!Payloads.TryGetValue(name, out paths)) {
                        paths = new List<string>();
                        Payloads.Add(name, paths);
                    }
                    foreach (string directory in directories) {
                        string path = Path.GetFullPath(Path.Combine(directory, name + ".dll"));
                        if (!paths.Contains(path)) paths.Add(path);
                    }
                }
                if (ContextType == null && !desktopHandlerRegistered) {
                    AppDomain.CurrentDomain.AssemblyResolve += ResolveDesktop;
                    desktopHandlerRegistered = true;
                }
            }
        }

        /// <summary>Reuses a loaded shared identity or loads a registered payload into the chosen shared context.</summary>
        public static Assembly Resolve(string name, string[] directories) {
            lock (Sync) {
                List<string> paths;
                if (!Payloads.TryGetValue(name, out paths))
                    throw new InvalidOperationException("Assembly is not declared process-shared: " + name);
                EnsureContext();
                Assembly loaded = FindLoaded(name);
                if (loaded != null) {
                    if (ContextType != null && !Object.ReferenceEquals(GetContext(loaded), sharedContext))
                        throw new FileLoadException("Shared assemblies are already loaded in different contexts. Start a new session.");
                    SharedInstances.Add(loaded);
                    return loaded;
                }
                // Only registration supplies provenance. A caller cannot expand the
                // payload map by passing another private directory to Resolve.
                foreach (string path in paths) {
                    if (!File.Exists(path)) continue;
                    AssemblyName identity = AssemblyName.GetAssemblyName(path);
                    if (!String.Equals(identity.Name, name, StringComparison.OrdinalIgnoreCase) || !String.IsNullOrEmpty(identity.CultureName))
                        throw new FileLoadException("Shared assembly file does not match its declared simple name: " + path);
                    Assembly assembly = ContextType == null ? Assembly.LoadFrom(path) :
                        (Assembly)ContextType.GetMethod("LoadFromAssemblyPath").Invoke(sharedContext, new object[] { path });
                    SharedInstances.Add(assembly);
                    return assembly;
                }
                throw new FileNotFoundException("Shared assembly '" + name + "' was not found in a registered module payload.", name + ".dll");
            }
        }

        private static Assembly FindLoaded(string name) {
            Assembly loaded = null;
            foreach (Assembly assembly in AppDomain.CurrentDomain.GetAssemblies()) {
                if (assembly.IsDynamic || !String.Equals(assembly.GetName().Name, name, StringComparison.OrdinalIgnoreCase)) continue;
                if (loaded != null)
                    throw new FileLoadException("Multiple copies of shared assembly '" + name + "' are already loaded. Start a new session.");
                loaded = assembly;
            }
            return loaded;
        }

        private static object GetContext(Assembly assembly) {
            object context = ContextType.GetMethod("GetLoadContext").Invoke(null, new object[] { assembly });
            if ((bool)ContextType.GetProperty("IsCollectible").GetValue(context, null))
                throw new FileLoadException("A collectible context cannot own a process-shared assembly.");
            return context;
        }

        private static void EnsureContext() {
            if (ContextType == null || sharedContext != null) return;
            object selected = null;
            // A lazy adapter may be requested before the core. Adopt any existing
            // declared identity before choosing Default, without preloading it.
            foreach (string name in Payloads.Keys) {
                Assembly loaded = FindLoaded(name);
                if (loaded == null) continue;
                object context = GetContext(loaded);
                if (selected != null && !Object.ReferenceEquals(selected, context))
                    throw new FileLoadException("Shared assemblies are already loaded in different contexts. Start a new session.");
                selected = context;
                SharedInstances.Add(loaded);
            }
            sharedContext = selected ?? ContextType.GetProperty("Default").GetValue(null, null);
            EventInfo resolvingEvent = ContextType.GetEvent("Resolving");
            MethodInfo method = typeof(SharedAssemblyPolicyV2).GetMethod("ResolveCore", BindingFlags.NonPublic | BindingFlags.Static).MakeGenericMethod(ContextType);
            contextHandler = Delegate.CreateDelegate(resolvingEvent.EventHandlerType, method);
            resolvingEvent.AddEventHandler(sharedContext, contextHandler);
        }

        // Closing this method over the runtime's ALC type avoids adding a Core
        // assembly reference to the same source compiled on Windows PowerShell.
        private static Assembly ResolveCore<TContext>(TContext context, AssemblyName requested) {
            lock (Sync) {
                if (!Object.ReferenceEquals(context, sharedContext)) return null;
                return ResolveDeclared(requested);
            }
        }

        private static Assembly ResolveDesktop(object sender, ResolveEventArgs args) {
            lock (Sync) {
                if (args == null || args.RequestingAssembly == null || !IsSharedRequester(args.RequestingAssembly)) return null;
                return ResolveDeclared(new AssemblyName(args.Name));
            }
        }

        private static bool IsSharedRequester(Assembly assembly) {
            if (SharedInstances.Contains(assembly)) return true;
            if (assembly.IsDynamic || String.IsNullOrEmpty(assembly.Location)) return false;
            List<string> paths;
            if (!Payloads.TryGetValue(assembly.GetName().Name, out paths)) return false;
            string location = Path.GetFullPath(assembly.Location);
            foreach (string path in paths)
                if (String.Equals(path, location, StringComparison.OrdinalIgnoreCase)) return true;
            return false;
        }

        private static Assembly ResolveDeclared(AssemblyName requested) {
            if (requested == null || String.IsNullOrEmpty(requested.Name) || !Payloads.ContainsKey(requested.Name)) return null;
            if (resolving == null) resolving = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (!resolving.Add(requested.Name)) return null;
            try {
                Assembly assembly = Resolve(requested.Name, new string[0]);
                AssemblyName actual = assembly.GetName();
                if (!String.Equals(requested.CultureName ?? "", actual.CultureName ?? "", StringComparison.OrdinalIgnoreCase)) return null;
                byte[] expectedToken = requested.GetPublicKeyToken();
                byte[] actualToken = actual.GetPublicKeyToken() ?? new byte[0];
                if (expectedToken != null) {
                    if (expectedToken.Length != actualToken.Length) return null;
                    for (int i = 0; i < expectedToken.Length; i++) if (expectedToken[i] != actualToken[i]) return null;
                }
                return assembly;
            } finally { resolving.Remove(requested.Name); }
        }
    }
}
