using System.Text.Json;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private static bool TryProcessControlledProjectReferenceItems(
        JsonElement items,
        string projectPath,
        string? msBuildToolsPath,
        string? msBuildSdksPath,
        IReadOnlyCollection<string> propertyDefinitionPaths,
        IReadOnlyList<PreprocessedProjectReferenceDeclaration> projectReferenceDeclarations,
        IReadOnlyDictionary<string, string> evaluatedConditionProperties,
        IReadOnlyCollection<string> taskWidePropertyRemovals,
        IEnumerable<EvaluatedProjectReference> knownProjectReferences,
        HashSet<string> inputs,
        HashSet<string> sourceInputs,
        HashSet<string> generatedBuildRoots,
        VerifiedPackageInputCatalog? verifiedPackages,
        IReadOnlyCollection<string> trustedBuildInfrastructureRoots,
        List<GeneratedProjectReferenceOutput> generatedProjectReferenceOutputs)
    {
        HashSet<string> embeddedResourceProjectReferences =
            ReadProjectReferenceOutputKeys(items, "EmbeddedResource");
        HashSet<string> analyzerProjectReferences =
            ReadProjectReferenceOutputKeys(items, "Analyzer");
        foreach (string itemName in EvaluatedBuildItemNames)
        {
            if (itemName.Equals("ProjectReference", StringComparison.Ordinal) ||
                !items.TryGetProperty(itemName, out JsonElement values) ||
                values.ValueKind != JsonValueKind.Array)
            {
                continue;
            }
            if (IsAmbientReferenceResolutionItem(itemName) && values.GetArrayLength() > 0)
                return false;

            foreach (JsonElement item in values.EnumerateArray())
            {
                if (itemName.Equals("Reference", StringComparison.Ordinal) &&
                    TryResolveEvaluatedItemPath(
                        item,
                        "HintPath",
                        Path.GetDirectoryName(projectPath)!,
                        out string? hintPath) &&
                    !IsBelowGeneratedBuildRoot(hintPath!, generatedBuildRoots))
                {
                    AddClassifiedEvaluatedInput(
                        hintPath!,
                        isSourceInput: true,
                        inputs,
                        sourceInputs,
                        generatedBuildRoots,
                        verifiedPackages,
                        trustedBuildInfrastructureRoots);
                }

                if (!item.TryGetProperty("FullPath", out JsonElement fullPathElement) ||
                    fullPathElement.ValueKind != JsonValueKind.String ||
                    string.IsNullOrWhiteSpace(fullPathElement.GetString()))
                {
                    continue;
                }
                string fullPath = Path.GetFullPath(fullPathElement.GetString()!);
                if (TryReadGeneratedProjectReferenceOutputs(
                        itemName,
                        fullPath,
                        item,
                        msBuildToolsPath,
                        msBuildSdksPath,
                        projectPath,
                        propertyDefinitionPaths,
                        projectReferenceDeclarations,
                        evaluatedConditionProperties,
                        embeddedResourceProjectReferences,
                        analyzerProjectReferences,
                        taskWidePropertyRemovals,
                        knownProjectReferences,
                        out GeneratedProjectReferenceOutput[] generatedOutputs))
                {
                    generatedProjectReferenceOutputs.AddRange(generatedOutputs);
                    continue;
                }
                if (IsBelowGeneratedBuildRoot(fullPath, generatedBuildRoots))
                    continue;
                if (itemName.Equals("EmbeddedResource", StringComparison.Ordinal) &&
                    TryResolveEvaluatedItemPath(
                        item,
                        "DependentUpon",
                        Path.GetDirectoryName(fullPath)!,
                        out string? dependentUponPath))
                {
                    AddClassifiedEvaluatedInput(
                        dependentUponPath!,
                        isSourceInput: true,
                        inputs,
                        sourceInputs,
                        generatedBuildRoots,
                        verifiedPackages,
                        trustedBuildInfrastructureRoots);
                }
                if (!itemName.Equals("None", StringComparison.Ordinal) ||
                    IsOutputRelevantNoneItem(item))
                {
                    bool isSourceInput = EvaluatedSourceItemNames.Contains(itemName) ||
                        (itemName.Equals("None", StringComparison.Ordinal) &&
                         IsOutputRelevantNoneItem(item));
                    AddClassifiedEvaluatedInput(
                        fullPath,
                        isSourceInput,
                        inputs,
                        sourceInputs,
                        generatedBuildRoots,
                        verifiedPackages,
                        trustedBuildInfrastructureRoots);
                }
            }
        }
        return true;
    }
}
