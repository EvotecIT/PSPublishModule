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
        var mappedMethods = (methods ?? Array.Empty<PowerShellCompiledMethod>()).Select(method =>
        {
            var generated = FindGeneratedMethod(sourceDirectory, method.GeneratedName);
            return new
            {
                powershellName = method.SourceName,
                generatedMethod = method.GeneratedName,
                sourceFile = ToPortableRelativePath(
                    sourceRoot,
                    string.IsNullOrWhiteSpace(method.SourcePath) ? spec.SourcePath : method.SourcePath),
                sourceLine = method.SourceLine,
                sourceRange = new
                {
                    startLine = method.SourceLine,
                    startColumn = method.SourceColumn,
                    endLine = method.SourceEndLine,
                    endColumn = method.SourceEndColumn
                },
                generatedFile = generated.FileName,
                generatedMethodLine = generated.Line,
                statements = method.SourceMap
                    .OrderBy(static entry => entry.SourceStartLine)
                    .ThenBy(static entry => entry.SourceStartColumn)
                    .ThenBy(static entry => entry.GeneratedStartLine)
                    .Select(entry => new
                    {
                        sourceRange = new
                        {
                            startLine = entry.SourceStartLine,
                            startColumn = entry.SourceStartColumn,
                            endLine = entry.SourceEndLine,
                            endColumn = entry.SourceEndColumn
                        },
                        generatedRange = new
                        {
                            startLine = generated.Line + entry.GeneratedStartLine - 1,
                            startColumn = entry.GeneratedStartColumn,
                            endLine = generated.Line + entry.GeneratedEndLine - 1,
                            endColumn = entry.GeneratedEndColumn
                        }
                    }).ToArray(),
                returnType = method.ReturnType
            };
        }).ToArray();
        var map = new
        {
            schemaVersion = 2,
            rootSource = ToPortableRelativePath(sourceRoot, spec.SourcePath),
            sourceFiles = sourcePaths.Select(path => ToPortableRelativePath(sourceRoot, path)).ToArray(),
            methods = mappedMethods
        };
        var options = new System.Text.Json.JsonSerializerOptions { WriteIndented = true };
        File.WriteAllText(
            Path.Combine(sourceDirectory, "source-map.json"),
            System.Text.Json.JsonSerializer.Serialize(map, options),
            new System.Text.UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static string ToPortableRelativePath(string root, string path)
        => FrameworkCompatibility.GetRelativePath(root, Path.GetFullPath(path)).Replace('\\', '/');

    private static GeneratedMethodLocation FindGeneratedMethod(string sourceDirectory, string generatedName)
    {
        var pattern = @"^\s*public\s+static\s+.*\s" +
                      System.Text.RegularExpressions.Regex.Escape(generatedName) + @"\s*\(";
        GeneratedMethodLocation? match = null;
        foreach (var path in Directory.EnumerateFiles(sourceDirectory, "*.cs", SearchOption.TopDirectoryOnly)
                     .OrderBy(static path => path, StringComparer.OrdinalIgnoreCase))
        {
            var lines = File.ReadAllLines(path);
            for (var index = 0; index < lines.Length; index++)
            {
                if (!System.Text.RegularExpressions.Regex.IsMatch(lines[index], pattern, System.Text.RegularExpressions.RegexOptions.CultureInvariant))
                    continue;
                if (match is not null)
                    throw new InvalidOperationException($"Generated method '{generatedName}' was found more than once while publishing source maps.");
                match = new GeneratedMethodLocation(Path.GetFileName(path), index + 1);
            }
        }
        return match ?? throw new InvalidOperationException($"Generated method '{generatedName}' was not found while publishing source maps.");
    }

    private sealed class GeneratedMethodLocation
    {
        internal GeneratedMethodLocation(string fileName, int line)
        {
            FileName = fileName;
            Line = line;
        }

        internal string FileName { get; }
        internal int Line { get; }
    }
}
