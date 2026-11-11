using System.Text.Json;
using System.Security.Cryptography;

namespace PowerForge;

public sealed partial class ModulePipelineRunner {
    // Persist only finalized output identity and selection, never publish configuration or credentials.
    // The release coordinator includes this file in the validation input checkpoint.
    private static void SavePublicationCheckpoint(ModulePipelinePlan plan, ModulePipelineRunState state) {
        var build = state.RequireBuildResult();
        var paths = new[] { build.StagingPath }
            .Concat(state.ArtefactResults.SelectMany(EnumerateOwnedArtefactPaths))
            .Select(Path.GetFullPath).Distinct(PowerShellCompilationPathSafety.PathComparer).ToArray();
        var checkpoint = new ModulePublicationCheckpoint {
            ModuleName = plan.ModuleName, Version = plan.ResolvedVersion, PreRelease = plan.PreRelease,
            StagingPath = build.StagingPath, SourceCommitSha = plan.SourceCommitSha,
            SigningResult = state.SigningResult,
            CompilationContractSha256 = state.PowerShellCompilationReleaseContract?.Sha256,
            CompilationRequiredModules = state.PowerShellCompilationReleaseContract?.RequiredModules ?? [],
            CompilationExternalModules = state.PowerShellCompilationReleaseContract?.ExternalModuleDependencies ?? [],
            PayloadFiles = ArtefactBuilder.ResolveModulePackageSourceFiles(
                build.StagingPath, plan.Information, plan.Delivery, !state.PackageWithoutScriptFolders, build.FinalizedPayloadFiles),
            Artefacts = state.ArtefactResults.Select(artefact => new PublicationArtefactCheckpoint {
                Type = artefact.Type, Id = artefact.Id, OutputPath = artefact.OutputPath,
                Modules = artefact.Modules, CopiedItems = artefact.CopiedItems,
                EvidencePaths = artefact.EvidencePaths, EntryPointRelativePath = artefact.EntryPointRelativePath
            }).ToArray(),
            Paths = paths,
            Hashes = PowerForgeReleaseService.CaptureValidationIntegrity(paths, CancellationToken.None),
            UnixModes = state.FinalizedPackedArtefactUnixModes
                .Concat(state.FinalizedLooseArtefactDirectoryUnixModes)
                .ToDictionary(entry => entry.Key, entry => entry.Value, PowerShellCompilationPathSafety.PathComparer)
        };
        string path = ModuleBuildSpec.GetPublicationCheckpointPath(build.StagingPath);
        FileSystemPathSafety.RejectReparsePoints(path, Path.GetDirectoryName(path), "Module publication checkpoint");
        File.WriteAllText(path, JsonSerializer.Serialize(checkpoint));
    }

    private ModulePipelineResult PublishFinalizedCheckpoint(
        ModulePipelineSpec spec, ModulePipelinePlan plan, ModulePipelineExecutionSession session,
        ModuleBuildPipeline pipeline, ModulePipelineRunState state) {
        if (plan.InstallEnabled || plan.ProjectBuilds.Length > 0 || plan.PackageBuilds.Length > 0) {
            throw new InvalidOperationException("Finalized module publication requires SkipInstall and IncludeProjectPackages=false; publish prerequisite packages through the release coordinator.");
        }
        string staging = Path.GetFullPath(plan.BuildSpec.StagingPath
            ?? throw new InvalidOperationException("Checkpoint publication requires an explicit StagingPath."));
        string path = ModuleBuildSpec.GetPublicationCheckpointPath(staging);
        FileSystemPathSafety.RequireRegularFile(path);
        byte[] checkpointBytes = File.ReadAllBytes(path);
        using var sha256 = SHA256.Create();
        string checkpointSha256 = BitConverter.ToString(sha256.ComputeHash(checkpointBytes)).Replace("-", string.Empty);
        if (!string.IsNullOrWhiteSpace(plan.BuildSpec.PublicationCheckpointSha256) &&
            !string.Equals(plan.BuildSpec.PublicationCheckpointSha256, checkpointSha256, StringComparison.OrdinalIgnoreCase)) {
            throw new InvalidOperationException($"Release validation changed a release input: '{path}'. Probes must leave release assets unchanged.");
        }
        var checkpoint = JsonSerializer.Deserialize<ModulePublicationCheckpoint>(checkpointBytes)
            ?? throw new InvalidOperationException("The finalized module publication checkpoint is empty.");
        if (!string.Equals(checkpoint.ModuleName, plan.ModuleName, StringComparison.Ordinal) ||
            !string.Equals(checkpoint.Version, plan.ResolvedVersion, StringComparison.Ordinal) ||
            !string.Equals(checkpoint.PreRelease, plan.PreRelease, StringComparison.Ordinal) ||
            !PowerShellCompilationPathSafety.PathComparer.Equals(checkpoint.StagingPath, staging) ||
            checkpoint.PayloadFiles.Length == 0 || !checkpoint.Paths.Contains(staging, PowerShellCompilationPathSafety.PathComparer) ||
            checkpoint.Hashes.Count == 0) {
            throw new InvalidOperationException("The finalized module publication checkpoint does not match the requested module output.");
        }

        // The enclosing release already checked this file. Include it in the local guard as well
        // so a module validation/action cannot rewrite it between restore and the upload callback.
        var publicationHashes = new Dictionary<string, string>(checkpoint.Hashes, PowerShellCompilationPathSafety.PathComparer) {
            [path] = checkpointSha256
        };
        state.PublicationIntegrity = new ReleaseValidationIntegrityCheckpoint(
            checkpoint.Paths.Append(path).ToArray(), publicationHashes);
        state.PublicationUnixModes = checkpoint.UnixModes;
        ValidatePublicationCheckpointIntegrity(state);
        state.Staged = pipeline.StageToStaging(plan.BuildSpec);
        state.StagingPathForCleanup = state.Staged.StagingPath;
        string manifest = Path.Combine(staging, plan.ModuleName + ".psd1");
        state.BuildResult = new ModuleBuildResult(staging, manifest, ModuleManifestExportReader.ReadExports(manifest),
            buildNotes: null, finalizedPayloadFiles: checkpoint.PayloadFiles);
        plan.SourceCommitSha = checkpoint.SourceCommitSha;
        state.SigningResult = checkpoint.SigningResult;
        if (plan.BuildSpec.PowerShellCompilation?.Enabled == true) {
            // Build and publication deliberately select different operations. Keep the original
            // build contract; Restore still checks its signed receipt, signer and exact inventory.
            state.PowerShellCompilationReleaseContract = new PowerShellModuleCompilationReleaseContract(
                checkpoint.CompilationContractSha256
                    ?? throw new InvalidOperationException("The finalized module checkpoint has no compilation contract."),
                checkpoint.ModuleName, checkpoint.Version, checkpoint.PreRelease?.Trim() ?? string.Empty,
                checkpoint.CompilationRequiredModules, checkpoint.CompilationExternalModules);
        }
        foreach (var artefact in checkpoint.Artefacts) {
            state.ArtefactResults.Add(new ArtefactBuildResult(artefact.Type, artefact.Id, artefact.OutputPath,
                artefact.Modules, artefact.CopiedItems, artefact.EvidencePaths, artefact.EntryPointRelativePath));
        }
        ExecuteCompilationPhase(plan, session, state, plan.RequiredModules, plan.ExternalModuleDependencies);
        CaptureAuthorizedProjectManifest(plan, state);
        RefreshFinalizedReleasePayloadIntegrity(plan, state);
        ExecuteValidationPhases(plan, session, state);
        ExecuteTestPhases(plan, session, state);
        ValidateRequestedReleaseVersion(plan, state);
        ExecuteActions(ModulePipelineActionStage.BeforePublish, plan, session, state);
        ValidatePublicationCheckpointIntegrity(state);
        ValidateDeliveredArtefactIntegrity(plan, state);
        ExecutePublishOperations(plan, session, state.RequireBuildResult(), state);
        ExecuteActions(ModulePipelineActionStage.AfterPublish, plan, session, state);
        ValidatePublicationCheckpointIntegrity(state);
        ValidateDeliveredArtefactIntegrity(plan, state);
        return BuildPipelineResult(spec, plan, state);
    }

    private static void ValidatePublicationCheckpointIntegrity(ModulePipelineRunState state) {
        if (state.PublicationIntegrity is { } checkpoint) {
            PowerForgeReleaseService.ValidateIntegrityUnchanged(checkpoint.Paths, checkpoint.Hashes, CancellationToken.None);
        }
        foreach (var entry in state.PublicationUnixModes) {
            if (ReadFinalizedArtefactUnixMode(entry.Key) != entry.Value) {
                ThrowFinalizedArtefactChanged(entry.Key, state.SigningResult is not null);
            }
        }
    }

    private static ReleaseValidationIntegrityCheckpoint ValidateFinalizedRepositoryPayload(
        ModulePipelineRunState state, string uploadRoot) {
        ValidatePublicationCheckpointIntegrity(state);
        var build = state.RequireBuildResult();
        var original = state.PublicationIntegrity
            ?? throw new InvalidOperationException("Finalized publication requires an integrity checkpoint.");
        // Bind the copied upload inventory to the original validated hashes, never to a new snapshot.
        var expected = new Dictionary<string, string>(PowerShellCompilationPathSafety.PathComparer) {
            [uploadRoot] = "directory"
        };
        foreach (string file in build.FinalizedPayloadFiles
            ?? throw new InvalidOperationException("Finalized publication requires an explicit payload inventory.")) {
            string relative = FrameworkCompatibility.GetRelativePath(build.StagingPath, file);
            expected.Add(Path.GetFullPath(Path.Combine(uploadRoot, relative)), original.Hashes[file]);
        }
        var checkpoint = new ReleaseValidationIntegrityCheckpoint([uploadRoot], expected);
        PowerForgeReleaseService.ValidateIntegrityUnchanged(checkpoint.Paths, checkpoint.Hashes, CancellationToken.None);
        return checkpoint;
    }

    private sealed class ModulePublicationCheckpoint {
        public string ModuleName { get; set; } = string.Empty;
        public string Version { get; set; } = string.Empty;
        public string? PreRelease { get; set; }
        public string StagingPath { get; set; } = string.Empty;
        public string? SourceCommitSha { get; set; }
        public ModuleSigningResult? SigningResult { get; set; }
        public string? CompilationContractSha256 { get; set; }
        public RequiredModuleReference[] CompilationRequiredModules { get; set; } = [];
        public string[] CompilationExternalModules { get; set; } = [];
        public string[] PayloadFiles { get; set; } = [];
        public PublicationArtefactCheckpoint[] Artefacts { get; set; } = [];
        public string[] Paths { get; set; } = [];
        public Dictionary<string, string> Hashes { get; set; } = new();
        public Dictionary<string, int> UnixModes { get; set; } = new();
    }

    private sealed class PublicationArtefactCheckpoint {
        public ArtefactType Type { get; set; }
        public string? Id { get; set; }
        public string OutputPath { get; set; } = string.Empty;
        public ArtefactModuleEntry[] Modules { get; set; } = [];
        public ArtefactCopyEntry[] CopiedItems { get; set; } = [];
        public string[] EvidencePaths { get; set; } = [];
        public string? EntryPointRelativePath { get; set; }
    }
}
