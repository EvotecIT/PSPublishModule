using System;
using System.IO;
using System.Management.Automation;
using System.Reflection;


/// <summary>
/// Namespace for module import and removal handling.
/// </summary>
public partial class OnModuleImportAndRemove : IModuleAssemblyInitializer, IModuleAssemblyCleanup {
    /// <summary>
    /// Handles module import event.
    /// </summary>
    public void OnImport() {
        if (IsNetFramework()) {
            AppDomain.CurrentDomain.AssemblyResolve += MyResolveEventHandler;
        } else {
            RegisterCoreResolver();
        }
    }

    /// <summary>
    /// Handles module removal event.
    /// </summary>
    /// <param name="module"></param>
    public void OnRemove(PSModuleInfo module) {
        if (IsNetFramework()) {
            AppDomain.CurrentDomain.AssemblyResolve -= MyResolveEventHandler;
        } else {
            UnregisterCoreResolver();
        }
    }

    partial void RegisterCoreResolver();

    partial void UnregisterCoreResolver();

    /// <summary>
    /// Custom assembly resolver to load assemblies from the module directory.
    /// </summary>
    /// <param name="sender"></param>
    /// <param name="args"></param>
    /// <returns></returns>
    private static Assembly? MyResolveEventHandler(object? sender, ResolveEventArgs args) {
        //This code is used to resolve the assemblies
        //Console.WriteLine($"Resolving {args.Name}");
        var directoryPath = Path.GetDirectoryName(typeof(OnModuleImportAndRemove).Assembly.Location);
        if (directoryPath != null) {
            var requestedName = new AssemblyName(args.Name).Name;
            if (!string.IsNullOrWhiteSpace(requestedName)) {
                var candidate = Path.Combine(directoryPath, requestedName + ".dll");
                if (File.Exists(candidate)) {
                    //Console.WriteLine($"Loading {args.Name} assembly {Path.GetFileName(candidate)}");
                    return Assembly.LoadFile(candidate);
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Determines if the current runtime is .NET Framework.
    /// </summary>
    /// <returns></returns>
    private bool IsNetFramework() {
        // Get the version of the CLR
        Version clrVersion = System.Environment.Version;
        // Check if the CLR version is 4.x.x.x
        return clrVersion.Major == 4;
    }

    /// <summary>
    /// Determines if the current runtime is .NET Core.
    /// </summary>
    /// <returns></returns>
    private bool IsNetCore() {
        return System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription.StartsWith(".NET Core", StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Determines if the current runtime is .NET 5 or higher.
    /// </summary>
    /// <returns></returns>
    private bool IsNet5OrHigher() {
        return System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription.StartsWith(".NET 5", StringComparison.OrdinalIgnoreCase) ||
               System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription.StartsWith(".NET 6", StringComparison.OrdinalIgnoreCase) ||
               System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription.StartsWith(".NET 7", StringComparison.OrdinalIgnoreCase) ||
               System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription.StartsWith(".NET 8", StringComparison.OrdinalIgnoreCase) ||
               System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription.StartsWith(".NET 9", StringComparison.OrdinalIgnoreCase) ||
               System.Runtime.InteropServices.RuntimeInformation.FrameworkDescription.StartsWith(".NET 10", StringComparison.OrdinalIgnoreCase);
    }
}
