namespace PowerForge;

internal static partial class ModuleBootstrapperGenerator
{
    // Empty policies keep the historical identity. Nonempty policies must not
    // bind to an older helper whose Load implementation has no shared routing.
    private const string SharedAssemblyPolicyCapability = "SharedV2_";

    private static string GetSharedAssemblyPolicyIdentitySuffix(IReadOnlyList<string>? names)
    {
        if (names is not { Count: > 0 }) return string.Empty;
        var normalized = names.Select(name => name.ToUpperInvariant()).Distinct(StringComparer.Ordinal)
            .OrderBy(name => name, StringComparer.Ordinal);
        using var algorithm = System.Security.Cryptography.SHA256.Create();
        var hash = algorithm.ComputeHash(System.Text.Encoding.UTF8.GetBytes(string.Join("\n", normalized)));
        return "." + SharedAssemblyPolicyCapability + BitConverter.ToString(hash, 0, 8).Replace("-", string.Empty);
    }

    internal static bool IsAssemblyLoadContextLoaderFileName(string moduleName, string fileName)
    {
        foreach (var legacy in GetAssemblyLoadContextLoaderFileNames(moduleName))
        {
            if (string.Equals(legacy, fileName, StringComparison.OrdinalIgnoreCase)) return true;
            var prefix = Path.GetFileNameWithoutExtension(legacy) + "." + SharedAssemblyPolicyCapability;
            if (fileName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase) && fileName.EndsWith(".dll", StringComparison.OrdinalIgnoreCase))
            {
                var hash = fileName.Substring(prefix.Length, fileName.Length - prefix.Length - 4);
                if (hash.Length == 16 && hash.All(Uri.IsHexDigit)) return true;
            }
        }
        return false;
    }

    private static string SharedDesktopResolverMembers => EmbeddedScripts.Load("Scripts/ModuleBootstrapper/SharedDesktopResolver.Members.cs");
    private static string SharedDesktopResolverConstructor => EmbeddedScripts.Load("Scripts/ModuleBootstrapper/SharedDesktopResolver.Constructor.cs");

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
                ["SharedAssemblyPolicySource"] = EmbeddedScripts.Load("Scripts/ModuleBootstrapper/SharedAssemblyPolicy.cs").TrimEnd(),
                ["SharedAssemblies"] = BuildPowerShellArrayLiteral(sharedAssemblies),
                ["SharedAssemblyDirectories"] = directoriesExpression,
                ["PreloadSharedAssemblies"] = BuildPowerShellArrayLiteral(preloadSharedAssemblies ?? Array.Empty<string>())
            }) + Environment.NewLine;
    }

    private static string BuildSharedAssemblyContextMembers(IReadOnlyList<string>? sharedAssemblies)
    {
        if (sharedAssemblies is not { Count: > 0 }) return string.Empty;
        // Names have already passed the exact-name validator, including quote/path rejection.
        var names = string.Join(", ", sharedAssemblies.Select(name => "\"" + name + "\""));
        return "private static readonly HashSet<string> SharedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { " + names + " };\n" +
            "private static readonly Func<string, string[], Assembly> ResolveShared = " +
            "(Func<string, string[], Assembly>)Delegate.CreateDelegate(typeof(Func<string, string[], Assembly>), " +
            "AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(\"PowerForge.Generated.SharedAssemblyPolicyV2\", false)).First(t => t != null).GetMethod(\"Resolve\"));\n";
    }

    private static string BuildSharedAssemblyResolve(IReadOnlyList<string>? sharedAssemblies, string directoriesExpression)
        => sharedAssemblies is { Count: > 0 }
            ? "if (SharedNames.Contains(assemblyName.Name)) return ResolveShared(assemblyName.Name, " + directoriesExpression + ");\n"
            : string.Empty;
}
