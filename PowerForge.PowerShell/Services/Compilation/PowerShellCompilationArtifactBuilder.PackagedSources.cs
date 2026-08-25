using System.Management.Automation.Language;

namespace PowerForge;

public sealed partial class PowerShellCompilationArtifactBuilder
{
    private static string GeneratePackagedScript(string sourcePath, PackagedSourceSet packagedSources)
        => PowerShellPackagedScriptRewriter.Rewrite(
            sourcePath,
            allowDotSource: packagedSources.HasDependencies,
            dependencyCommandPathExpression: packagedSources.HasDependencies
                ? "[PowerForge.Compiled.PowerForgePackagedEntryPoint]::Path"
                : null,
            embeddedResourceRelativePaths: packagedSources.EmbeddedResourceRelativePaths,
            packagedScriptRootExpression: packagedSources.UsesExtractedRoot
                ? "$([System.IO.Path]::GetDirectoryName([PowerForge.Compiled.PowerForgePackagedEntryPoint]::Path))"
                : null);

    private static PackagedSourceSet PreparePackagedSources(
        string workspace,
        string sourcePath,
        IEnumerable<string> compilationSourcePaths,
        IEnumerable<PowerShellCompilationDependency> dependencyPlan)
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
                usesExtractedRoot: false);

        var dependencyDirectory = Path.Combine(workspace, "EmbeddedDependencies");
        Directory.CreateDirectory(dependencyDirectory);
        var projectResources = new List<string>();
        var dependencySpecs = new List<string>();
        for (var index = 0; index < scriptDependencies.Length; index++)
        {
            var dependency = scriptDependencies[index];
            PowerShellCompilationPathSafety.EnsureContained(sourceRoot, dependency, $"Packaged dependency '{dependency}' escapes the executable entrypoint root.");
            RejectDependencyExits(dependency);
            var fileName = $"Dependency{index:D4}.ps1";
            File.Copy(dependency, Path.Combine(dependencyDirectory, fileName), overwrite: false);
            var logicalName = $"PowerForge.Compiled.{Path.GetFileNameWithoutExtension(fileName)}.ps1";
            var relativePath = FrameworkCompatibility.GetRelativePath(sourceRoot, dependency).Replace('\\', '/');
            projectResources.Add($"    <EmbeddedResource Include=\"EmbeddedDependencies/{fileName}\" LogicalName=\"{EscapeXml(logicalName)}\" />");
            dependencySpecs.Add($"        new EmbeddedDependency({PowerShellCSharpLiteral.QuoteString(logicalName)}, {PowerShellCSharpLiteral.QuoteString(relativePath)}, {GetExecutableUnixMode(dependency)}),");
        }
        for (var index = 0; index < resourceDependencies.Length; index++)
        {
            var dependency = resourceDependencies[index];
            PowerShellCompilationPathSafety.EnsureContained(sourceRoot, dependency.Path, $"Packaged resource '{dependency.RelativePath}' escapes the executable entrypoint root.");
            var fileName = $"Resource{index:D4}.bin";
            File.Copy(dependency.Path, Path.Combine(dependencyDirectory, fileName), overwrite: false);
            var logicalName = $"PowerForge.Compiled.Resource{index:D4}";
            projectResources.Add($"    <EmbeddedResource Include=\"EmbeddedDependencies/{fileName}\" LogicalName=\"{EscapeXml(logicalName)}\" />");
            dependencySpecs.Add($"        new EmbeddedDependency({PowerShellCSharpLiteral.QuoteString(logicalName)}, {PowerShellCSharpLiteral.QuoteString(dependency.RelativePath)}, {GetExecutableUnixMode(dependency.Path)}),");
        }
        var resourcePaths = resourceDependencies.Select(static dependency => dependency.RelativePath).ToArray();
        var usesExtractedRoot = resourceDependencies.Any(static dependency =>
            dependency.Selection is PowerShellCompilationDependencySelection.ExplicitInclude or
                PowerShellCompilationDependencySelection.PolicyInclude);
        return new PackagedSourceSet(
            Path.GetFileName(fullSourcePath),
            string.Join(Environment.NewLine, projectResources),
            string.Join(Environment.NewLine, dependencySpecs),
            hasDependencies: true,
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

    private static void RejectDependencyExits(string dependencyPath)
    {
        var ast = Parser.ParseFile(dependencyPath, out _, out var errors);
        if (errors.Length > 0)
            throw new InvalidOperationException($"Packaged dependency '{dependencyPath}' could not be parsed while validating exit semantics.");
        PowerShellPackagedScriptRewriter.ValidateHostInteraction(ast);
        var exit = ast.FindAll(static node => node is ExitStatementAst, searchNestedScriptBlocks: true)
            .Cast<ExitStatementAst>()
            .FirstOrDefault();
        if (exit is not null)
        {
            throw new InvalidOperationException(
                $"Packaged dependency '{dependencyPath}' contains exit at line {exit.Extent.StartLineNumber}; dependency exits cannot preserve executable process-exit semantics and must remain in the root entry script.");
        }
    }
}
