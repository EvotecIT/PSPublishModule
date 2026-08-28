using System.Management.Automation.Language;
using System.Text;

namespace PowerForge;

public sealed partial class PowerShellCompilationArtifactBuilder
{
    private static string GeneratePackagedScript(
        string sourcePath,
        PackagedSourceSet packagedSources,
        PowerShellTypedCompilationResult? hybrid = null)
    {
        var rewritten = PowerShellPackagedScriptRewriter.Rewrite(
            sourcePath,
            packagedCommandPathExpression: packagedSources.UsesExtractedRoot
                ? "[PowerForge.Compiled.PowerForgePackagedEntryPoint]::Path"
                : null,
            embeddedScriptPaths: packagedSources.EmbeddedScriptPaths,
            dependencyCommandPathExpression: packagedSources.HasDependencies
                ? "[PowerForge.Compiled.PowerForgePackagedEntryPoint]::Path"
                : null,
            embeddedResourceRelativePaths: packagedSources.EmbeddedResourceRelativePaths,
            packagedScriptRootExpression: packagedSources.UsesExtractedRoot
                ? "$([System.IO.Path]::GetDirectoryName([PowerForge.Compiled.PowerForgePackagedEntryPoint]::Path))"
                : null);
        return hybrid is null ? rewritten : PowerShellHybridModuleComposer.ComposeExecutableRoot(rewritten, sourcePath, hybrid);
    }

    private static PackagedSourceSet PreparePackagedSources(
        string workspace,
        string sourcePath,
        IEnumerable<string> compilationSourcePaths,
        IEnumerable<PowerShellCompilationDependency> dependencyPlan,
        PowerShellTypedCompilationResult? hybrid = null)
    {
        var fullSourcePath = Path.GetFullPath(sourcePath);
        var sourceRoot = Path.GetDirectoryName(fullSourcePath) ?? Directory.GetCurrentDirectory();
        var scriptDependencies = compilationSourcePaths
            .Select(Path.GetFullPath)
            .Where(path => !PowerShellCompilationPathSafety.PathEquals(path, fullSourcePath))
            .Distinct(PowerShellCompilationPathSafety.PathComparer)
            .OrderBy(path => FrameworkCompatibility.GetRelativePath(sourceRoot, path), StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var resourceDependencies = dependencyPlan
            .Where(static dependency => dependency.Exists &&
                dependency.SourcePath is not null &&
                dependency.Disposition == PowerShellCompilationDependencyDisposition.EmbeddedAndExtracted &&
                (dependency.Selection is PowerShellCompilationDependencySelection.Required or
                    PowerShellCompilationDependencySelection.ExplicitInclude or
                    PowerShellCompilationDependencySelection.Inferred or
                    PowerShellCompilationDependencySelection.PolicyInclude))
            .Select(static dependency => new
            {
                Path = Path.GetFullPath(dependency.SourcePath!),
                RelativePath = dependency.RelativePath,
                dependency.Selection
            })
            .Where(dependency => !PowerShellCompilationPathSafety.PathEquals(dependency.Path, fullSourcePath) &&
                                 !scriptDependencies.Contains(dependency.Path, PowerShellCompilationPathSafety.PathComparer))
            .GroupBy(static dependency => dependency.Path, PowerShellCompilationPathSafety.PathComparer)
            .Select(static group => group.First())
            .OrderBy(static dependency => dependency.RelativePath, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (scriptDependencies.Length == 0 && resourceDependencies.Length == 0)
            return new PackagedSourceSet(
                Path.GetFileName(fullSourcePath),
                string.Empty,
                string.Empty,
                hasDependencies: false,
                Array.Empty<string>(),
                Array.Empty<string>(),
                usesExtractedRoot: false);

        var dependencyDirectory = Path.Combine(workspace, "EmbeddedDependencies");
        Directory.CreateDirectory(dependencyDirectory);
        var hybridCompiledMethods = hybrid is null
            ? null
            : PowerShellHybridModuleComposer.GetExecutableCompiledMethodKeys(hybrid);
        var projectResources = new List<string>();
        var dependencySpecs = new List<string>();
        for (var index = 0; index < scriptDependencies.Length; index++)
        {
            var dependency = scriptDependencies[index];
            PowerShellCompilationPathSafety.EnsureContained(sourceRoot, dependency, $"Packaged dependency '{dependency}' escapes the executable entrypoint root.");
            ValidatePackagedDependency(dependency, scriptDependencies.Prepend(fullSourcePath).ToArray());
            var fileName = $"Dependency{index:D4}.ps1";
            var target = Path.Combine(dependencyDirectory, fileName);
            var composed = hybrid is null ? null : PowerShellHybridModuleComposer.ComposeDependency(dependency, hybrid, hybridCompiledMethods!);
            if (composed is null)
                File.Copy(dependency, target, overwrite: false);
            else
                File.WriteAllText(target, composed, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            var logicalName = $"PowerForge.Compiled.{Path.GetFileNameWithoutExtension(fileName)}.ps1";
            var relativePath = FrameworkCompatibility.GetRelativePath(sourceRoot, dependency).Replace('\\', '/');
            projectResources.Add($"    <EmbeddedResource Include=\"EmbeddedDependencies/{fileName}\" LogicalName=\"{EscapeXml(logicalName)}\" />");
            dependencySpecs.Add($"        new EmbeddedDependency({PowerShellCSharpLiteral.QuoteString(logicalName)}, {PowerShellCSharpLiteral.QuoteString(relativePath)}, {PowerShellCSharpLiteral.QuoteString(ComputeSha256(target))}, {GetExecutableUnixMode(dependency)}),");
        }
        for (var index = 0; index < resourceDependencies.Length; index++)
        {
            var dependency = resourceDependencies[index];
            PowerShellCompilationPathSafety.EnsureContained(sourceRoot, dependency.Path, $"Packaged resource '{dependency.RelativePath}' escapes the executable entrypoint root.");
            var fileName = $"Resource{index:D4}.bin";
            File.Copy(dependency.Path, Path.Combine(dependencyDirectory, fileName), overwrite: false);
            var logicalName = $"PowerForge.Compiled.Resource{index:D4}";
            projectResources.Add($"    <EmbeddedResource Include=\"EmbeddedDependencies/{fileName}\" LogicalName=\"{EscapeXml(logicalName)}\" />");
            dependencySpecs.Add($"        new EmbeddedDependency({PowerShellCSharpLiteral.QuoteString(logicalName)}, {PowerShellCSharpLiteral.QuoteString(dependency.RelativePath)}, {PowerShellCSharpLiteral.QuoteString(ComputeSha256(dependency.Path))}, {GetExecutableUnixMode(dependency.Path)}),");
        }
        var resourcePaths = scriptDependencies
            .Select(dependency => FrameworkCompatibility.GetRelativePath(sourceRoot, dependency).Replace('\\', '/'))
            .Concat(resourceDependencies.Select(static dependency => dependency.RelativePath))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var usesExtractedRoot = scriptDependencies.Length > 0 ||
                                resourceDependencies.Any(static dependency =>
                                    dependency.Selection is PowerShellCompilationDependencySelection.ExplicitInclude or
                                        PowerShellCompilationDependencySelection.PolicyInclude);
        return new PackagedSourceSet(
            Path.GetFileName(fullSourcePath),
            string.Join(Environment.NewLine, projectResources),
            string.Join(Environment.NewLine, dependencySpecs),
            hasDependencies: true,
            scriptDependencies,
            resourcePaths,
            usesExtractedRoot);
    }

    private static int GetExecutableUnixMode(string path)
    {
#if NET8_0_OR_GREATER
        if (OperatingSystem.IsWindows())
            return 0;
        var mode = File.GetUnixFileMode(path);
        var executable = mode & (UnixFileMode.UserExecute | UnixFileMode.GroupExecute | UnixFileMode.OtherExecute);
        return executable == 0 ? 0 : (int)mode;
#else
        return 0;
#endif
    }

    private static void ValidatePackagedDependency(string dependencyPath, IReadOnlyCollection<string> embeddedScriptPaths)
        => PowerShellPackagedScriptRewriter.ValidateDependency(dependencyPath, embeddedScriptPaths);
}
