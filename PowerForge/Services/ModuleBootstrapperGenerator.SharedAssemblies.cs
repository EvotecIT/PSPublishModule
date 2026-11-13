namespace PowerForge;

internal static partial class ModuleBootstrapperGenerator
{
    // PowerForge releases before the precompiled loader built a per-module helper named
    // <Module>.ModuleLoadContext[.SharedV3_<hash>].dll (or .DevelopmentModuleLoadContext) into Lib.
    // Such files can still sit in project outputs or old staging folders; keep them out of preload lists.
    private const string LegacySharedAssemblyPolicyCapability = "SharedV3_";

    internal static bool IsAssemblyLoadContextLoaderFileName(string moduleName, string fileName)
    {
        var moduleAssemblyName = SanitizeLegacyAssemblyName(moduleName);
        foreach (var suffix in new[] { ".ModuleLoadContext", ".DevelopmentModuleLoadContext" })
        {
            var legacy = moduleAssemblyName + suffix;
            if (string.Equals(legacy + ".dll", fileName, StringComparison.OrdinalIgnoreCase)) return true;
            var prefix = legacy + "." + LegacySharedAssemblyPolicyCapability;
            if (fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                var hash = fileName.Substring(prefix.Length, fileName.Length - prefix.Length - 4);
                if (hash.Length == 16 && hash.All(Uri.IsHexDigit)) return true;
            }
        }

        return string.Equals(fileName, ModuleLoaderAssets.FileName, StringComparison.OrdinalIgnoreCase);
    }

    private static string SanitizeLegacyAssemblyName(string value)
    {
        var chars = (value ?? string.Empty).Select(ch => char.IsLetterOrDigit(ch) || ch is '.' or '_' or '-' ? ch : '_').ToArray();
        var sanitized = new string(chars).Trim('.', '-', '_');
        return string.IsNullOrWhiteSpace(sanitized) ? "Module" : sanitized;
    }

    private static string BuildSharedAssembliesBlock(
        IReadOnlyList<string>? sharedAssemblies,
        IReadOnlyList<string>? preloadSharedAssemblies,
        string directoriesExpression)
    {
        if (sharedAssemblies is not { Count: > 0 }) return string.Empty;
        return RenderModuleBootstrapperTemplate(
            "SharedAssemblies",
            "Scripts/ModuleBootstrapper/SharedAssemblies.Template.ps1",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["SharedAssemblies"] = BuildPowerShellArrayLiteral(sharedAssemblies),
                ["SharedAssemblyDirectories"] = directoriesExpression,
                ["PreloadSharedAssemblies"] = BuildPowerShellArrayLiteral(preloadSharedAssemblies ?? Array.Empty<string>())
            }) + Environment.NewLine;
    }
}
