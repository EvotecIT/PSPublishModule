namespace PowerForge;

internal sealed partial class AppleReleaseSourceTrustService
{
    private static ShippingSourceOwnership ResolveShippingSourceOwnership(
        IReadOnlyDictionary<string, PbxObject> objects,
        string metadataPath)
    {
        var sourcePhaseIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var synchronizedRoots = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var target in objects.Values.Where(static value =>
                     value.Isa.Equals("PBXNativeTarget", StringComparison.OrdinalIgnoreCase)))
        {
            var productType = ReadPbxScalar(target.Body, "productType")?.Trim();
            if (string.IsNullOrWhiteSpace(productType))
                throw new InvalidOperationException($"PBX native target '{target.Id}' is missing productType: {metadataPath}");
            if (IsTestProductType(productType!))
                continue;

            foreach (var phaseId in ReadPbxReferences(target.Body, "buildPhases"))
            {
                if (!objects.TryGetValue(phaseId, out var phase))
                    throw new InvalidOperationException($"PBX native target '{target.Id}' references unknown build phase '{phaseId}': {metadataPath}");
                if (phase.Isa.Equals("PBXSourcesBuildPhase", StringComparison.OrdinalIgnoreCase))
                    sourcePhaseIds.Add(phaseId);
            }
            foreach (var rootId in ReadPbxReferences(target.Body, "fileSystemSynchronizedGroups"))
            {
                if (!objects.TryGetValue(rootId, out var root) ||
                    !root.Isa.Equals("PBXFileSystemSynchronizedRootGroup", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"PBX native target '{target.Id}' references invalid synchronized source root '{rootId}': {metadataPath}");
                }
                synchronizedRoots.Add(rootId);
            }
        }

        var fileReferences = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var phaseId in sourcePhaseIds)
        {
            var phase = objects[phaseId];
            foreach (var buildFileId in ReadPbxReferences(phase.Body, "files"))
            {
                if (!objects.TryGetValue(buildFileId, out var buildFile) ||
                    !buildFile.Isa.Equals("PBXBuildFile", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"PBX sources phase '{phaseId}' references invalid build file '{buildFileId}': {metadataPath}");
                }

                var fileReferenceValue = ReadPbxScalar(buildFile.Body, "fileRef")?.Trim();
                if (string.IsNullOrWhiteSpace(fileReferenceValue))
                {
                    throw new InvalidOperationException($"PBX sources build file '{buildFileId}' is missing fileRef: {metadataPath}");
                }
                var fileReference = ParsePbxObjectIdentifier(
                    fileReferenceValue!,
                    $"source file reference in build file {buildFileId}");
                if (!objects.TryGetValue(fileReference, out var source) ||
                    !source.Isa.Equals("PBXFileReference", StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException($"PBX sources build file '{buildFileId}' references invalid source file '{fileReference}': {metadataPath}");
                }
                fileReferences.Add(fileReference);
            }
        }

        return new ShippingSourceOwnership(fileReferences, synchronizedRoots);
    }

    private static bool IsTestProductType(string productType)
        => productType.Equals("com.apple.product-type.bundle.unit-test", StringComparison.OrdinalIgnoreCase) ||
           productType.Equals("com.apple.product-type.bundle.ui-testing", StringComparison.OrdinalIgnoreCase);

    private sealed class ShippingSourceOwnership
    {
        internal ShippingSourceOwnership(ISet<string> fileReferences, ISet<string> synchronizedRoots)
        {
            FileReferences = fileReferences;
            SynchronizedRoots = synchronizedRoots;
        }

        internal ISet<string> FileReferences { get; }

        internal ISet<string> SynchronizedRoots { get; }
    }
}
