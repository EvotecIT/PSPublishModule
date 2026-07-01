using System.Collections.Generic;

namespace PowerForge;

public sealed partial class ModulePipelineRunner
{
    private sealed class ModulePipelineRunState
    {
        public ModulePipelineRunState(string? stagingPathForCleanup)
        {
            StagingPathForCleanup = stagingPathForCleanup;
        }

        public string? StagingPathForCleanup { get; set; }
        public Exception? PipelineFailure { get; set; }
        public ModuleDependencyInstallResult[] DependencyInstallResults { get; set; } = Array.Empty<ModuleDependencyInstallResult>();
        public ModuleBuildPipeline.StagingResult? Staged { get; set; }
        public ModuleBuildResult? BuildResult { get; set; }
        public MergeExecutionResult MergeExecution { get; set; } = MergeExecutionResult.None;
        public DocumentationBuildResult? DocumentationResult { get; set; }
        public FormatterResult[] FormattingStagingResults { get; set; } = Array.Empty<FormatterResult>();
        public FormatterResult[] FormattingProjectResults { get; set; } = Array.Empty<FormatterResult>();
        public ModuleSigningResult? SigningResult { get; set; }
        public ProjectConsistencyReport? FileConsistencyReport { get; set; }
        public CheckStatus? FileConsistencyStatus { get; set; }
        public ProjectConversionResult? FileConsistencyEncodingFix { get; set; }
        public ProjectConversionResult? FileConsistencyLineEndingFix { get; set; }
        public ProjectConsistencyReport? ProjectFileConsistencyReport { get; set; }
        public CheckStatus? ProjectFileConsistencyStatus { get; set; }
        public ProjectConversionResult? ProjectFileConsistencyEncodingFix { get; set; }
        public ProjectConversionResult? ProjectFileConsistencyLineEndingFix { get; set; }
        public PowerShellCompatibilityReport? CompatibilityReport { get; set; }
        public ModuleValidationReport? ValidationReport { get; set; }
        public BuildDiagnostic[] AutomaticBinaryConflictDiagnostics { get; set; } = Array.Empty<BuildDiagnostic>();
        public List<ArtefactBuildResult> ArtefactResults { get; } = new();
        public List<ModulePublishResult> PublishResults { get; } = new();
        public List<ProjectBuildHostExecutionResult> ProjectBuildResults { get; } = new();
        public List<ExternalAssetPreparationResult> ExternalAssetResults { get; } = new();
        public List<ReleaseVersionCandidate> ReleaseVersionCandidates { get; } = new();
        public ModuleReleaseCoordinationResult? ReleaseCoordinationResult { get; set; }
        public List<ModulePipelineActionResult> ActionResults { get; } = new();
        public List<XcodeProjectVersionUpdateResult> XcodeProjectVersionResults { get; } = new();
        public List<AppleAppReleasePreparationResult> AppleAppResults { get; } = new();
        public ModuleInstallerResult? InstallResult { get; set; }
        public string? ProjectManifestSyncMessage { get; set; }
        public bool PackageWithoutScriptFolders =>
            MergeExecution.MergedModule ||
            (MergeExecution.UsedExistingPsm1 && !MergeExecution.HasScriptSources);

        public ModuleBuildResult RequireBuildResult()
            => BuildResult ?? throw new InvalidOperationException("Build result is not available for the current pipeline state.");

        public ModuleBuildPipeline.StagingResult RequireStaged()
            => Staged ?? throw new InvalidOperationException("Staging result is not available for the current pipeline state.");
    }

    private sealed class ReleaseVersionCandidate
    {
        public ReleaseVersionCandidate(
            ReleaseVersionSource source,
            string label,
            bool explicitSource,
            ProjectBuildHostExecutionResult result)
        {
            Source = source;
            Label = label;
            ExplicitSource = explicitSource;
            Result = result ?? throw new ArgumentNullException(nameof(result));
        }

        public ReleaseVersionSource Source { get; }
        public string Label { get; }
        public bool ExplicitSource { get; }
        public ProjectBuildHostExecutionResult Result { get; }
    }
}
