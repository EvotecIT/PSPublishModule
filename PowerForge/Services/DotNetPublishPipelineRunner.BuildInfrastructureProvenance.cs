using System.Text.Json;
using System.Xml.Linq;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private static bool ProjectDeclaresTargetFramework(string projectPath)
    {
        try
        {
            XDocument project = XDocument.Load(projectPath, LoadOptions.None);
            return project.Descendants().Any(element =>
                (element.Name.LocalName.Equals("TargetFramework", StringComparison.OrdinalIgnoreCase) ||
                 element.Name.LocalName.Equals("TargetFrameworks", StringComparison.OrdinalIgnoreCase)) &&
                !string.IsNullOrWhiteSpace(element.Value));
        }
        catch
        {
            return false;
        }
    }

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
        IEnumerable<string>? recordedGeneratedOutputPaths,
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
                    string.Equals(Path.GetFullPath(expectedPath), fullPath, comparison)) ||
                !(recordedGeneratedOutputPaths ?? Array.Empty<string>()).Any(recordedPath =>
                    string.Equals(Path.GetFullPath(recordedPath), fullPath, comparison)))
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

    private static string[] ReadRecordedGeneratedOutputPaths(
        JsonElement properties,
        string projectPath)
    {
        try
        {
            string projectDirectory = Path.GetDirectoryName(projectPath)!;
            string? intermediateOutputPath = ReadEvaluatedPath(
                properties,
                "IntermediateOutputPath",
                projectDirectory);
            if (string.IsNullOrWhiteSpace(intermediateOutputPath))
                return Array.Empty<string>();

            string cleanFile = ReadItemText(properties, "CleanFile") ??
                Path.GetFileName(projectPath) + ".FileListAbsolute.txt";
            if (string.IsNullOrWhiteSpace(cleanFile))
                return Array.Empty<string>();

            string recordPath = Path.GetFullPath(Path.IsPathRooted(cleanFile)
                ? cleanFile
                : Path.Combine(intermediateOutputPath!, cleanFile));
            if (!File.Exists(recordPath))
                return Array.Empty<string>();

            return File.ReadLines(recordPath)
                .Where(line => !string.IsNullOrWhiteSpace(line))
                .Select(line => Path.GetFullPath(Path.IsPathRooted(line)
                    ? line
                    : Path.Combine(projectDirectory, line)))
                .Where(File.Exists)
                .Distinct(IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal)
                .ToArray();
        }
        catch
        {
            // Missing or unreadable build-write evidence fails closed.
            return Array.Empty<string>();
        }
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
            string[] recordedGeneratedOutputPaths,
            GeneratedProjectReferenceOutput[] generatedProjectReferenceOutputs)
        {
            BuildInputs = buildInputs;
            SourceInputs = sourceInputs;
            ProjectReferences = projectReferences;
            TargetFrameworks = targetFrameworks;
            OutputRoots = outputRoots;
            ExpectedOutputPaths = expectedOutputPaths;
            RecordedGeneratedOutputPaths = recordedGeneratedOutputPaths;
            GeneratedProjectReferenceOutputs = generatedProjectReferenceOutputs;
        }

        internal string[] BuildInputs { get; }
        internal string[] SourceInputs { get; }
        internal EvaluatedProjectReference[] ProjectReferences { get; }
        internal string[] TargetFrameworks { get; }
        internal string[] OutputRoots { get; }
        internal string[] ExpectedOutputPaths { get; }
        internal string[] RecordedGeneratedOutputPaths { get; }
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
