namespace PowerForge;

internal sealed partial class AppleReleaseSourceTrustService
{
    private ShippingSourceOwnership ResolveShippingSourceOwnership(
        string repositoryRoot,
        string projectDirectory,
        IReadOnlyDictionary<string, PbxObject> objects,
        string metadataPath,
        IReadOnlyCollection<string> generatedOutputPaths)
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

        var fileReferences = new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);
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
                var effectiveExtension = ResolveShippingSourceExtension(
                    repositoryRoot,
                    projectDirectory,
                    source,
                    buildFile,
                    metadataPath,
                    generatedOutputPaths);
                if (fileReferences.TryGetValue(fileReference, out var priorExtension) &&
                    !string.Equals(priorExtension, effectiveExtension, StringComparison.OrdinalIgnoreCase))
                {
                    throw new InvalidOperationException(
                        $"Shipping source '{fileReference}' is compiled with conflicting effective languages in {metadataPath}.");
                }
                fileReferences[fileReference] = effectiveExtension;
            }
        }

        return new ShippingSourceOwnership(fileReferences, synchronizedRoots);
    }

    private string? ResolveShippingSourceExtension(
        string repositoryRoot,
        string projectDirectory,
        PbxObject source,
        PbxObject buildFile,
        string metadataPath,
        IReadOnlyCollection<string> generatedOutputPaths)
    {
        string? language = null;
        var settings = ReadPbxDictionary(buildFile.Body, "settings");
        if (settings is not null)
        {
            var compilerFlags = ReadPbxAssignments(settings)
                .Where(static assignment => assignment.Key.Equals("COMPILER_FLAGS", StringComparison.OrdinalIgnoreCase))
                .Select(static assignment => assignment.Value)
                .ToArray();
            if (compilerFlags.Length > 1)
                throw new InvalidOperationException($"PBXBuildFile '{buildFile.Id}' repeats COMPILER_FLAGS: {metadataPath}");
            if (compilerFlags.Length == 1)
            {
                var tokens = ExpandCompilerResponseFileTokens(
                        repositoryRoot,
                        projectDirectory,
                        ExpandForwardedBuildFlagTokens(SplitBuildSettingPaths(compilerFlags[0]).ToArray(), "COMPILER_FLAGS"),
                        "COMPILER_FLAGS",
                        generatedOutputPaths,
                        $"PBXBuildFile '{buildFile.Id}' in {metadataPath}",
                        new HashSet<string>(GetPathComparer()))
                    .ToArray();
                TryReadCompilerLanguageOverride(tokens, out language);
            }
        }

        if (string.Equals(language, "none", StringComparison.OrdinalIgnoreCase))
            language = null;
        var mapped = language is null
            ? MapPbxSourceType(ReadPbxScalar(source.Body, "explicitFileType") ?? ReadPbxScalar(source.Body, "lastKnownFileType"))
            : MapCompilerLanguage(language);
        return mapped;
    }

    private static string? MapPbxSourceType(string? fileType)
        => fileType?.Trim() switch
        {
            "sourcecode.c.c" => ".c",
            "sourcecode.c.objc" => ".m",
            "sourcecode.cpp.cpp" => ".cpp",
            "sourcecode.cpp.objcpp" => ".mm",
            "sourcecode.asm" => ".s",
            "sourcecode.metal" => ".metal",
            "sourcecode.swift" => ".swift",
            _ => null
        };

    private static string MapCompilerLanguage(string language)
        => language.Trim().ToLowerInvariant() switch
        {
            "c" or "c-header" => ".c",
            "objective-c" or "objective-c-header" => ".m",
            "c++" or "c++-header" => ".cpp",
            "objective-c++" or "objective-c++-header" => ".mm",
            "assembler" or "assembler-with-cpp" => ".s",
            "metal" => ".metal",
            _ => throw new InvalidOperationException(
                $"PBX per-file compiler language '{language}' is not supported by exact-source Apple validation.")
        };

    private static bool IsTestProductType(string productType)
        => productType.Equals("com.apple.product-type.bundle.unit-test", StringComparison.OrdinalIgnoreCase) ||
           productType.Equals("com.apple.product-type.bundle.ui-testing", StringComparison.OrdinalIgnoreCase);

    private sealed class ShippingSourceOwnership
    {
        internal ShippingSourceOwnership(
            IReadOnlyDictionary<string, string?> fileReferences,
            ISet<string> synchronizedRoots)
        {
            FileReferences = fileReferences;
            SynchronizedRoots = synchronizedRoots;
        }

        internal IReadOnlyDictionary<string, string?> FileReferences { get; }

        internal ISet<string> SynchronizedRoots { get; }

        internal string? ResolveEffectiveExtension(
            string fileReference,
            string sourcePath,
            PbxObject source,
            string metadataPath)
        {
            if (!FileReferences.TryGetValue(fileReference, out var configuredExtension))
                return null;
            if (!string.IsNullOrWhiteSpace(configuredExtension))
                return configuredExtension;
            var extension = Path.GetExtension(sourcePath);
            if (SourceIncludeExtensions.Contains(extension))
                return extension;
            throw new InvalidOperationException(
                $"Shipping source '{source.Path ?? sourcePath}' has no exact compiler language in PBX metadata or per-file flags: {metadataPath}");
        }
    }
}
