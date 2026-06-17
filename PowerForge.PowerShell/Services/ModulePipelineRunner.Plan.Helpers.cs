namespace PowerForge;

/// <summary>
/// Planning helper methods for <see cref="ModulePipelineRunner"/>.
/// </summary>
public sealed partial class ModulePipelineRunner
{
    private static string[] BuildMissingCsprojReasonList(
        ModulePipelineSpec spec,
        bool syncNETProjectVersion,
        string[]? dotnetFrameworksFromSegments,
        string[]? exportAssembliesFromSegments,
        string[]? excludeLibraryFilterFromSegments,
        bool? doNotCopyLibrariesRecursivelyFromSegments,
        bool? handleRuntimesFromSegments,
        bool useAssemblyLoadContextRequested,
        bool typeAcceleratorsRequireAlc,
        string? resolveBinaryConflictsProjectName,
        bool binaryModuleDocumentationRequested)
    {
        var reasons = new List<string>();
        var hasFrameworks = HasAnyConfiguredValues(dotnetFrameworksFromSegments)
                            || HasAnyConfiguredValues(spec.Build.Frameworks);
        var hasBinaryModules = HasAnyConfiguredValues(exportAssembliesFromSegments)
                               || HasAnyConfiguredValues(spec.Build.ExportAssemblies);
        var effectiveDoNotCopyLibrariesRecursively =
            doNotCopyLibrariesRecursivelyFromSegments ?? spec.Build.DoNotCopyLibrariesRecursively;
        var effectiveHandleRuntimes =
            handleRuntimesFromSegments ?? spec.Build.HandleRuntimes;
        var hasExplicitBinaryIntentBeyondFramework =
            syncNETProjectVersion
            || hasBinaryModules
            || !string.IsNullOrWhiteSpace(resolveBinaryConflictsProjectName)
            || HasAnyConfiguredValues(excludeLibraryFilterFromSegments)
            || HasAnyConfiguredValues(spec.Build.ExcludeLibraryFilter)
            || effectiveDoNotCopyLibrariesRecursively
            || effectiveHandleRuntimes
            || useAssemblyLoadContextRequested
            || typeAcceleratorsRequireAlc
            || binaryModuleDocumentationRequested;

        if (syncNETProjectVersion)
            reasons.Add("SyncNETProjectVersion");

        if (hasFrameworks && hasExplicitBinaryIntentBeyondFramework)
            reasons.Add("NETFramework");

        if (hasBinaryModules)
            reasons.Add("NETBinaryModule");

        if (!string.IsNullOrWhiteSpace(resolveBinaryConflictsProjectName))
            reasons.Add("ResolveBinaryConflictsName");

        if (HasAnyConfiguredValues(excludeLibraryFilterFromSegments) || HasAnyConfiguredValues(spec.Build.ExcludeLibraryFilter))
            reasons.Add("NETExcludeLibraryFilter");

        if (effectiveDoNotCopyLibrariesRecursively)
            reasons.Add("NETDoNotCopyLibrariesRecursively");

        if (effectiveHandleRuntimes)
            reasons.Add("NETHandleRuntimes");

        if (useAssemblyLoadContextRequested)
            reasons.Add("UseAssemblyLoadContext");

        if (typeAcceleratorsRequireAlc)
            reasons.Add("NETAssemblyTypeAccelerators");

        if (binaryModuleDocumentationRequested)
            reasons.Add("NETBinaryModuleDocumentation");

        return reasons
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool HasAnyConfiguredValues(string[]? values)
        => values is { Length: > 0 } &&
           values.Any(static value => !string.IsNullOrWhiteSpace(value));

    private static string[] NormalizeStringArray(string[]? values)
        => values is { Length: > 0 }
            ? values
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : Array.Empty<string>();
}
