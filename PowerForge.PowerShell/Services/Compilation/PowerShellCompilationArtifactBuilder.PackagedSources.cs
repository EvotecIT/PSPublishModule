using System.Management.Automation.Language;
using System.Text;

namespace PowerForge;

public sealed partial class PowerShellCompilationArtifactBuilder
{
    private static string GeneratePackagedScript(
        string sourcePath,
        PackagedSourceSet packagedSources,
        PowerShellTypedCompilationResult? hybrid = null,
        PowerShellPackagedSourceMapping? sourceMapping = null)
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
                : null,
            guardDotSources: true,
            sourceMapping: sourceMapping);
        return hybrid is null ? rewritten : PowerShellHybridModuleComposer.ComposeExecutableRoot(rewritten, sourcePath, hybrid, sourceMapping);
    }

    private static PackagedSourceSet PreparePackagedSources(
        string workspace,
        string sourcePath,
        IEnumerable<string> compilationSourcePaths,
        IEnumerable<PowerShellCompilationDependency> dependencyPlan,
        PowerShellTypedCompilationResult? hybrid = null,
        bool nativeScriptEntry = false)
    {
        WritePackagedPathGuardSource(workspace);
        WritePackagedConsoleHostSources(workspace);
        var fullSourcePath = Path.GetFullPath(sourcePath);
        var sourceRoot = Path.GetDirectoryName(fullSourcePath) ?? Directory.GetCurrentDirectory();
        var scriptDependencies = compilationSourcePaths
            .Select(Path.GetFullPath)
            .Where(path => !PowerShellCompilationPathSafety.PathEquals(path, fullSourcePath))
            .Distinct(PowerShellCompilationPathSafety.PathComparer)
            .OrderBy(path => FrameworkCompatibility.GetRelativePath(sourceRoot, path), StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var resourceDependencies = dependencyPlan
            .Where(dependency => dependency.Exists &&
                dependency.SourcePath is not null &&
                (dependency.Disposition == PowerShellCompilationDependencyDisposition.EmbeddedAndExtracted ||
                 nativeScriptEntry && dependency.Disposition == PowerShellCompilationDependencyDisposition.CopiedAdjacent) &&
                (dependency.Selection is PowerShellCompilationDependencySelection.Required or
                    PowerShellCompilationDependencySelection.ExplicitInclude or
                    PowerShellCompilationDependencySelection.Inferred or
                    PowerShellCompilationDependencySelection.PolicyInclude))
            .Select(static dependency => new
            {
                Path = Path.GetFullPath(dependency.SourcePath!),
                RelativePath = dependency.RelativePath.Replace('\\', '/'),
                dependency.Selection,
                Metadata = dependency
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
            var composed = hybrid is null ? null : PowerShellHybridModuleComposer.ComposeDependency(dependency, hybrid, hybridCompiledMethods!, executable: true);
            var retained = composed ?? Parser.ParseFile(dependency, out _, out _).Extent.Text;
            var guarded = PowerShellPackagedScriptRewriter.RewriteDependencyDotSources(
                retained, dependency, sourceRoot, scriptDependencies.Prepend(fullSourcePath).ToArray());
            if (composed is null && guarded.Equals(retained, StringComparison.Ordinal))
                File.Copy(dependency, target, overwrite: false);
            else
                File.WriteAllText(target, guarded, new UTF8Encoding(encoderShouldEmitUTF8Identifier: true));
            var logicalName = $"PowerForge.Compiled.{Path.GetFileNameWithoutExtension(fileName)}.ps1";
            var relativePath = FrameworkCompatibility.GetRelativePath(sourceRoot, dependency).Replace('\\', '/');
            projectResources.Add($"    <EmbeddedResource Include=\"EmbeddedDependencies/{fileName}\" LogicalName=\"{EscapeXml(logicalName)}\" />");
            dependencySpecs.Add($"        new EmbeddedDependency({PowerShellCSharpLiteral.QuoteString(logicalName)}, {PowerShellCSharpLiteral.QuoteString(relativePath)}, {PowerShellCSharpLiteral.QuoteString(ComputeSha256(target))}, {GetExecutableUnixMode(dependency)}),");
        }
        for (var index = 0; index < resourceDependencies.Length; index++)
        {
            var dependency = resourceDependencies[index];
            PowerShellCompilationPathSafety.EnsureContained(sourceRoot, dependency.Path, $"Packaged resource '{dependency.RelativePath}' escapes the executable entrypoint root.");
            if (nativeScriptEntry)
                PowerShellCompilationPathSafety.EnsureNoLinks(sourceRoot, dependency.Path,
                    $"Native source-layout resource '{dependency.RelativePath}' cannot traverse a link.");
            var fileName = $"Resource{index:D4}.bin";
            var target = Path.Combine(dependencyDirectory, fileName);
            File.Copy(dependency.Path, target, overwrite: false);
            var logicalName = $"PowerForge.Compiled.Resource{index:D4}";
            projectResources.Add($"    <EmbeddedResource Include=\"EmbeddedDependencies/{fileName}\" LogicalName=\"{EscapeXml(logicalName)}\" />");
            dependencySpecs.Add($"        new EmbeddedDependency({PowerShellCSharpLiteral.QuoteString(logicalName)}, {PowerShellCSharpLiteral.QuoteString(dependency.RelativePath)}, {PowerShellCSharpLiteral.QuoteString(ComputeSha256(target))}, {GetExecutableUnixMode(target)}),");
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
            usesExtractedRoot,
            nativeMirroredResources: nativeScriptEntry
                ? resourceDependencies.Where(static dependency => dependency.Metadata.Disposition == PowerShellCompilationDependencyDisposition.CopiedAdjacent)
                    .Select(static dependency => dependency.Metadata).ToArray()
                : Array.Empty<PowerShellCompilationDependency>());
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
        => PowerShellPackagedScriptRewriter.ValidateDependency(dependencyPath, embeddedScriptPaths, allowDerivedPaths: true);
}
