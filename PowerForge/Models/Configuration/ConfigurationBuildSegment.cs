namespace PowerForge;

/// <summary>
/// Configuration segment that describes legacy build settings.
/// </summary>
public sealed class ConfigurationBuildSegment : IConfigurationSegment
{
    /// <inheritdoc />
    public string Type => "Build";

    /// <summary>
    /// BuildModule configuration payload.
    /// </summary>
    public BuildModuleConfiguration BuildModule { get; set; } = new();
}

/// <summary>
/// BuildModule configuration payload for <see cref="ConfigurationBuildSegment"/>.
/// </summary>
public sealed class BuildModuleConfiguration
{
    /// <summary>Enable build process.</summary>
    public bool? Enable { get; set; }

    /// <summary>Delete target module before build (legacy key: DeleteBefore).</summary>
    public bool? DeleteBefore { get; set; }

    /// <summary>Merge module on build (legacy key: Merge).</summary>
    public bool? Merge { get; set; }

    /// <summary>When merging, include functions from approved modules (legacy key: MergeMissing).</summary>
    public bool? MergeMissing { get; set; }

    /// <summary>Sign merged output (legacy key: SignMerged).</summary>
    public bool? SignMerged { get; set; }

    /// <summary>Keep classes in a separate dot-sourced file (legacy key: ClassesDotSource).</summary>
    public bool? ClassesDotSource { get; set; }

    /// <summary>Keep library-loading code in a separate dot-sourced file (legacy key: LibraryDotSource).</summary>
    public bool? LibraryDotSource { get; set; }

    /// <summary>Write library-loading code into a separate file (legacy key: LibrarySeparateFile).</summary>
    public bool? LibrarySeparateFile { get; set; }

    /// <summary>Only regenerate PSD1 without rebuilding/merging other artefacts (legacy key: RefreshPSD1Only).</summary>
    public bool? RefreshPSD1Only { get; set; }

    /// <summary>Export all functions/aliases via wildcard in PSD1 (legacy key: UseWildcardForFunctions).</summary>
    public bool? UseWildcardForFunctions { get; set; }

    /// <summary>Use local versioning (legacy key: LocalVersion).</summary>
    public bool? LocalVersion { get; set; }

    /// <summary>Installation strategy for versioned installs.</summary>
    public InstallationStrategy? VersionedInstallStrategy { get; set; }

    /// <summary>How many versions to keep per module when using versioned installs.</summary>
    public int? VersionedInstallKeep { get; set; }

    /// <summary>
    /// Controls how legacy flat installs under &lt;root&gt;\&lt;ModuleName&gt; are handled during install.
    /// </summary>
    public LegacyFlatModuleHandling? LegacyFlatHandling { get; set; }

    /// <summary>
    /// Version folder names to preserve during pruning (for example older major versions).
    /// </summary>
    public string[]? PreserveInstallVersions { get; set; }

    /// <summary>
    /// When true, installs missing module dependencies (Required/External modules) before running the build.
    /// </summary>
    public bool? InstallMissingModules { get; set; }

    /// <summary>
    /// When true, forces re-install even if the dependency is already present.
    /// </summary>
    public bool? InstallMissingModulesForce { get; set; }

    /// <summary>
    /// When true, allows prerelease versions when installing dependencies.
    /// </summary>
    public bool? InstallMissingModulesPrerelease { get; set; }

    /// <summary>
    /// When true, resolves Auto/Latest dependency versions from the repository without installing.
    /// </summary>
    public bool? ResolveMissingModulesOnline { get; set; }

    /// <summary>
    /// When true, warns if RequiredModules are older than the latest available in the repository.
    /// </summary>
    public bool? WarnIfRequiredModulesOutdated { get; set; }

    /// <summary>
    /// Repository name to use when installing missing dependencies (defaults to PSGallery).
    /// </summary>
    public string? InstallMissingModulesRepository { get; set; }

    /// <summary>
    /// Optional credentials used when installing missing dependencies.
    /// </summary>
    public RepositoryCredential? InstallMissingModulesCredential { get; set; }

    /// <summary>Do not attempt to fix relative paths during merge.</summary>
    public bool? DoNotAttemptToFixRelativePaths { get; set; }

    /// <summary>Enable legacy debug DLL merge behavior (legacy key: DebugDLL).</summary>
    public bool? DebugDLL { get; set; }

    /// <summary>Kill locking processes before install.</summary>
    public bool? KillLockersBeforeInstall { get; set; }

    /// <summary>Force killing locking processes before install.</summary>
    public bool? KillLockersForce { get; set; }

    /// <summary>Auto switch VersionedInstallStrategy to Exact when publishing.</summary>
    public bool? AutoSwitchExactOnPublish { get; set; }

    /// <summary>Binary conflict resolution settings.</summary>
    public ResolveBinaryConflictsConfiguration? ResolveBinaryConflicts { get; set; }
}

/// <summary>
/// Configuration for resolving binary conflicts when merging libraries.
/// </summary>
public sealed class ResolveBinaryConflictsConfiguration
{
    /// <summary>Enable/disable conflict resolution.</summary>
    public bool? Enabled { get; set; }

    /// <summary>Project name used when resolving conflicts.</summary>
    public string? ProjectName { get; set; }
}

