namespace PowerForge;

/// <summary>
/// Writes the precompiled PowerForge module loader (embedded in this assembly) next to a generated module.
/// Layout: <c>&lt;root&gt;/Core/PowerForge.ModuleLoader.V1.dll</c> (PowerShell 7) and
/// <c>&lt;root&gt;/Desktop/PowerForge.ModuleLoader.V1.dll</c> (Windows PowerShell 5.1).
/// </summary>
internal static class ModuleLoaderAssets
{
    /// <summary>Folder under a packaged module root that holds the loader builds.</summary>
    internal const string PackagedFolderName = "Loader";

    /// <summary>Folder under a development binary root (for example <c>Project/bin</c>) that holds the loader builds.</summary>
    internal const string DevelopmentFolderName = "PowerForge.ModuleLoader";

    internal const string FileName = "PowerForge.ModuleLoader.V1.dll";

    /// <summary>A public loader type the generated script probes before loading the assembly.</summary>
    internal const string ProbeTypeName = "PowerForge.ModuleLoader.V1.SharedAssemblyPolicy";

    private static readonly (string Edition, string Resource)[] Builds =
    {
        ("Core", "PowerForge.ModuleLoader.Core.dll"),
        ("Desktop", "PowerForge.ModuleLoader.Desktop.dll")
    };

    internal static void Write(string loaderRoot)
    {
        foreach (var (edition, resource) in Builds)
        {
            var bytes = ReadResource(resource);
            var target = Path.Combine(loaderRoot, edition, FileName);
            if (File.Exists(target) && File.ReadAllBytes(target).SequenceEqual(bytes))
                continue;

            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.WriteAllBytes(target, bytes);
        }
    }

    private static byte[] ReadResource(string name)
    {
        using var stream = typeof(ModuleLoaderAssets).Assembly.GetManifestResourceStream(name)
            ?? throw new InvalidOperationException($"PowerForge module loader resource '{name}' is missing from {typeof(ModuleLoaderAssets).Assembly.GetName().Name}.");
        using var buffer = new MemoryStream();
        stream.CopyTo(buffer);
        return buffer.ToArray();
    }
}
