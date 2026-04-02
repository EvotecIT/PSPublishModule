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
        public ModuleInstallerResult? InstallResult { get; set; }
        public string? ProjectManifestSyncMessage { get; set; }
        public bool MergedScripts => MergeExecution.MergedModule;

        public ModuleBuildResult RequireBuildResult()
            => BuildResult ?? throw new InvalidOperationException("Build result is not available for the current pipeline state.");

        public ModuleBuildPipeline.StagingResult RequireStaged()
            => Staged ?? throw new InvalidOperationException("Staging result is not available for the current pipeline state.");
    }
}
