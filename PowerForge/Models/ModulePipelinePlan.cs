using System.Collections.Generic;

namespace PowerForge;

/// <summary>
/// Planned execution details computed from a <see cref="ModulePipelineSpec"/> and its configuration segments.
/// </summary>
public sealed class ModulePipelinePlan
{
    /// <summary>
    /// Module name resolved for the pipeline.
    /// </summary>
    public string ModuleName { get; }

    /// <summary>
    /// Source/project root directory used for staging.
    /// </summary>
    public string ProjectRoot { get; }

    /// <summary>
    /// Version input provided by configuration (can be an X-pattern like <c>2.0.X</c>).
    /// </summary>
    public string ExpectedVersion { get; }

    /// <summary>
    /// Resolved version after stepping.
    /// </summary>
    public string ResolvedVersion { get; }

    /// <summary>
    /// Optional prerelease tag (used for token replacements).
    /// </summary>
    public string? PreRelease { get; }

    /// <summary>
    /// Final build spec used for <see cref="ModuleBuildPipeline.BuildToStaging"/>.
    /// </summary>
    public ModuleBuildSpec BuildSpec { get; }

    /// <summary>
    /// Compatible PowerShell editions requested by configuration (used for manifest patching and install root derivation).
    /// </summary>
    public string[] CompatiblePSEditions { get; }

    /// <summary>
    /// Required module entries requested by configuration (used for manifest patching).
    /// </summary>
    public ManifestEditor.RequiredModule[] RequiredModules { get; }

    /// <summary>
    /// External module dependencies (PSData.ExternalModuleDependencies).
    /// </summary>
    public string[] ExternalModuleDependencies { get; }

    /// <summary>
    /// Required modules that should be packaged into artefacts when enabled (excludes ExternalModule dependencies).
    /// </summary>
    public ManifestEditor.RequiredModule[] RequiredModulesForPackaging { get; }

    /// <summary>
    /// Optional information configuration (include/exclude patterns) used for artefact packaging.
    /// </summary>
    public InformationConfiguration? Information { get; }

    /// <summary>
    /// Optional documentation configuration (docs folder + readme path).       
    /// </summary>
    public DocumentationConfiguration? Documentation { get; }

    /// <summary>
    /// Optional delivery metadata configuration (Internals bundle information and generated install/update commands).
    /// </summary>
    public DeliveryOptionsConfiguration? Delivery { get; }

    /// <summary>
    /// Optional documentation build configuration (enable/clean/tool).
    /// </summary>
    public BuildDocumentationConfiguration? DocumentationBuild { get; }

    /// <summary>
    /// Optional compatibility validation settings.
    /// </summary>
    public CompatibilitySettings? CompatibilitySettings { get; }

    /// <summary>
    /// Optional file consistency validation settings.
    /// </summary>
    public FileConsistencySettings? FileConsistencySettings { get; }

    /// <summary>
    /// Optional module validation settings.
    /// </summary>
    public ModuleValidationSettings? ValidationSettings { get; }

    /// <summary>
    /// Optional formatting configuration.
    /// </summary>
    public ConfigurationFormattingSegment? Formatting { get; }

    /// <summary>
    /// Optional import-modules configuration (self/required modules).
    /// </summary>
    public ImportModulesConfiguration? ImportModules { get; }

    /// <summary>
    /// Placeholder replacement entries applied to the merged PSM1.
    /// </summary>
    public PlaceHolderReplacement[] PlaceHolders { get; }

    /// <summary>
    /// Placeholder options (skip built-in replacements).
    /// </summary>
    public PlaceHolderOptionConfiguration? PlaceHolderOption { get; }

    /// <summary>
    /// Command module dependencies to write into the manifest.
    /// </summary>
    public IReadOnlyDictionary<string, string[]> CommandModuleDependencies { get; }

    /// <summary>
    /// TestsAfterMerge configurations to execute.
    /// </summary>
    public TestConfiguration[] TestsAfterMerge { get; }

    /// <summary>
    /// When true, module sources should be merged into a single PSM1 (legacy: BuildModule.Merge).
    /// </summary>
    public bool MergeModule { get; }

    /// <summary>
    /// When true, merge missing functions from approved modules into the PSM1 (legacy: BuildModule.MergeMissing).
    /// </summary>
    public bool MergeMissing { get; }

    /// <summary>
    /// Approved module names that can be used as function donors during merge.
    /// </summary>
    public string[] ApprovedModules { get; }

    /// <summary>
    /// Optional ModuleSkip configuration (ignored modules/functions when validating merge dependencies).
    /// </summary>
    public ModuleSkipConfiguration? ModuleSkip { get; }

    /// <summary>
    /// When true, code-signing is requested for the built module output (legacy: BuildModule.SignMerged).
    /// </summary>
    public bool SignModule { get; }

    /// <summary>
    /// Optional signing options (certificate selection and include/exclude patterns).
    /// </summary>
    public SigningOptionsConfiguration? Signing { get; }

    /// <summary>
    /// Publish configuration segments enabled for this pipeline run.
    /// </summary>
    public ConfigurationPublishSegment[] Publishes { get; }

    /// <summary>
    /// Artefact configuration segments enabled for this pipeline run.
    /// </summary>
    public ConfigurationArtefactSegment[] Artefacts { get; }

    /// <summary>
    /// When true, installs the module after building.
    /// </summary>
    public bool InstallEnabled { get; }

    /// <summary>
    /// Installation strategy used when installing.
    /// </summary>
    public InstallationStrategy InstallStrategy { get; }

    /// <summary>
    /// Number of versions to keep per module root when installing.
    /// </summary>
    public int InstallKeepVersions { get; }

    /// <summary>
    /// Destination module roots used for install. When empty, defaults are used by the installer.
    /// </summary>
    public string[] InstallRoots { get; }

    /// <summary>
    /// When true, installs missing module dependencies before running the build.
    /// </summary>
    public bool InstallMissingModules { get; }

    /// <summary>
    /// When true, forces dependency install even if already present.
    /// </summary>
    public bool InstallMissingModulesForce { get; }

    /// <summary>
    /// When true, allows prerelease versions during dependency installation.
    /// </summary>
    public bool InstallMissingModulesPrerelease { get; }

    /// <summary>
    /// Repository name to use when installing missing dependencies.
    /// </summary>
    public string? InstallMissingModulesRepository { get; }

    /// <summary>
    /// Credential used when installing missing dependencies.
    /// </summary>
    public RepositoryCredential? InstallMissingModulesCredential { get; }

    /// <summary>
    /// When true, the staging directory was generated by the pipeline (not explicitly provided).
    /// </summary>
    public bool StagingWasGenerated { get; }

    /// <summary>
    /// When true, the pipeline should delete the generated staging folder after a successful run.
    /// </summary>
    public bool DeleteGeneratedStagingAfterRun { get; }

    /// <summary>
    /// Creates a new plan instance.
    /// </summary>
    public ModulePipelinePlan(
        string moduleName,
        string projectRoot,
        string expectedVersion,
        string resolvedVersion,
        string? preRelease,
        ModuleBuildSpec buildSpec,
        string[] compatiblePSEditions,
        ManifestEditor.RequiredModule[] requiredModules,
        string[] externalModuleDependencies,
        ManifestEditor.RequiredModule[] requiredModulesForPackaging,
        InformationConfiguration? information,
        DocumentationConfiguration? documentation,
        DeliveryOptionsConfiguration? delivery,
        BuildDocumentationConfiguration? documentationBuild,
        CompatibilitySettings? compatibilitySettings,
        FileConsistencySettings? fileConsistencySettings,
        ModuleValidationSettings? validationSettings,
        ConfigurationFormattingSegment? formatting,
        ImportModulesConfiguration? importModules,
        PlaceHolderReplacement[] placeHolders,
        PlaceHolderOptionConfiguration? placeHolderOption,
        IReadOnlyDictionary<string, string[]> commandModuleDependencies,
        TestConfiguration[] testsAfterMerge,
        bool mergeModule,
        bool mergeMissing,
        string[] approvedModules,
        ModuleSkipConfiguration? moduleSkip,
        bool signModule,
        SigningOptionsConfiguration? signing,
        ConfigurationPublishSegment[] publishes,
        ConfigurationArtefactSegment[] artefacts,
        bool installEnabled,
        InstallationStrategy installStrategy,
        int installKeepVersions,
        string[] installRoots,
        bool installMissingModules,
        bool installMissingModulesForce,
        bool installMissingModulesPrerelease,
        string? installMissingModulesRepository,
        RepositoryCredential? installMissingModulesCredential,
        bool stagingWasGenerated,
        bool deleteGeneratedStagingAfterRun)
    {
        ModuleName = moduleName;
        ProjectRoot = projectRoot;
        ExpectedVersion = expectedVersion;
        ResolvedVersion = resolvedVersion;
        PreRelease = preRelease;
        BuildSpec = buildSpec;
        CompatiblePSEditions = compatiblePSEditions;
        RequiredModules = requiredModules;
        ExternalModuleDependencies = externalModuleDependencies ?? Array.Empty<string>();
        RequiredModulesForPackaging = requiredModulesForPackaging;
        Information = information;
        Documentation = documentation;
        Delivery = delivery;
        DocumentationBuild = documentationBuild;
        CompatibilitySettings = compatibilitySettings;
        FileConsistencySettings = fileConsistencySettings;
        ValidationSettings = validationSettings;
        Formatting = formatting;
        ImportModules = importModules;
        PlaceHolders = placeHolders ?? Array.Empty<PlaceHolderReplacement>();
        PlaceHolderOption = placeHolderOption;
        CommandModuleDependencies = commandModuleDependencies ?? new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase);
        TestsAfterMerge = testsAfterMerge ?? Array.Empty<TestConfiguration>();
        MergeModule = mergeModule;
        MergeMissing = mergeMissing;
        ApprovedModules = approvedModules ?? Array.Empty<string>();
        ModuleSkip = moduleSkip;
        SignModule = signModule;
        Signing = signing;
        Publishes = publishes ?? Array.Empty<ConfigurationPublishSegment>();
        Artefacts = artefacts;
        InstallEnabled = installEnabled;
        InstallStrategy = installStrategy;
        InstallKeepVersions = installKeepVersions;
        InstallRoots = installRoots;
        InstallMissingModules = installMissingModules;
        InstallMissingModulesForce = installMissingModulesForce;
        InstallMissingModulesPrerelease = installMissingModulesPrerelease;
        InstallMissingModulesRepository = installMissingModulesRepository;
        InstallMissingModulesCredential = installMissingModulesCredential;
        StagingWasGenerated = stagingWasGenerated;
        DeleteGeneratedStagingAfterRun = deleteGeneratedStagingAfterRun;
    }
}
