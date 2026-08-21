using System.Text;
using System.Text.Json;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private static void AddProjectReferenceProperties(
        IDictionary<string, string> properties,
        string? assignments)
    {
        foreach (string assignment in (assignments ?? string.Empty).Split(
                     new[] { ';' },
                     StringSplitOptions.RemoveEmptyEntries))
        {
            int separator = assignment.IndexOf('=');
            if (separator <= 0)
                continue;
            string name = assignment.Substring(0, separator).Trim();
            if (name.Length == 0)
                continue;
            properties[name] = assignment.Substring(separator + 1).Trim();
        }
    }

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

    private static bool TryReadGeneratedProjectReferenceOutput(
        string itemName,
        string fullPath,
        JsonElement item,
        string? msBuildToolsPath,
        HashSet<string> embeddedResourceProjectReferences,
        HashSet<string> analyzerProjectReferences,
        IReadOnlyCollection<string> taskWidePropertyRemovals,
        out GeneratedProjectReferenceOutput? output)
    {
        output = null;
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
                declaredOutputs!) ||
            !TryReadEvaluatedProjectReference(
                item,
                "MSBuildSourceProjectFile",
                taskWidePropertyRemovals,
                out EvaluatedProjectReference? projectReference) ||
            projectReference is null)
        {
            return false;
        }

        // An analyzer ProjectReference resolves to its compiled output even when project builds are
        // disabled. MSBuild stamps that direct target output with its source project but does not
        // guarantee ReferenceSourceTarget metadata. Other output item types can intentionally return
        // tracked source inputs and must remain in provenance.
        output = new GeneratedProjectReferenceOutput(fullPath, projectReference);
        return true;
    }

    private static bool IsDeclaredProjectReferenceOutput(
        JsonElement item,
        string outputItemType,
        string? msBuildToolsPath,
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
               IsTrustedMsBuildProjectReferenceOutput(item, outputItemType, msBuildToolsPath) &&
               TryBuildProjectReferenceOutputKey(
                   item,
                   "MSBuildSourceProjectFile",
                   out string? key) &&
               declaredProjectReferenceOutputs.Contains(key!);
    }

    private static bool IsTrustedMsBuildProjectReferenceOutput(
        JsonElement item,
        string outputItemType,
        string? msBuildToolsPath)
    {
        string? definingProject = ReadItemText(item, "DefiningProjectFullPath");
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

            string analyzerConflictTarget = Path.GetFullPath(Path.Combine(
                msBuildToolsPath,
                "Sdks",
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
