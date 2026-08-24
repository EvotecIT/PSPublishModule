using System.Text.Json;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private static bool IsGeneratedBuildInfrastructurePath(
        string path,
        IEnumerable<string>? generatedBuildRoots)
    {
        if (!IsBelowGeneratedBuildRoot(path, generatedBuildRoots))
            return false;

        string fileName = Path.GetFileName(path);
        return fileName.EndsWith(".nuget.g.props", StringComparison.OrdinalIgnoreCase) ||
               fileName.EndsWith(".nuget.g.targets", StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsBelowGeneratedBuildRoot(
        string path,
        IEnumerable<string>? generatedBuildRoots)
        => (generatedBuildRoots ?? Array.Empty<string>())
            .Any(root => IsSameOrBelowBuildInputPath(path, root));

    private static bool IsTrustedGeneratedOutputPath(
        string path,
        IEnumerable<string>? outputRoots,
        IEnumerable<string>? expectedOutputPaths,
        string referencedProjectDirectory,
        IEnumerable<string> evaluatedProjectDirectories)
    {
        try
        {
            string fullPath = Path.GetFullPath(path);
            StringComparison comparison = IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            if (!(expectedOutputPaths ?? Array.Empty<string>()).Any(expectedPath =>
                    string.Equals(Path.GetFullPath(expectedPath), fullPath, comparison)))
            {
                return false;
            }
        }
        catch
        {
            return false;
        }

        foreach (string root in outputRoots ?? Array.Empty<string>())
        {
            try
            {
                string traversalBoundary = FindCommonBuildInputPathRoot(
                    root,
                    referencedProjectDirectory);
                if (!IsSameOrBelowBuildInputPath(path, root) ||
                    evaluatedProjectDirectories.Any(projectDirectory =>
                        IsSameOrBelowBuildInputPath(projectDirectory, root)) ||
                    IsTrackedProjectOutputPath(path, referencedProjectDirectory) ||
                    !HasSinglePhysicalLink(path) ||
                    IsReparsePointPath(root) ||
                    IsReparsePointPath(traversalBoundary) ||
                    HasReparsePointBelowRoot(path, traversalBoundary))
                {
                    continue;
                }

                return true;
            }
            catch
            {
                // Generated outputs are trusted only when physical containment can be proven.
            }
        }

        return false;
    }

    private static bool HasSinglePhysicalLink(string path)
    {
        try
        {
            return ExistingFilePathIdentityResolver.ResolveHardLinkCounts(new[] { path })[0] == 1;
        }
        catch
        {
            return false;
        }
    }

    private static bool TryProveControlledGeneratedOutput(
        ProjectEvaluationRequest request,
        string candidatePath,
        string? evaluatedIntermediateRoot,
        string? evaluatedIntermediateOutputPath,
        string? evaluatedPathMap,
        IDictionary<string, bool> cache)
    {
        string fullCandidatePath = Path.GetFullPath(candidatePath);
        string cacheKey = request.BuildVisitKey() + "\0" +
            (IsWindows() ? fullCandidatePath.ToUpperInvariant() : fullCandidatePath);
        if (cache.TryGetValue(cacheKey, out bool cachedResult))
            return cachedResult;

        string controlledOutputRoot = Path.Combine(
            Path.GetTempPath(),
            "powerforge-provenance-build-" + Guid.NewGuid().ToString("N"));
        string controlledIntermediateRoot = Path.Combine(controlledOutputRoot, "obj");
        string controlledBinaryRoot = Path.Combine(controlledOutputRoot, "bin");
        string controlledSourceRoot = Path.Combine(controlledOutputRoot, "source");
        string? controlledGitRoot = null;
        try
        {
            if (!File.Exists(fullCandidatePath))
                return Cache(false);
            Directory.CreateDirectory(controlledOutputRoot);
            if (!TryCreateControlledSourceCheckout(
                    request.ProjectPath,
                    controlledSourceRoot,
                    out controlledGitRoot,
                    out string? controlledProjectPath))
            {
                return Cache(false);
            }
            var arguments = new List<string>
            {
                "msbuild",
                controlledProjectPath!,
                "-nologo",
                "-verbosity:quiet",
                "-restore",
                "-target:Rebuild",
                "-getProperty:TargetPath",
                "-getItem:FileWrites"
            };
            if (request.Configuration is not null)
                arguments.Add("-p:Configuration=" + EscapeMsBuildPropertyValue(request.Configuration));
            if (request.TargetFramework is not null)
                arguments.Add("-p:TargetFramework=" + EscapeMsBuildPropertyValue(request.TargetFramework));
            foreach (KeyValuePair<string, string> property in request.GlobalProperties.OrderBy(
                         entry => entry.Key,
                         StringComparer.OrdinalIgnoreCase))
            {
                if (property.Key.Equals("Configuration", StringComparison.OrdinalIgnoreCase) ||
                    property.Key.Equals("TargetFramework", StringComparison.OrdinalIgnoreCase) ||
                    property.Key.Equals("BuildProjectReferences", StringComparison.OrdinalIgnoreCase) ||
                    property.Key.Equals("NuGetLockFilePath", StringComparison.OrdinalIgnoreCase) ||
                    property.Key.Equals("PathMap", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }
                arguments.Add("-p:" + property.Key + "=" + EscapeMsBuildPropertyValue(property.Value));
            }
            arguments.Add("-p:BuildProjectReferences=false");
            arguments.Add("-p:NuGetLockFilePath=" + EscapeMsBuildPropertyValue(
                Path.Combine(controlledIntermediateRoot, "packages.lock.json")));
            arguments.Add("-p:OutDir=" + EscapeMsBuildPropertyValue(
                controlledBinaryRoot + Path.DirectorySeparatorChar));
            arguments.Add("-p:MSBuildProjectExtensionsPath=" + EscapeMsBuildPropertyValue(
                controlledIntermediateRoot + Path.DirectorySeparatorChar));
            string controlledIntermediateOutputPath = Path.Combine(
                controlledIntermediateRoot,
                request.Configuration ?? "Release",
                request.TargetFramework ?? string.Empty);
            if (!string.IsNullOrWhiteSpace(evaluatedIntermediateRoot) &&
                !string.IsNullOrWhiteSpace(evaluatedIntermediateOutputPath))
            {
                string normalizedIntermediateRoot = NormalizeBuildInputPathRoot(evaluatedIntermediateRoot!);
                string normalizedIntermediateOutputPath = Path.GetFullPath(evaluatedIntermediateOutputPath!);
                if (!IsSameOrBelowBuildInputPath(
                        normalizedIntermediateOutputPath,
                        normalizedIntermediateRoot))
                {
                    return Cache(false);
                }
                string relativeIntermediatePath = FrameworkCompatibility.GetRelativePath(
                    normalizedIntermediateRoot,
                    normalizedIntermediateOutputPath);
                controlledIntermediateOutputPath = Path.Combine(
                    controlledIntermediateRoot,
                    relativeIntermediatePath);
            }
            arguments.Add("-p:IntermediateOutputPath=" + EscapeMsBuildPropertyValue(
                controlledIntermediateOutputPath + Path.DirectorySeparatorChar));
            string controlledPathMap = evaluatedPathMap ?? string.Empty;
            if (!TryBuildControlledPathMap(
                    controlledSourceRoot,
                    controlledGitRoot!,
                    controlledPathMap,
                    out controlledPathMap))
            {
                return Cache(false);
            }
            if (!string.IsNullOrWhiteSpace(evaluatedIntermediateRoot))
            {
                if (!TryBuildControlledPathMap(
                        controlledIntermediateRoot,
                        evaluatedIntermediateRoot!,
                        controlledPathMap,
                        out controlledPathMap))
                {
                    return Cache(false);
                }
            }
            arguments.Add("-p:PathMap=" + EscapeMsBuildPropertyValue(controlledPathMap));

            var process = RunBuildInputEvaluationProcess(
                "dotnet",
                Path.GetDirectoryName(controlledProjectPath!)!,
                arguments,
                request.EnvironmentVariables,
                TimeSpan.FromMinutes(5));
            if (process.ExitCode != 0 || process.TimedOut)
                return Cache(false);

            int jsonStart = process.StdOut.IndexOf('{');
            int jsonEnd = process.StdOut.LastIndexOf('}');
            if (jsonStart < 0 || jsonEnd < jsonStart)
                return Cache(false);
            using JsonDocument document = JsonDocument.Parse(
                process.StdOut.Substring(jsonStart, jsonEnd - jsonStart + 1));
            if (!document.RootElement.TryGetProperty("Properties", out JsonElement properties) ||
                !properties.TryGetProperty("TargetPath", out JsonElement targetPathElement) ||
                targetPathElement.ValueKind != JsonValueKind.String ||
                string.IsNullOrWhiteSpace(targetPathElement.GetString()) ||
                !document.RootElement.TryGetProperty("Items", out JsonElement items) ||
                !items.TryGetProperty("FileWrites", out JsonElement fileWrites) ||
                fileWrites.ValueKind != JsonValueKind.Array)
            {
                return Cache(false);
            }

            string targetPath = Path.GetFullPath(targetPathElement.GetString()!);
            StringComparer comparer = IsWindows()
                ? StringComparer.OrdinalIgnoreCase
                : StringComparer.Ordinal;
            string[] writtenPaths = fileWrites.EnumerateArray()
                .Select(item => ReadItemText(item, "FullPath") ?? ReadItemText(item, "Identity"))
                .Where(value => !string.IsNullOrWhiteSpace(value))
                .Select(value => Path.GetFullPath(Path.IsPathRooted(value!)
                    ? value!
                    : Path.Combine(Path.GetDirectoryName(request.ProjectPath)!, value!)))
                .Where(File.Exists)
                .Distinct(comparer)
                .ToArray();
            if (!writtenPaths.Contains(targetPath, comparer) ||
                !File.Exists(targetPath) ||
                !IsSameOrBelowBuildInputPath(targetPath, controlledOutputRoot))
            {
                return Cache(false);
            }

            return Cache(AreControlledGeneratedOutputsEquivalent(
                fullCandidatePath,
                targetPath));
        }
        catch
        {
            // A generated output is trusted only when the controlled rebuild proves it.
            return Cache(false);
        }
        finally
        {
            RemoveControlledSourceCheckout(controlledGitRoot, controlledSourceRoot);
            try
            {
                if (Directory.Exists(controlledOutputRoot))
                    Directory.Delete(controlledOutputRoot, recursive: true);
            }
            catch
            {
                // Temporary proof output is best-effort cleanup only.
            }
        }

        bool Cache(bool result)
        {
            cache[cacheKey] = result;
            return result;
        }
    }

    private static bool TryBuildControlledPathMap(
        string controlledIntermediateRoot,
        string evaluatedIntermediateRoot,
        string? evaluatedPathMap,
        out string pathMap)
    {
        string originalRoot = NormalizeBuildInputPathRoot(evaluatedIntermediateRoot);
        string mappedOriginalRoot = originalRoot;
        if (!string.IsNullOrWhiteSpace(evaluatedPathMap))
        {
            foreach (string entry in evaluatedPathMap!.Split(','))
            {
                int separator = entry.IndexOf('=');
                if (separator <= 0)
                {
                    pathMap = string.Empty;
                    return false;
                }

                string source = entry.Substring(0, separator).Trim();
                string target = entry.Substring(separator + 1).Trim();
                if (source.Length == 0 || target.Length == 0)
                {
                    pathMap = string.Empty;
                    return false;
                }

                if (!IsSameOrBelowBuildInputPath(originalRoot, source))
                    continue;
                string suffix = originalRoot.Substring(NormalizeBuildInputPathRoot(source).Length)
                    .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                mappedOriginalRoot = suffix.Length == 0
                    ? target
                    : target.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) +
                      Path.DirectorySeparatorChar + suffix;
                break;
            }
        }

        string controlledMapping = NormalizeBuildInputPathRoot(controlledIntermediateRoot) + "=" +
                                   mappedOriginalRoot;
        pathMap = string.IsNullOrWhiteSpace(evaluatedPathMap)
            ? controlledMapping
            : controlledMapping + "," + evaluatedPathMap;
        return true;
    }

    private static bool IsTrackedProjectOutputPath(string path, string projectDirectory)
    {
        string outputDirectory = Path.GetDirectoryName(Path.GetFullPath(path))!;
        foreach (string candidateDirectory in new[] { projectDirectory, outputDirectory }
                     .Distinct(IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal))
        {
            string? gitRoot = ReadGitText(candidateDirectory, "rev-parse --show-toplevel");
            if (string.IsNullOrWhiteSpace(gitRoot))
                continue;

            string? relativePath = ToGitRelativeExclusion(candidateDirectory, gitRoot!, path);
            if (relativePath is null)
                continue;

            string? trackedOutput = ReadGitRawText(
                gitRoot!,
                $"ls-files --stage -- {QuoteLiteralGitPath(relativePath)}");
            if (trackedOutput is null || trackedOutput.Length > 0)
                return true;
        }

        return false;
    }

    private static string FindCommonBuildInputPathRoot(string firstPath, string secondPath)
    {
        string current = NormalizeBuildInputPathRoot(firstPath);
        string second = Path.GetFullPath(secondPath);
        while (!IsSameOrBelowBuildInputPath(second, current))
        {
            string? parent = Path.GetDirectoryName(current);
            if (string.IsNullOrWhiteSpace(parent) ||
                string.Equals(
                    parent,
                    current,
                    IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
            {
                return current;
            }
            current = NormalizeBuildInputPathRoot(parent);
        }
        return current;
    }

    private static string NormalizeBuildInputPathRoot(string path)
    {
        string fullPath = Path.GetFullPath(path);
        string trimmed = fullPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        string pathRoot = Path.GetPathRoot(fullPath)!;
        string trimmedPathRoot = pathRoot.TrimEnd(
            Path.DirectorySeparatorChar,
            Path.AltDirectorySeparatorChar);
        return trimmed.Length == 0 ||
               string.Equals(
                   trimmed,
                   trimmedPathRoot,
                   IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal)
            ? pathRoot
            : trimmed;
    }

    private static bool IsReparsePointPath(string path)
    {
        try
        {
            return (File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0;
        }
        catch
        {
            return true;
        }
    }

    private static void AddPropertyPath(
        JsonElement properties,
        string name,
        string baseDirectory,
        HashSet<string> values)
    {
        if (!properties.TryGetProperty(name, out JsonElement property) ||
            property.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(property.GetString()))
        {
            return;
        }

        string value = property.GetString()!;
        values.Add(Path.GetFullPath(Path.IsPathRooted(value) ? value : Path.Combine(baseDirectory, value)));
    }

    private static bool IsTrustedExternalBuildInfrastructurePath(string path)
        => IsTrustedExternalBuildInfrastructurePath(path, Array.Empty<string>());

    private static bool IsTrustedExternalBuildInfrastructurePath(
        string path,
        IEnumerable<string>? evaluatedRoots)
    {
        string fullPath = Path.GetFullPath(path);
        var roots = new HashSet<string>(
            evaluatedRoots ?? Array.Empty<string>(),
            IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
        AddEnvironmentDirectory(roots, "DOTNET_ROOT");
        AddEnvironmentDirectory(roots, "DOTNET_ROOT(x86)");
        AddEnvironmentDirectory(roots, "MSBuildSDKsPath");
        AddEnvironmentDirectory(roots, "MSBuildExtensionsPath");
        AddEnvironmentDirectory(roots, "NUGET_PACKAGES");

        string? programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (!string.IsNullOrWhiteSpace(programFiles))
            roots.Add(Path.GetFullPath(Path.Combine(programFiles, "dotnet")));
        string? programFilesX86 = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86);
        if (!string.IsNullOrWhiteSpace(programFilesX86))
            roots.Add(Path.GetFullPath(Path.Combine(programFilesX86, "dotnet")));
        string? userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        if (!string.IsNullOrWhiteSpace(userProfile))
        {
            roots.Add(Path.GetFullPath(Path.Combine(userProfile, ".dotnet")));
            roots.Add(Path.GetFullPath(Path.Combine(userProfile, ".nuget", "packages")));
        }

        return roots.Any(root => IsSameOrBelowBuildInputPath(fullPath, root));
    }

    private static string[] ReadTrustedBuildInfrastructureRoots(
        JsonElement properties,
        string projectDirectory)
    {
        string? toolsPath = ReadEvaluatedPath(properties, "MSBuildToolsPath", projectDirectory);
        if (string.IsNullOrWhiteSpace(toolsPath))
            return Array.Empty<string>();

        var versionDirectory = new DirectoryInfo(toolsPath!);
        DirectoryInfo? sdkDirectory = versionDirectory.Parent;
        if (sdkDirectory is not null &&
            sdkDirectory.Name.Equals("sdk", StringComparison.OrdinalIgnoreCase) &&
            sdkDirectory.Parent is not null)
        {
            return [sdkDirectory.Parent.FullName];
        }

        return [versionDirectory.FullName];
    }

    private static void AddEnvironmentDirectory(HashSet<string> roots, string name)
    {
        string? value = Environment.GetEnvironmentVariable(name);
        if (!string.IsNullOrWhiteSpace(value))
            roots.Add(Path.GetFullPath(value));
    }

    private static bool IsSameOrBelowBuildInputPath(string path, string root)
    {
        string fullRoot = NormalizeBuildInputPathRoot(root);
        StringComparison comparison = IsWindows()
            ? StringComparison.OrdinalIgnoreCase
            : StringComparison.Ordinal;
        string separator = fullRoot.EndsWith(Path.DirectorySeparatorChar.ToString(), StringComparison.Ordinal) ||
                           fullRoot.EndsWith(Path.AltDirectorySeparatorChar.ToString(), StringComparison.Ordinal)
            ? string.Empty
            : Path.DirectorySeparatorChar.ToString();
        return string.Equals(Path.GetFullPath(path), fullRoot, comparison) ||
               Path.GetFullPath(path).StartsWith(fullRoot + separator, comparison);
    }

    private sealed class ProjectEvaluationRequest
    {
        internal ProjectEvaluationRequest(
            string projectPath,
            string? targetFramework,
            string? configuration,
            IReadOnlyDictionary<string, string>? globalProperties,
            IReadOnlyDictionary<string, string?>? environmentVariables)
        {
            ProjectPath = projectPath;
            TargetFramework = targetFramework;
            Configuration = configuration;
            GlobalProperties = globalProperties ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            EnvironmentVariables = environmentVariables ?? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
        }

        internal string ProjectPath { get; }
        internal string? TargetFramework { get; }
        internal string? Configuration { get; }
        internal IReadOnlyDictionary<string, string> GlobalProperties { get; }
        internal IReadOnlyDictionary<string, string?> EnvironmentVariables { get; }

        internal ProjectEvaluationRequest ForProject(string projectPath, string? targetFramework)
            => new(
                Path.GetFullPath(projectPath),
                targetFramework,
                Configuration,
                GlobalProperties,
                EnvironmentVariables);

        internal ProjectEvaluationRequest ForProject(EvaluatedProjectReference projectReference)
        {
            var properties = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (KeyValuePair<string, string> property in GlobalProperties)
                properties[property.Key] = property.Value;
            foreach (KeyValuePair<string, string> property in projectReference.GlobalProperties)
                properties[property.Key] = property.Value;
            foreach (string propertyName in projectReference.UndefineProperties)
                properties.Remove(propertyName);

            bool undefinesConfiguration = projectReference.UndefineProperties.Contains(
                "Configuration",
                StringComparer.OrdinalIgnoreCase);
            bool undefinesTargetFramework = projectReference.UndefineProperties.Contains(
                "TargetFramework",
                StringComparer.OrdinalIgnoreCase);
            string? configuration = undefinesConfiguration
                ? null
                : properties.TryGetValue("Configuration", out string? childConfiguration)
                    ? childConfiguration
                    : Configuration;
            string? targetFramework = undefinesTargetFramework
                ? null
                : projectReference.TargetFramework;
            properties.Remove("Configuration");
            properties.Remove("TargetFramework");
            return new ProjectEvaluationRequest(
                Path.GetFullPath(projectReference.ProjectPath),
                targetFramework,
                configuration,
                properties,
                EnvironmentVariables);
        }

        internal string BuildVisitKey()
        {
            var key = new System.Text.StringBuilder();
            AppendProjectReferenceKeySegment(key, "ProjectPath");
            AppendProjectReferenceKeySegment(key, NormalizeProjectReferenceIdentityPath(ProjectPath));
            AppendProjectReferenceKeySegment(key, "TargetFramework");
            AppendProjectReferenceKeySegment(key, TargetFramework is null ? "Undefined" : "Defined");
            AppendProjectReferenceKeySegment(key, TargetFramework ?? string.Empty);
            AppendProjectReferenceKeySegment(key, "Configuration");
            AppendProjectReferenceKeySegment(key, Configuration is null ? "Undefined" : "Defined");
            AppendProjectReferenceKeySegment(key, Configuration ?? string.Empty);
            foreach (KeyValuePair<string, string> property in GlobalProperties.OrderBy(
                         entry => entry.Key,
                         StringComparer.OrdinalIgnoreCase))
            {
                AppendProjectReferenceKeySegment(key, "Property");
                AppendProjectReferenceKeySegment(key, NormalizeMsBuildPropertyIdentityName(property.Key));
                AppendProjectReferenceKeySegment(key, property.Value);
            }
            foreach (KeyValuePair<string, string?> environmentVariable in EnvironmentVariables.OrderBy(
                         entry => entry.Key,
                         StringComparer.OrdinalIgnoreCase))
            {
                AppendProjectReferenceKeySegment(key, "Environment");
                AppendProjectReferenceKeySegment(key, NormalizeEnvironmentIdentityName(environmentVariable.Key));
                AppendProjectReferenceKeySegment(key, environmentVariable.Value ?? string.Empty);
            }
            return key.ToString();
        }
    }

    private sealed class EvaluatedProjectInputs
    {
        internal EvaluatedProjectInputs(
            string[] buildInputs,
            string[] sourceInputs,
            EvaluatedProjectReference[] projectReferences,
            string[] targetFrameworks,
            string[] outputRoots,
            string[] expectedOutputPaths,
            string? intermediateRoot,
            string? intermediateOutputPath,
            string? pathMap,
            GeneratedProjectReferenceOutput[] generatedProjectReferenceOutputs)
        {
            BuildInputs = buildInputs;
            SourceInputs = sourceInputs;
            ProjectReferences = projectReferences;
            TargetFrameworks = targetFrameworks;
            OutputRoots = outputRoots;
            ExpectedOutputPaths = expectedOutputPaths;
            IntermediateRoot = intermediateRoot;
            IntermediateOutputPath = intermediateOutputPath;
            PathMap = pathMap;
            GeneratedProjectReferenceOutputs = generatedProjectReferenceOutputs;
        }

        internal string[] BuildInputs { get; }
        internal string[] SourceInputs { get; }
        internal EvaluatedProjectReference[] ProjectReferences { get; }
        internal string[] TargetFrameworks { get; }
        internal string[] OutputRoots { get; }
        internal string[] ExpectedOutputPaths { get; }
        internal string? IntermediateRoot { get; }
        internal string? IntermediateOutputPath { get; }
        internal string? PathMap { get; }
        internal GeneratedProjectReferenceOutput[] GeneratedProjectReferenceOutputs { get; }
    }

    private sealed class EvaluatedProjectReference
    {
        internal EvaluatedProjectReference(
            string projectPath,
            string? targetFramework,
            IReadOnlyDictionary<string, string>? globalProperties = null,
            string[]? undefineProperties = null)
        {
            ProjectPath = projectPath;
            TargetFramework = targetFramework;
            GlobalProperties = globalProperties ?? new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            UndefineProperties = undefineProperties ?? Array.Empty<string>();
        }

        internal string ProjectPath { get; }
        internal string? TargetFramework { get; }
        internal IReadOnlyDictionary<string, string> GlobalProperties { get; }
        internal string[] UndefineProperties { get; }
    }

    private sealed class GeneratedProjectReferenceOutput
    {
        internal GeneratedProjectReferenceOutput(string outputPath, EvaluatedProjectReference projectReference)
        {
            OutputPath = outputPath;
            ProjectReference = projectReference;
        }

        internal string OutputPath { get; }
        internal EvaluatedProjectReference ProjectReference { get; }
    }
}
