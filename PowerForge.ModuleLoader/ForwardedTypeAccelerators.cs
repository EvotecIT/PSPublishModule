using System;
using System.Reflection;
using PowerForge.ModuleLoader.V1;

namespace PowerForge.ModuleLoader.V3;

/// <summary>
/// Selects forwarding targets with an exact assembly surface for earlier type accelerator registrars.
/// </summary>
public sealed class ForwardedTypeAccelerators
{
    internal ForwardedTypeAccelerators(string[] assemblyNames, string[] warnings)
    {
        AssemblyNames = assemblyNames;
        Warnings = warnings;
    }

    /// <summary>Assemblies the registrar can enumerate without widening the configured surface.</summary>
    public string[] AssemblyNames { get; }

    /// <summary>Forwarding problems to report during import.</summary>
    public string[] Warnings { get; }

#if NETCOREAPP
    /// <summary>Resolves forwarded assemblies from the module assembly's load context.</summary>
    public static ForwardedTypeAccelerators FromLoadContext(
        Type registrar, Assembly moduleAssembly, string libraryDirectory, string mode, string[] assemblyNames)
        => DiscoversForwardedTypes(registrar)
            ? new ForwardedTypeAccelerators(assemblyNames, Array.Empty<string>())
            : TypeAcceleratorRegistration.ForwardedAssembliesFromLoadContext(moduleAssembly, libraryDirectory, mode, assemblyNames);
#endif

    /// <summary>Resolves forwarded assemblies from the module's Desktop payload.</summary>
    public static ForwardedTypeAccelerators FromDirectory(
        Type registrar, string libraryDirectory, string mode, string[] assemblyNames, string[] ignoredLibraryFileNames)
        => DiscoversForwardedTypes(registrar)
            ? new ForwardedTypeAccelerators(assemblyNames, Array.Empty<string>())
            : TypeAcceleratorRegistration.ForwardedAssembliesFromDirectory(libraryDirectory, mode, assemblyNames, ignoredLibraryFileNames);

    private static bool DiscoversForwardedTypes(Type registrar)
        => registrar.Assembly.GetType(typeof(TypeAcceleratorForwardedTypes).FullName!, throwOnError: false) is not null;
}
