namespace PowerForge.Web;

public static partial class WebApiDocsGenerator
{
    /// <summary>Uses site copy, package metadata, or a product-specific reference introduction.</summary>
    private static string ResolveApiIndexLead(WebApiDocsOptions options)
    {
        if (!string.IsNullOrWhiteSpace(options.IndexLead))
            return options.IndexLead.Trim();

        if (options.Type == ApiDocsType.PowerShell)
        {
            var help = ResolvePowerShellHelpFile(options.HelpPath ?? string.Empty, new List<string>());
            var name = Path.GetFileNameWithoutExtension(help ?? string.Empty);
            if (name.EndsWith("-help", StringComparison.OrdinalIgnoreCase)) name = name[..^5];
            if (name.EndsWith(".dll", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
            var manifest = ResolvePowerShellModuleManifestPath(help ?? string.Empty, name, options.PowerShellModuleManifestPath);
            name = ResolvePowerShellModuleDisplayName(name, manifest, options);
            return TryReadModuleManifestDescription(manifest) ??
                (string.IsNullOrWhiteSpace(name)
                    ? "PowerShell command reference with syntax, parameters, and examples."
                    : $"PowerShell command reference for {name}, with syntax, parameters, and examples.");
        }

        var assemblies = ResolveCSharpAssemblyPaths(options);
        var description = assemblies.Select(TryReadAssemblyDescription).FirstOrDefault(static value => !string.IsNullOrWhiteSpace(value));
        if (description is not null)
            return description;

        var product = options.PackageId?.Trim();
        if (string.IsNullOrWhiteSpace(product) && !string.IsNullOrWhiteSpace(options.Title) &&
            !options.Title.Equals("API Reference", StringComparison.OrdinalIgnoreCase))
        {
            product = System.Text.RegularExpressions.Regex.Replace(options.Title.Trim(),
                @"\s+API(?:\s+reference|\s+documentation)?$", string.Empty,
                System.Text.RegularExpressions.RegexOptions.IgnoreCase | System.Text.RegularExpressions.RegexOptions.CultureInvariant,
                RegexTimeout);
        }
        if (string.IsNullOrWhiteSpace(product) && assemblies.Length > 0)
            product = Path.GetFileNameWithoutExtension(assemblies[0]);
        return string.IsNullOrWhiteSpace(product)
            ? "API reference with types, members, and signatures."
            : $"API reference for {product}, with types, members, and signatures.";
    }
}
