namespace PowerForge;

/// <summary>
/// Planning helper methods for <see cref="ModulePipelineRunner"/>.
/// </summary>
public sealed partial class ModulePipelineRunner
{
    /// <summary>
    /// Layers the ReadyToRun values set by configuration segments over the build spec's ReadyToRun options.
    /// Returns null when neither sets anything, so modules that do not opt in build exactly as before.
    /// </summary>
    internal static ModuleReadyToRunOptions? ResolveReadyToRun(ModuleReadyToRunOptions? fromSpec, ReadyToRunSegmentValues? fromSegments)
    {
        if (fromSpec is null && fromSegments is null)
            return null;

        return new ModuleReadyToRunOptions
        {
            Enabled = fromSegments?.Enabled ?? fromSpec?.Enabled ?? false,
            Framework = fromSegments?.Framework ?? fromSpec?.Framework ?? ModuleReadyToRunOptions.DefaultFramework,
            RuntimeIdentifiers = fromSegments?.RuntimeIdentifiers ?? fromSpec?.RuntimeIdentifiers ??
                                 (string[])ModuleReadyToRunOptions.DefaultRuntimeIdentifiers.Clone(),
            Scope = fromSegments?.Scope ?? fromSpec?.Scope ?? ModuleReadyToRunScope.ModuleAssemblies
        };
    }

    /// <summary>ReadyToRun values set by <c>BuildLibraries</c> segments; a later segment overrides only what it sets.</summary>
    internal sealed class ReadyToRunSegmentValues
    {
        internal bool? Enabled { get; private set; }
        internal string? Framework { get; private set; }
        internal string[]? RuntimeIdentifiers { get; private set; }
        internal ModuleReadyToRunScope? Scope { get; private set; }

        internal static ReadyToRunSegmentValues? Merge(ReadyToRunSegmentValues? current, BuildLibrariesConfiguration segment)
        {
            if (!segment.ReadyToRun.HasValue && string.IsNullOrWhiteSpace(segment.ReadyToRunFramework) &&
                segment.ReadyToRunRuntimeIdentifiers is not { Length: > 0 } && !segment.ReadyToRunScope.HasValue)
                return current;

            var merged = current ?? new ReadyToRunSegmentValues();
            if (segment.ReadyToRun.HasValue) merged.Enabled = segment.ReadyToRun.Value;
            if (!string.IsNullOrWhiteSpace(segment.ReadyToRunFramework)) merged.Framework = segment.ReadyToRunFramework!.Trim();
            if (segment.ReadyToRunRuntimeIdentifiers is { Length: > 0 }) merged.RuntimeIdentifiers = segment.ReadyToRunRuntimeIdentifiers;
            if (segment.ReadyToRunScope.HasValue) merged.Scope = segment.ReadyToRunScope.Value;
            return merged;
        }
    }

    private static string[] BuildMissingCsprojReasonList(
        ModulePipelineSpec spec,
        bool syncNETProjectVersion,
        string[]? dotnetFrameworksFromSegments,
        string[]? exportAssembliesFromSegments,
        string[]? excludeLibraryFilterFromSegments,
        bool? doNotCopyLibrariesRecursivelyFromSegments,
        bool? handleRuntimesFromSegments,
        Dictionary<string, string> desktopDependencyAssetFrameworks,
        bool useAssemblyLoadContextRequested,
        bool typeAcceleratorsRequireAlc,
        string? resolveBinaryConflictsProjectName,
        bool binaryModuleDocumentationRequested,
        ModuleDevelopmentBinaryMode developmentBinariesMode,
        string? developmentBinariesPath,
        bool readyToRunRequested = false)
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
        var developmentBinariesNeedProject =
            developmentBinariesMode != ModuleDevelopmentBinaryMode.Off &&
            string.IsNullOrWhiteSpace(developmentBinariesPath);
        var hasExplicitBinaryIntentBeyondFramework =
            syncNETProjectVersion
            || hasBinaryModules
            || !string.IsNullOrWhiteSpace(resolveBinaryConflictsProjectName)
            || HasAnyConfiguredValues(excludeLibraryFilterFromSegments)
            || HasAnyConfiguredValues(spec.Build.ExcludeLibraryFilter)
            || effectiveDoNotCopyLibrariesRecursively
            || effectiveHandleRuntimes
            || desktopDependencyAssetFrameworks.Count > 0
            || useAssemblyLoadContextRequested
            || typeAcceleratorsRequireAlc
            || binaryModuleDocumentationRequested
            || developmentBinariesNeedProject
            || readyToRunRequested;

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

        if (desktopDependencyAssetFrameworks.Count > 0)
            reasons.Add("NETDesktopDependencyAssetFrameworks");

        if (useAssemblyLoadContextRequested)
            reasons.Add("UseAssemblyLoadContext");

        if (typeAcceleratorsRequireAlc)
            reasons.Add("NETAssemblyTypeAccelerators");

        if (binaryModuleDocumentationRequested)
            reasons.Add("NETBinaryModuleDocumentation");

        if (developmentBinariesNeedProject)
            reasons.Add("NETDevelopmentBinaries");

        if (readyToRunRequested)
            reasons.Add("NETReadyToRun");

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
