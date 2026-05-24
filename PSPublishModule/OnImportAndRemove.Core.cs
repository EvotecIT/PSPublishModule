#if NET8_0_OR_GREATER
using System;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

/// <summary>
/// CoreCLR assembly resolution for PSPublishModule dependencies.
/// </summary>
public partial class OnModuleImportAndRemove {
    private static readonly object CoreResolverLock = new();
    private static bool _coreResolverRegistered;
    private static AssemblyDependencyResolver? _coreResolver;
    private static string? _moduleDirectory;

    partial void RegisterCoreResolver() {
        lock (CoreResolverLock) {
            if (_coreResolverRegistered) {
                return;
            }

            var assemblyPath = typeof(OnModuleImportAndRemove).Assembly.Location;
            if (string.IsNullOrWhiteSpace(assemblyPath) || !File.Exists(assemblyPath)) {
                return;
            }

            _moduleDirectory = Path.GetDirectoryName(assemblyPath);
            if (string.IsNullOrWhiteSpace(_moduleDirectory)) {
                return;
            }

            _coreResolver = new AssemblyDependencyResolver(assemblyPath);
            AssemblyLoadContext.Default.Resolving += ResolveCoreAssembly;
            _coreResolverRegistered = true;
        }
    }

    partial void UnregisterCoreResolver() {
        lock (CoreResolverLock) {
            if (!_coreResolverRegistered) {
                return;
            }

            AssemblyLoadContext.Default.Resolving -= ResolveCoreAssembly;
            _coreResolver = null;
            _moduleDirectory = null;
            _coreResolverRegistered = false;
        }
    }

    private static Assembly? ResolveCoreAssembly(AssemblyLoadContext context, AssemblyName assemblyName) {
        if (context is null || ShouldSkipCoreResolution(assemblyName)) {
            return null;
        }

        var resolvedPath = _coreResolver?.ResolveAssemblyToPath(assemblyName);
        if (!string.IsNullOrWhiteSpace(resolvedPath) && File.Exists(resolvedPath)) {
            return context.LoadFromAssemblyPath(resolvedPath);
        }

        if (!string.IsNullOrWhiteSpace(_moduleDirectory) && !string.IsNullOrWhiteSpace(assemblyName.Name)) {
            var candidate = Path.Combine(_moduleDirectory, assemblyName.Name + ".dll");
            if (File.Exists(candidate)) {
                return context.LoadFromAssemblyPath(candidate);
            }
        }

        return null;
    }

    private static bool ShouldSkipCoreResolution(AssemblyName assemblyName) {
        var name = assemblyName.Name;
        if (string.IsNullOrWhiteSpace(name)) {
            return true;
        }

        if (name.Equals("System.Management.Automation", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("PowerShellStandard.Library", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("System.Private.CoreLib", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("netstandard", StringComparison.OrdinalIgnoreCase) ||
            name.Equals("mscorlib", StringComparison.OrdinalIgnoreCase)) {
            return true;
        }

        return false;
    }

    internal static bool ShouldSkipCoreResolutionForTesting(string simpleName)
        => ShouldSkipCoreResolution(new AssemblyName(simpleName));
}
#endif
