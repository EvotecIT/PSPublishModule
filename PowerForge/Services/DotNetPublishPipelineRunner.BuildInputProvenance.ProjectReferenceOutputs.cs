using System.Text.Json;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private static HashSet<string> ReadProjectReferenceOutputKeys(
        JsonElement items,
        string outputItemType)
    {
        var keys = new HashSet<string>(
            IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
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
            key = string.Join(
                "\n",
                Path.GetFullPath(projectPath),
                ReadItemText(item, "AdditionalProperties") ?? string.Empty,
                ReadItemText(item, "LogicalName") ?? string.Empty);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static bool IsGeneratedProjectReferenceOutput(
        string itemName,
        string fullPath,
        JsonElement item,
        string? msBuildToolsPath,
        HashSet<string> embeddedResourceProjectReferences,
        HashSet<string> analyzerProjectReferences)
    {
        bool resolvedFromProjectReference = string.Equals(
            ReadItemText(item, "ReferenceSourceTarget"),
            "ProjectReference",
            StringComparison.OrdinalIgnoreCase);
        if (itemName.Equals("ReferencePath", StringComparison.Ordinal) ||
            itemName.Equals("ReferenceCopyLocalPaths", StringComparison.Ordinal))
        {
            return resolvedFromProjectReference;
        }

        if (itemName.Equals("EmbeddedResource", StringComparison.Ordinal) &&
            Path.GetExtension(fullPath).Equals(".dll", StringComparison.OrdinalIgnoreCase))
        {
            return resolvedFromProjectReference &&
                   IsDeclaredProjectReferenceOutput(
                       item,
                       "EmbeddedResource",
                       msBuildToolsPath,
                       embeddedResourceProjectReferences);
        }

        // An analyzer ProjectReference resolves to its compiled DLL even when project builds are
        // disabled. MSBuild stamps that direct target output with its source project but does not
        // guarantee ReferenceSourceTarget metadata. Other output item types can intentionally return
        // tracked source inputs and must remain in provenance.
        return itemName.Equals("Analyzer", StringComparison.Ordinal) &&
               Path.GetExtension(fullPath).Equals(".dll", StringComparison.OrdinalIgnoreCase) &&
               IsDeclaredProjectReferenceOutput(
                   item,
                   "Analyzer",
                   msBuildToolsPath,
                   analyzerProjectReferences);
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
