using System.Text;
using System.Text.Json;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private static string[] ReadProjectReferencePropertyNames(params string?[] values)
        => values
            .SelectMany(value => (value ?? string.Empty).Split(
                new[] { ';' },
                StringSplitOptions.RemoveEmptyEntries))
            .Select(value => value.Trim())
            .Where(value => value.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

    private static string BuildEvaluatedProjectReferenceKey(EvaluatedProjectReference reference)
    {
        var key = new StringBuilder();
        AppendProjectReferenceKeySegment(key, "ProjectPath");
        AppendProjectReferenceKeySegment(key, NormalizeProjectReferenceIdentityPath(reference.ProjectPath));
        AppendProjectReferenceKeySegment(key, "TargetFramework");
        AppendProjectReferenceKeySegment(key, reference.TargetFramework is null ? "Undefined" : "Defined");
        AppendProjectReferenceKeySegment(key, reference.TargetFramework ?? string.Empty);
        foreach (KeyValuePair<string, string> property in reference.GlobalProperties.OrderBy(
                     entry => entry.Key,
                     StringComparer.OrdinalIgnoreCase))
        {
            AppendProjectReferenceKeySegment(key, "Property");
            AppendProjectReferenceKeySegment(key, NormalizeMsBuildPropertyIdentityName(property.Key));
            AppendProjectReferenceKeySegment(key, property.Value);
        }
        foreach (string propertyName in reference.UndefineProperties.OrderBy(
                     value => value,
                     StringComparer.OrdinalIgnoreCase))
        {
            AppendProjectReferenceKeySegment(key, "Undefine");
            AppendProjectReferenceKeySegment(key, NormalizeMsBuildPropertyIdentityName(propertyName));
        }
        return key.ToString();
    }

    private static bool TryReadEvaluatedProjectReferences(
        JsonElement item,
        string declaringProjectPath,
        IReadOnlyCollection<string> propertyDefinitionPaths,
        IReadOnlyCollection<string> taskWidePropertyRemovals,
        out EvaluatedProjectReference[] references)
        => TryReadEvaluatedProjectReferences(
            item,
            declaringProjectPath,
            "FullPath",
            propertyDefinitionPaths,
            taskWidePropertyRemovals,
            out references);

    private static bool TryReadEvaluatedProjectReferences(
        JsonElement item,
        string declaringProjectPath,
        string projectPathMetadataName,
        IReadOnlyCollection<string> propertyDefinitionPaths,
        IReadOnlyCollection<string> taskWidePropertyRemovals,
        out EvaluatedProjectReference[] references)
    {
        references = Array.Empty<EvaluatedProjectReference>();
        if (!item.TryGetProperty(projectPathMetadataName, out JsonElement fullPathElement) ||
            fullPathElement.ValueKind != JsonValueKind.String ||
            string.IsNullOrWhiteSpace(fullPathElement.GetString()))
        {
            return false;
        }

        var propertyContexts = new List<Dictionary<string, string>>
        {
            new(StringComparer.OrdinalIgnoreCase)
        };
        string? projectProperties = ReadItemText(item, "Properties");
        if (!string.IsNullOrWhiteSpace(projectProperties))
        {
            // MSBuild item-level Properties replaces the task-wide property table.
            if (!TryOverlayProjectReferenceProperties(
                    propertyContexts,
                    item,
                    declaringProjectPath,
                    projectPathMetadataName,
                    propertyDefinitionPaths,
                    "Properties",
                    projectProperties,
                    out propertyContexts))
            {
                return false;
            }
        }
        else
        {
            foreach (string metadataName in new[] { "SetConfiguration", "SetPlatform", "SetTargetFramework" })
            {
                if (!TryOverlayProjectReferenceProperties(
                        propertyContexts,
                        item,
                        declaringProjectPath,
                        projectPathMetadataName,
                        propertyDefinitionPaths,
                        metadataName,
                        ReadItemText(item, metadataName),
                        out propertyContexts))
                {
                    return false;
                }
            }
        }
        // Per-item AdditionalProperties overlays either the replacement or task-wide table.
        if (!TryOverlayProjectReferenceProperties(
                propertyContexts,
                item,
                declaringProjectPath,
                projectPathMetadataName,
                propertyDefinitionPaths,
                "AdditionalProperties",
                ReadItemText(item, "AdditionalProperties"),
                out propertyContexts))
        {
            return false;
        }

        string[] undefineProperties = ReadProjectReferencePropertyNames(
            ReadItemText(item, "UndefineProperties"),
            ReadItemText(item, "GlobalPropertiesToRemove"),
            string.Join(";", taskWidePropertyRemovals));

        string projectPath = Path.GetFullPath(fullPathElement.GetString()!);
        string? nearestTargetFramework = ReadItemText(item, "NearestTargetFramework");
        references = propertyContexts
            .Select(globalProperties => new EvaluatedProjectReference(
                projectPath,
                globalProperties.TryGetValue("TargetFramework", out string? propertyTargetFramework)
                    ? propertyTargetFramework
                    : nearestTargetFramework,
                globalProperties,
                undefineProperties))
            .GroupBy(BuildEvaluatedProjectReferenceKey, StringComparer.Ordinal)
            .Select(group => group.First())
            .ToArray();
        return references.Length > 0;
    }

    private static bool TryReadResolvedProjectReferences(
        JsonElement items,
        string declaringProjectPath,
        IReadOnlyCollection<string> propertyDefinitionPaths,
        IReadOnlyCollection<string> taskWidePropertyRemovals,
        out EvaluatedProjectReference[] references)
    {
        references = Array.Empty<EvaluatedProjectReference>();
        if (!items.TryGetProperty(
                "_MSBuildProjectReferenceExistent",
                out JsonElement resolvedReferences) ||
            resolvedReferences.ValueKind != JsonValueKind.Array)
        {
            return false;
        }

        var resolved = new Dictionary<string, EvaluatedProjectReference>(StringComparer.Ordinal);
        foreach (JsonElement item in resolvedReferences.EnumerateArray())
        {
            if (!TryReadEvaluatedProjectReferences(
                    item,
                    declaringProjectPath,
                    propertyDefinitionPaths,
                    taskWidePropertyRemovals,
                    out EvaluatedProjectReference[] itemReferences))
            {
                return false;
            }

            foreach (EvaluatedProjectReference itemReference in itemReferences)
                resolved[BuildEvaluatedProjectReferenceKey(itemReference)] = itemReference;
        }
        references = resolved.Values.ToArray();
        // An empty resolved item list is a valid result for a conditional
        // ProjectReference that does not participate in this target framework.
        return true;
    }

    private static void AppendProjectReferenceKeySegment(StringBuilder key, string value)
        => key.Append(value.Length).Append(':').Append(value);

    private static string NormalizeProjectReferenceIdentityPath(string path)
    {
        string fullPath = Path.GetFullPath(path);
        return IsWindows() ? fullPath.ToUpperInvariant() : fullPath;
    }

    private static string NormalizeMsBuildPropertyIdentityName(string name)
        => name.ToUpperInvariant();

    private static string NormalizeEnvironmentIdentityName(string name)
        => IsWindows() ? name.ToUpperInvariant() : name;

    private static HashSet<string> ReadProjectReferenceOutputKeys(
        JsonElement items,
        string outputItemType)
    {
        var keys = new HashSet<string>(StringComparer.Ordinal);
        if (!items.TryGetProperty("ProjectReference", out JsonElement projectReferences) ||
            projectReferences.ValueKind != JsonValueKind.Array)
        {
            return keys;
        }

        foreach (JsonElement projectReference in projectReferences.EnumerateArray())
        {
            if (!string.Equals(
                    ReadItemText(projectReference, "OutputItemType"),
                    outputItemType,
                    StringComparison.OrdinalIgnoreCase) ||
                !string.Equals(
                    ReadItemText(projectReference, "ReferenceOutputAssembly"),
                    "false",
                    StringComparison.OrdinalIgnoreCase))
            {
                continue;
            }

            if (TryBuildProjectReferenceOutputKey(
                    projectReference,
                    "FullPath",
                    out string? key))
            {
                keys.Add(key!);
            }
        }

        return keys;
    }

    private static bool TryBuildProjectReferenceOutputKey(
        JsonElement item,
        string projectPathMetadataName,
        out string? key)
    {
        key = null;
        string? projectPath = ReadItemText(item, projectPathMetadataName);
        if (string.IsNullOrWhiteSpace(projectPath))
            return false;

        try
        {
            var value = new StringBuilder();
            AppendProjectReferenceKeySegment(value, "ProjectPath");
            AppendProjectReferenceKeySegment(value, NormalizeProjectReferenceIdentityPath(projectPath!));
            AppendProjectReferenceKeySegment(value, "Properties");
            AppendProjectReferenceKeySegment(value, ReadItemText(item, "Properties") ?? string.Empty);
            AppendProjectReferenceKeySegment(value, "AdditionalProperties");
            AppendProjectReferenceKeySegment(value, ReadItemText(item, "AdditionalProperties") ?? string.Empty);
            AppendProjectReferenceKeySegment(value, "LogicalName");
            AppendProjectReferenceKeySegment(value, ReadItemText(item, "LogicalName") ?? string.Empty);
            key = value.ToString();
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsGeneratedProjectReferenceAssemblyReference(
        string itemName,
        JsonElement item)
        => (itemName.Equals("ReferencePath", StringComparison.Ordinal) ||
            itemName.Equals("ReferenceCopyLocalPaths", StringComparison.Ordinal)) &&
           string.Equals(
               ReadItemText(item, "ReferenceSourceTarget"),
               "ProjectReference",
               StringComparison.OrdinalIgnoreCase);

    private static bool TryReadGeneratedProjectReferenceOutputs(
        string itemName,
        string fullPath,
        JsonElement item,
        string? msBuildToolsPath,
        string? msBuildSdksPath,
        string declaringProjectPath,
        IReadOnlyCollection<string> propertyDefinitionPaths,
        HashSet<string> embeddedResourceProjectReferences,
        HashSet<string> analyzerProjectReferences,
        IReadOnlyCollection<string> taskWidePropertyRemovals,
        out GeneratedProjectReferenceOutput[] outputs)
    {
        outputs = Array.Empty<GeneratedProjectReferenceOutput>();
        bool resolvedFromProjectReference = string.Equals(
            ReadItemText(item, "ReferenceSourceTarget"),
            "ProjectReference",
            StringComparison.OrdinalIgnoreCase);
        string? outputItemType = null;
        HashSet<string>? declaredOutputs = null;
        if (itemName.Equals("EmbeddedResource", StringComparison.Ordinal) && resolvedFromProjectReference)
        {
            outputItemType = "EmbeddedResource";
            declaredOutputs = embeddedResourceProjectReferences;
        }
        else if (itemName.Equals("Analyzer", StringComparison.Ordinal))
        {
            outputItemType = "Analyzer";
            declaredOutputs = analyzerProjectReferences;
        }

        if (outputItemType is null ||
            !IsDeclaredProjectReferenceOutput(
                item,
                outputItemType,
                msBuildToolsPath,
                msBuildSdksPath,
                declaredOutputs!) ||
            !TryReadEvaluatedProjectReferences(
                item,
                declaringProjectPath,
                "MSBuildSourceProjectFile",
                propertyDefinitionPaths,
                taskWidePropertyRemovals,
                out EvaluatedProjectReference[] projectReferences) ||
            projectReferences.Length == 0)
        {
            return false;
        }

        // An analyzer ProjectReference resolves to its compiled output even when project builds are
        // disabled. MSBuild stamps that direct target output with its source project but does not
        // guarantee ReferenceSourceTarget metadata. Other output item types can intentionally return
        // tracked source inputs and must remain in provenance.
        outputs = projectReferences
            .Select(projectReference => new GeneratedProjectReferenceOutput(fullPath, projectReference))
            .ToArray();
        return true;
    }

    private static bool IsDeclaredProjectReferenceOutput(
        JsonElement item,
        string outputItemType,
        string? msBuildToolsPath,
        string? msBuildSdksPath,
        HashSet<string> declaredProjectReferenceOutputs)
    {
        return string.Equals(
                   ReadItemText(item, "OutputItemType"),
                   outputItemType,
                   StringComparison.OrdinalIgnoreCase) &&
               string.Equals(
                   ReadItemText(item, "ReferenceOutputAssembly"),
                   "false",
                   StringComparison.OrdinalIgnoreCase) &&
               IsTrustedMsBuildProjectReferenceOutput(
                   item,
                   outputItemType,
                   msBuildToolsPath,
                   msBuildSdksPath) &&
               TryBuildProjectReferenceOutputKey(
                   item,
                   "MSBuildSourceProjectFile",
                   out string? key) &&
               declaredProjectReferenceOutputs.Contains(key!);
    }

    private static bool IsTrustedMsBuildProjectReferenceOutput(
        JsonElement item,
        string outputItemType,
        string? msBuildToolsPath,
        string? msBuildSdksPath)
    {
        string? definingProject = ReadItemText(item, "DefiningProjectFullPath");
        if (string.IsNullOrWhiteSpace(definingProject))
            return false;

        return IsTrustedMsBuildProjectReferenceTargetPath(
            definingProject!,
            outputItemType,
            msBuildToolsPath,
            msBuildSdksPath);
    }

    /// <summary>
    /// Confirms that generated project-reference metadata originates from an exact MSBuild-owned
    /// target path, including SDK installations selected through the evaluated MSBuild SDK path.
    /// </summary>
    internal static bool IsTrustedMsBuildProjectReferenceTargetPath(
        string definingProject,
        string outputItemType,
        string? msBuildToolsPath,
        string? msBuildSdksPath)
    {
        if (string.IsNullOrWhiteSpace(definingProject) || string.IsNullOrWhiteSpace(msBuildToolsPath))
            return false;

        try
        {
            string actualTarget = Path.GetFullPath(definingProject);
            string commonTarget = Path.GetFullPath(Path.Combine(
                msBuildToolsPath,
                "Microsoft.Common.CurrentVersion.targets"));
            StringComparison comparison = IsWindows()
                ? StringComparison.OrdinalIgnoreCase
                : StringComparison.Ordinal;
            if (string.Equals(actualTarget, commonTarget, comparison))
                return true;

            if (!outputItemType.Equals("Analyzer", StringComparison.OrdinalIgnoreCase))
                return false;

            string effectiveSdksPath = string.IsNullOrWhiteSpace(msBuildSdksPath)
                ? Path.Combine(msBuildToolsPath, "Sdks")
                : msBuildSdksPath!;
            string analyzerConflictTarget = Path.GetFullPath(Path.Combine(
                effectiveSdksPath,
                "Microsoft.NET.Sdk",
                "targets",
                "Microsoft.NET.ConflictResolution.targets"));
            return string.Equals(
                actualTarget,
                analyzerConflictTarget,
                comparison);
        }
        catch
        {
            return false;
        }
    }
}
