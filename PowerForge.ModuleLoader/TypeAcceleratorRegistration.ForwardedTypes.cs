using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using PowerForge.ModuleLoader.V3;
#if NETCOREAPP
using System.Runtime.Loader;
#endif

namespace PowerForge.ModuleLoader.V1;

public sealed partial class TypeAcceleratorRegistration
{
#if NETCOREAPP
    internal static ForwardedTypeAccelerators ForwardedAssembliesFromLoadContext(
        Assembly moduleAssembly, string libraryDirectory, string mode, string[] assemblyNames)
    {
        var context = AssemblyLoadContext.GetLoadContext(moduleAssembly);
        if (context is null) return new ForwardedTypeAccelerators(assemblyNames, Array.Empty<string>());
        var source = new LoadContextTypeSource(context, Path.GetFullPath(libraryDirectory),
            new TypeAcceleratorRegistration(AcceleratorTable.Unavailable));
        return IncludeForwardedAssemblies(mode, assemblyNames, source);
    }
#endif

    internal static ForwardedTypeAccelerators ForwardedAssembliesFromDirectory(
        string libraryDirectory, string mode, string[] assemblyNames, string[] ignoredLibraryFileNames)
    {
        if (!Directory.Exists(libraryDirectory)) return new ForwardedTypeAccelerators(assemblyNames, Array.Empty<string>());
        var source = new DirectoryTypeSource(Path.GetFullPath(libraryDirectory), ignoredLibraryFileNames,
            new TypeAcceleratorRegistration(AcceleratorTable.Unavailable));
        return IncludeForwardedAssemblies(mode, assemblyNames, source);
    }

    partial void IncludeForwarders(Assembly assembly, ref IEnumerable<Type> visibleTypes)
        => visibleTypes = visibleTypes.Concat(TypeAcceleratorForwardedTypes.Read(assembly, message => Warn(message)));

    private static ForwardedTypeAccelerators IncludeForwardedAssemblies(string mode, string[] assemblyNames, ITypeSource source)
    {
        var enumsOnly = string.Equals(mode, "Enums", StringComparison.OrdinalIgnoreCase);
        var names = new List<string>();
        var warnings = new List<string>();
        foreach (var name in Normalize(assemblyNames))
        {
            names.Add(name);
            var facade = source.ImportAssembly(name);
            if (facade is null) continue;

            foreach (var forwarded in TypeAcceleratorForwardedTypes.Read(facade, warnings.Add)
                         .Where(type => !enumsOnly || type.IsEnum)
                         .GroupBy(type => type.Assembly))
            {
                var target = forwarded.Key;
                var targetName = target.GetName().Name!;
                try
                {
                    // The old registrar accepts whole assemblies or unconstrained type names. Use an
                    // assembly only when it selects exactly the types the facade would have registered.
                    var exported = target.GetExportedTypes().Where(type => !enumsOnly || type.IsEnum);
                    if (new HashSet<Type>(forwarded).SetEquals(exported) &&
                        ReferenceEquals(source.ImportAssembly(targetName), target))
                    {
                        names.Add(targetName);
                        continue;
                    }
                    warnings.Add($"An earlier type accelerator registrar cannot expose exactly the types '{name}' forwards to '{targetName}'. Import this module first in a new PowerShell session.");
                }
                catch (Exception ex)
                {
                    warnings.Add($"Could not verify the forwarded type surface of '{targetName}' for '{name}': {ex.Message}");
                }
            }
        }

        return new ForwardedTypeAccelerators(names.ToArray(), warnings.ToArray());
    }
}
