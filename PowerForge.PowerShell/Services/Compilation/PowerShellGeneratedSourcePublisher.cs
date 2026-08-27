namespace PowerForge;

/// <summary>
/// Publishes the exact generated source inputs needed to inspect and independently rebuild a compilation artifact.
/// </summary>
internal static class PowerShellGeneratedSourcePublisher
{
    internal static string CopyProject(
        string workspace,
        string projectPath,
        string artifactName,
        string artifactStagingDirectory,
        PowerShellCompilationBuildSpec spec,
        IReadOnlyCollection<PowerShellCompiledMethod>? methods)
    {
        var sourceDirectory = Path.Combine(artifactStagingDirectory, artifactName + ".generated");
        Directory.CreateDirectory(sourceDirectory);

        var files = Directory.EnumerateFiles(workspace, "*", SearchOption.TopDirectoryOnly)
            .Where(path => Path.GetExtension(path).Equals(".cs", StringComparison.OrdinalIgnoreCase) ||
                           Path.GetExtension(path).Equals(".ps1", StringComparison.OrdinalIgnoreCase) ||
                           PowerShellCompilationPathSafety.PathEquals(path, projectPath))
            .OrderBy(static path => Path.GetFileName(path), StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (!files.Contains(projectPath, PowerShellCompilationPathSafety.PathComparer))
            throw new InvalidOperationException("Generated source publication could not locate the generated project file.");
        if (!files.Any(path => Path.GetExtension(path).Equals(".cs", StringComparison.OrdinalIgnoreCase)))
            throw new InvalidOperationException("Generated source publication could not locate generated C# source.");

        foreach (var file in files)
            File.Copy(file, Path.Combine(sourceDirectory, Path.GetFileName(file)), overwrite: false);
        var embeddedDependencies = Path.Combine(workspace, "EmbeddedDependencies");
        if (Directory.Exists(embeddedDependencies))
        {
            var targetDependencies = Path.Combine(sourceDirectory, "EmbeddedDependencies");
            Directory.CreateDirectory(targetDependencies);
            foreach (var dependency in Directory.EnumerateFiles(embeddedDependencies, "*", SearchOption.TopDirectoryOnly))
                File.Copy(dependency, Path.Combine(targetDependencies, Path.GetFileName(dependency)), overwrite: false);
        }
        WriteBuildIsolationFiles(sourceDirectory, spec.TargetFramework);
        WriteSourceMap(sourceDirectory, spec, methods);
        return sourceDirectory;
    }

    private static void WriteBuildIsolationFiles(string sourceDirectory, string targetFramework)
    {
        var utf8 = new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
        File.WriteAllText(Path.Combine(sourceDirectory, "Directory.Build.props"), "<Project />" + Environment.NewLine, utf8);
        File.WriteAllText(Path.Combine(sourceDirectory, "Directory.Build.targets"), "<Project />" + Environment.NewLine, utf8);
        File.WriteAllText(
            Path.Combine(sourceDirectory, "Directory.Packages.props"),
            "<Project><PropertyGroup><ManagePackageVersionsCentrally>false</ManagePackageVersionsCentrally></PropertyGroup></Project>" + Environment.NewLine,
            utf8);
        var sdkVersion = targetFramework.Equals("net10.0", StringComparison.OrdinalIgnoreCase) ? "10.0.100" : "8.0.100";
        File.WriteAllText(
            Path.Combine(sourceDirectory, "global.json"),
            "{\n  \"sdk\": {\n    \"version\": \"" + sdkVersion + "\",\n    \"rollForward\": \"latestMajor\",\n    \"allowPrerelease\": true\n  }\n}\n",
            utf8);
    }

    private static void WriteSourceMap(
        string sourceDirectory,
        PowerShellCompilationBuildSpec spec,
        IReadOnlyCollection<PowerShellCompiledMethod>? methods)
    {
        var sourceRoot = Path.GetDirectoryName(Path.GetFullPath(spec.SourcePath)) ?? Directory.GetCurrentDirectory();
        var sourcePaths = new[] { spec.SourcePath }
            .Concat(spec.CompilationSourcePaths ?? Array.Empty<string>())
            .Select(Path.GetFullPath)
            .Distinct(System.Runtime.InteropServices.RuntimeInformation.IsOSPlatform(System.Runtime.InteropServices.OSPlatform.Windows)
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal)
            .ToArray();
        var map = new
        {
            schemaVersion = 1,
            rootSource = ToPortableRelativePath(sourceRoot, spec.SourcePath),
            sourceFiles = sourcePaths.Select(path => ToPortableRelativePath(sourceRoot, path)).ToArray(),
            methods = (methods ?? Array.Empty<PowerShellCompiledMethod>()).Select(method => new
            {
                powershellName = method.SourceName,
                generatedMethod = method.GeneratedName,
                sourceFile = ToPortableRelativePath(
                    sourceRoot,
                    string.IsNullOrWhiteSpace(method.SourcePath) ? spec.SourcePath : method.SourcePath),
                sourceLine = method.SourceLine,
                returnType = method.ReturnType
            }).ToArray()
        };
        var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(
            Path.Combine(sourceDirectory, "source-map.json"),
            System.Text.Json.JsonSerializer.Serialize(map, options),
            new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static string ToPortableRelativePath(string root, string path)
        => FrameworkCompatibility.GetRelativePath(root, Path.GetFullPath(path)).Replace('\\', '/');
}
