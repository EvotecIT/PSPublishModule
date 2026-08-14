using System.Diagnostics;
using System.Text.Json;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private static readonly string[] EvaluatedBuildItemNames =
    [
        "Compile",
        "Content",
        "EmbeddedResource",
        "AdditionalFiles",
        "Analyzer",
        "ApplicationDefinition",
        "Page",
        "Resource",
        "SplashScreen",
        "RazorComponent",
        "TypeScriptCompile",
        "None",
        "ProjectReference"
    ];

    private static bool TryEvaluateDotNetBuildInputs(
        IEnumerable<string>? projectPaths,
        string? configuration,
        DotNetPublishPlan? buildPlan,
        out string[] projectDirectories,
        out HashSet<string> buildInputs)
    {
        var comparison = IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
        var pending = new Queue<ProjectEvaluationRequest>(BuildProjectEvaluationRequests(
            projectPaths,
            configuration,
            buildPlan));
        var visited = new HashSet<string>(comparison);
        var directories = new HashSet<string>(comparison);
        buildInputs = new HashSet<string>(comparison);

        while (pending.Count > 0)
        {
            ProjectEvaluationRequest request = pending.Dequeue();
            string visitKey = request.BuildVisitKey();
            if (!visited.Add(visitKey) || !File.Exists(request.ProjectPath))
                continue;

            string projectDirectory = Path.GetDirectoryName(request.ProjectPath)!;
            directories.Add(projectDirectory);
            buildInputs.Add(request.ProjectPath);
            if (!TryReadEvaluatedProjectInputs(request, out EvaluatedProjectInputs? evaluation) || evaluation is null)
            {
                projectDirectories = directories.ToArray();
                return false;
            }

            foreach (string input in evaluation.BuildInputs)
                buildInputs.Add(input);
            if (request.TargetFramework is null)
            {
                if (evaluation.TargetFrameworks.Length > 0)
                {
                    foreach (string targetFramework in evaluation.TargetFrameworks)
                        pending.Enqueue(request.ForProject(request.ProjectPath, targetFramework));
                }
                else
                {
                    foreach (EvaluatedProjectReference projectReference in evaluation.ProjectReferences)
                        pending.Enqueue(request.ForProject(projectReference.ProjectPath, targetFramework: null));
                }
            }
            else
            {
                foreach (EvaluatedProjectReference projectReference in evaluation.ProjectReferences)
                    pending.Enqueue(request.ForProject(projectReference.ProjectPath, projectReference.TargetFramework));
            }
        }

        projectDirectories = directories.ToArray();
        return true;
    }

    private static IEnumerable<ProjectEvaluationRequest> BuildProjectEvaluationRequests(
        IEnumerable<string>? projectPaths,
        string? configuration,
        DotNetPublishPlan? buildPlan)
    {
        string effectiveConfiguration = string.IsNullOrWhiteSpace(buildPlan?.Configuration)
            ? string.IsNullOrWhiteSpace(configuration) ? "Release" : configuration!.Trim()
            : buildPlan!.Configuration.Trim();
        DotNetPublishTargetPlan[] targets = buildPlan?.Targets ?? Array.Empty<DotNetPublishTargetPlan>();
        if (targets.Length > 0)
        {
            foreach (DotNetPublishTargetPlan target in targets)
            {
                DotNetPublishTargetCombination[] combinations = target.Combinations ?? Array.Empty<DotNetPublishTargetCombination>();
                if (combinations.Length == 0)
                {
                    yield return new ProjectEvaluationRequest(
                        Path.GetFullPath(target.ProjectPath),
                        targetFramework: null,
                        effectiveConfiguration,
                        globalProperties: null,
                        buildPlan!.EnvironmentVariables);
                    continue;
                }

                foreach (DotNetPublishTargetCombination combination in combinations)
                {
                    Dictionary<string, string> properties = BuildPublishEvaluationProperties(
                        buildPlan!,
                        target,
                        combination);
                    yield return new ProjectEvaluationRequest(
                        Path.GetFullPath(target.ProjectPath),
                        combination.Framework,
                        effectiveConfiguration,
                        properties,
                        buildPlan!.EnvironmentVariables);
                }
            }

            yield break;
        }

        foreach (string path in projectPaths ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(path))
                continue;
            yield return new ProjectEvaluationRequest(
                Path.GetFullPath(path),
                targetFramework: null,
                effectiveConfiguration,
                globalProperties: null,
                environmentVariables: null);
        }
    }

    private static Dictionary<string, string> BuildPublishEvaluationProperties(
        DotNetPublishPlan plan,
        DotNetPublishTargetPlan target,
        DotNetPublishTargetCombination combination)
    {
        Dictionary<string, string> properties = BuildPublishMsBuildProperties(
            plan,
            target,
            combination.Framework,
            combination.Runtime,
            combination.Style);
        if (!string.IsNullOrWhiteSpace(combination.Runtime))
            properties["RuntimeIdentifier"] = combination.Runtime;

        if (IsPortableStyle(combination.Style))
        {
            properties["SelfContained"] = "true";
            properties["PublishSingleFile"] = "true";
            properties["IncludeNativeLibrariesForSelfExtract"] = "true";
            properties["PortableTrim"] = (combination.Style == DotNetPublishStyle.PortableSize).ToString().ToLowerInvariant();
            properties["PortableTrimMode"] = combination.Style == DotNetPublishStyle.PortableSize ? "full" : "partial";
            if (target.Publish.ReadyToRun.HasValue)
                properties["PublishReadyToRun"] = target.Publish.ReadyToRun.Value.ToString().ToLowerInvariant();
        }
        else if (combination.Style == DotNetPublishStyle.AotSpeed || combination.Style == DotNetPublishStyle.AotSize)
        {
            properties["SelfContained"] = "true";
            properties["PublishAot"] = "true";
            properties["StripSymbols"] = "true";
            properties["IlcOptimizationPreference"] = combination.Style == DotNetPublishStyle.AotSize ? "Size" : "Speed";
            properties["InvariantGlobalization"] = "false";
        }

        return properties;
    }

    private static bool TryReadEvaluatedProjectInputs(
        ProjectEvaluationRequest request,
        out EvaluatedProjectInputs? evaluation)
    {
        evaluation = null;
        var arguments = new List<string>
        {
            "msbuild",
            request.ProjectPath,
            "-nologo",
            "-verbosity:quiet",
            "-getProperty:TargetFramework",
            "-getProperty:TargetFrameworks",
            "-getProperty:MSBuildAllProjects",
            "-p:Configuration=" + request.Configuration
        };
        foreach (string itemName in EvaluatedBuildItemNames)
            arguments.Add("-getItem:" + itemName);
        if (!string.IsNullOrWhiteSpace(request.TargetFramework))
            arguments.Add("-p:TargetFramework=" + request.TargetFramework);
        foreach (KeyValuePair<string, string> property in request.GlobalProperties.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (property.Key.Equals("Configuration", StringComparison.OrdinalIgnoreCase) ||
                property.Key.Equals("TargetFramework", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            arguments.Add("-p:" + property.Key + "=" + property.Value);
        }

        try
        {
            var process = RunBuildInputEvaluationProcess(
                "dotnet",
                Path.GetDirectoryName(request.ProjectPath)!,
                arguments,
                request.EnvironmentVariables,
                TimeSpan.FromMinutes(2));
            if (process.ExitCode != 0 || process.TimedOut)
                return false;

            int jsonStart = process.StdOut.IndexOf('{');
            int jsonEnd = process.StdOut.LastIndexOf('}');
            if (jsonStart < 0 || jsonEnd < jsonStart)
                return false;
            using JsonDocument document = JsonDocument.Parse(
                process.StdOut.Substring(jsonStart, jsonEnd - jsonStart + 1));
            JsonElement root = document.RootElement;
            var inputs = new HashSet<string>(IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            var references = new Dictionary<string, EvaluatedProjectReference>(
                IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            var rawReferences = new HashSet<string>(
                IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
            var targetFrameworks = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            if (root.TryGetProperty("Properties", out JsonElement properties))
            {
                AddSemicolonSeparatedPaths(
                    properties,
                    "MSBuildAllProjects",
                    Path.GetDirectoryName(request.ProjectPath)!,
                    inputs);
                AddSemicolonSeparatedValues(properties, "TargetFrameworks", targetFrameworks);
                if (targetFrameworks.Count == 0)
                    AddSemicolonSeparatedValues(properties, "TargetFramework", targetFrameworks);
            }
            if (root.TryGetProperty("Items", out JsonElement items))
            {
                foreach (string itemName in EvaluatedBuildItemNames)
                {
                    if (!items.TryGetProperty(itemName, out JsonElement values) || values.ValueKind != JsonValueKind.Array)
                        continue;
                    foreach (JsonElement item in values.EnumerateArray())
                    {
                        if (!item.TryGetProperty("FullPath", out JsonElement fullPathElement) ||
                            fullPathElement.ValueKind != JsonValueKind.String ||
                            string.IsNullOrWhiteSpace(fullPathElement.GetString()))
                        {
                            continue;
                        }
                        string fullPath = Path.GetFullPath(fullPathElement.GetString()!);
                        if (itemName.Equals("ProjectReference", StringComparison.Ordinal))
                        {
                            inputs.Add(fullPath);
                            rawReferences.Add(fullPath);
                        }
                        else if (!itemName.Equals("None", StringComparison.Ordinal) || IsOutputRelevantNoneItem(item))
                        {
                            inputs.Add(fullPath);
                        }
                    }
                }

            }

            if (string.IsNullOrWhiteSpace(request.TargetFramework))
            {
                foreach (string projectReference in rawReferences)
                    references[projectReference] = new EvaluatedProjectReference(projectReference, targetFramework: null);
            }
            else if (rawReferences.Count > 0)
            {
                if (!TryReadResolvedProjectReferences(request, out EvaluatedProjectReference[] resolvedReferences))
                    return false;
                foreach (EvaluatedProjectReference reference in resolvedReferences)
                {
                    references[reference.ProjectPath] = reference;
                    inputs.Add(reference.ProjectPath);
                }
            }

            evaluation = new EvaluatedProjectInputs(
                inputs.ToArray(),
                references.Values.ToArray(),
                targetFrameworks.Where(value => !string.IsNullOrWhiteSpace(value)).ToArray());
            return true;
        }
        catch
        {
            return false;
        }
    }

    internal static (int ExitCode, string StdOut, string StdErr, bool TimedOut) RunBuildInputEvaluationProcess(
        string fileName,
        string workingDirectory,
        IReadOnlyList<string> arguments,
        IReadOnlyDictionary<string, string?>? environmentVariables,
        TimeSpan timeout)
        => RunProcessCore(
            fileName,
            workingDirectory,
            arguments,
            timeout,
            environmentVariables);

    private static bool IsOutputRelevantNoneItem(JsonElement item)
        => HasRelevantMetadata(item, "CopyToOutputDirectory")
           || HasRelevantMetadata(item, "CopyToPublishDirectory")
           || (item.TryGetProperty("Pack", out JsonElement pack) &&
               pack.ValueKind == JsonValueKind.String &&
               bool.TryParse(pack.GetString(), out bool packs) && packs);

    private static bool HasRelevantMetadata(JsonElement item, string name)
        => item.TryGetProperty(name, out JsonElement value)
           && value.ValueKind == JsonValueKind.String
           && !string.IsNullOrWhiteSpace(value.GetString())
           && !value.GetString()!.Equals("Never", StringComparison.OrdinalIgnoreCase);

    private static bool TryReadEvaluatedProjectReference(
        JsonElement item,
        out EvaluatedProjectReference? reference)
    {
        reference = null;
        if (!item.TryGetProperty("FullPath", out JsonElement fullPathElement) ||
            fullPathElement.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(fullPathElement.GetString()))
        {
            return false;
        }

        string? targetFramework = ReadItemText(item, "NearestTargetFramework");
        if (string.IsNullOrWhiteSpace(targetFramework))
        {
            string? setTargetFramework = ReadItemText(item, "SetTargetFramework");
            const string prefix = "TargetFramework=";
            if (!string.IsNullOrWhiteSpace(setTargetFramework) &&
                setTargetFramework!.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
            {
                targetFramework = setTargetFramework.Substring(prefix.Length).Trim();
            }
        }

        reference = new EvaluatedProjectReference(
            Path.GetFullPath(fullPathElement.GetString()!),
            string.IsNullOrWhiteSpace(targetFramework) ? null : targetFramework);
        return true;
    }

    private static bool TryReadResolvedProjectReferences(
        ProjectEvaluationRequest request,
        out EvaluatedProjectReference[] references)
    {
        references = Array.Empty<EvaluatedProjectReference>();
        var arguments = new List<string>
        {
            "msbuild",
            request.ProjectPath,
            "-nologo",
            "-verbosity:quiet",
            "-target:PrepareProjectReferences",
            "-getItem:_MSBuildProjectReferenceExistent",
            "-p:Configuration=" + request.Configuration,
            "-p:TargetFramework=" + request.TargetFramework
        };
        foreach (KeyValuePair<string, string> property in request.GlobalProperties.OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase))
        {
            if (property.Key.Equals("Configuration", StringComparison.OrdinalIgnoreCase) ||
                property.Key.Equals("TargetFramework", StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }
            arguments.Add("-p:" + property.Key + "=" + property.Value);
        }

        try
        {
            var process = RunBuildInputEvaluationProcess(
                "dotnet",
                Path.GetDirectoryName(request.ProjectPath)!,
                arguments,
                request.EnvironmentVariables,
                TimeSpan.FromMinutes(2));
            if (process.ExitCode != 0 || process.TimedOut)
                return false;
            int jsonStart = process.StdOut.IndexOf('{');
            int jsonEnd = process.StdOut.LastIndexOf('}');
            if (jsonStart < 0 || jsonEnd < jsonStart)
                return false;
            using JsonDocument document = JsonDocument.Parse(
                process.StdOut.Substring(jsonStart, jsonEnd - jsonStart + 1));
            if (!document.RootElement.TryGetProperty("Items", out JsonElement items) ||
                !items.TryGetProperty("_MSBuildProjectReferenceExistent", out JsonElement resolvedReferences) ||
                resolvedReferences.ValueKind != JsonValueKind.Array)
            {
                return false;
            }

            references = resolvedReferences.EnumerateArray()
                .Select(item => TryReadEvaluatedProjectReference(item, out EvaluatedProjectReference? reference)
                    ? reference
                    : null)
                .Where(static reference => reference is not null)
                .Cast<EvaluatedProjectReference>()
                .ToArray();
            // An empty resolved item list is a valid result for a conditional
            // ProjectReference that does not participate in this target framework.
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string? ReadItemText(JsonElement item, string name)
        => item.TryGetProperty(name, out JsonElement value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static void AddSemicolonSeparatedPaths(
        JsonElement properties,
        string name,
        string baseDirectory,
        HashSet<string> values)
    {
        if (!properties.TryGetProperty(name, out JsonElement property) || property.ValueKind != JsonValueKind.String)
            return;
        foreach (string value in (property.GetString() ?? string.Empty).Split(
                     new[] { ';' },
                     StringSplitOptions.RemoveEmptyEntries))
        {
            string fullPath = Path.GetFullPath(
                Path.IsPathRooted(value) ? value : Path.Combine(baseDirectory, value));
            if (File.Exists(fullPath))
                values.Add(fullPath);
        }
    }

    private static void AddSemicolonSeparatedValues(
        JsonElement properties,
        string name,
        HashSet<string> values)
    {
        if (!properties.TryGetProperty(name, out JsonElement property) || property.ValueKind != JsonValueKind.String)
            return;
        foreach (string value in (property.GetString() ?? string.Empty).Split(
                     new[] { ';' },
                     StringSplitOptions.RemoveEmptyEntries))
        {
            values.Add(value.Trim());
        }
    }

    private sealed class ProjectEvaluationRequest
    {
        internal ProjectEvaluationRequest(
            string projectPath,
            string? targetFramework,
            string configuration,
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
        internal string Configuration { get; }
        internal IReadOnlyDictionary<string, string> GlobalProperties { get; }
        internal IReadOnlyDictionary<string, string?> EnvironmentVariables { get; }

        internal ProjectEvaluationRequest ForProject(string projectPath, string? targetFramework)
            => new(
                Path.GetFullPath(projectPath),
                targetFramework,
                Configuration,
                GlobalProperties,
                EnvironmentVariables);

        internal string BuildVisitKey()
            => string.Join(
                "|",
                new[] { ProjectPath, TargetFramework ?? string.Empty, Configuration }
                    .Concat(GlobalProperties
                        .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                        .Select(entry => entry.Key + "=" + entry.Value))
                    .Concat(EnvironmentVariables
                        .OrderBy(entry => entry.Key, StringComparer.OrdinalIgnoreCase)
                        .Select(entry => entry.Key + "=" + entry.Value)));
    }

    private sealed class EvaluatedProjectInputs
    {
        internal EvaluatedProjectInputs(
            string[] buildInputs,
            EvaluatedProjectReference[] projectReferences,
            string[] targetFrameworks)
        {
            BuildInputs = buildInputs;
            ProjectReferences = projectReferences;
            TargetFrameworks = targetFrameworks;
        }

        internal string[] BuildInputs { get; }
        internal EvaluatedProjectReference[] ProjectReferences { get; }
        internal string[] TargetFrameworks { get; }
    }

    private sealed class EvaluatedProjectReference
    {
        internal EvaluatedProjectReference(string projectPath, string? targetFramework)
        {
            ProjectPath = projectPath;
            TargetFramework = targetFramework;
        }

        internal string ProjectPath { get; }
        internal string? TargetFramework { get; }
    }
}
