using System.Diagnostics;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    private DotNetPublishArtefactResult Publish(
        DotNetPublishPlan plan,
        string targetName,
        string framework,
        string rid,
        DotNetPublishStyle? styleOverride,
        string reservationOwner,
        NoBuildPublishInputSnapshot? inputSnapshot,
        PublishProvenanceLease? provenanceLease,
        SourceProvenance? verifiedSourceProvenance,
        IReadOnlyCollection<string> plannedPublishGeneratedPaths,
        out SourceProvenance? finalSourceProvenance,
        Action<string>? signingProgress = null)
    {
        var target = plan.Targets.FirstOrDefault(t => string.Equals(t.Name, targetName, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException($"Target not found: {targetName}");

        var tfm = string.IsNullOrWhiteSpace(framework) ? target.Publish.Framework : framework.Trim();
        var style = styleOverride ?? target.Publish.Style;
        var publishStep = new DotNetPublishStep
        {
            TargetName = target.Name,
            Framework = tfm,
            Runtime = rid,
            Style = style
        };
        var tokens = BuildPublishOutputTokens(plan, target, tfm, rid, style);
        string outputDir = ResolvePublishOutputDirectory(plan, target, tfm, rid, style);

        string[] evidencePaths = Array.Empty<string>();
        SourceProvenance? signingProvenance = null;
        if (target.Publish.Sign?.Enabled == true)
        {
            signingProvenance = verifiedSourceProvenance ??
                ReadPortableInventorySourceProvenance(plan, outputDir, publishStep: publishStep);
        }

        Directory.CreateDirectory(outputDir);

        var lifecycle = target.Publish.Service?.Lifecycle;
        if (target.Publish.Service is not null
            && lifecycle is not null
            && lifecycle.Enabled
            && lifecycle.Mode == DotNetPublishServiceLifecycleMode.InlineRebuild)
        {
            ExecuteServiceLifecycleInlineBeforePublish(outputDir, target.Name, target.Publish.Service, lifecycle);
        }

        // The registered service owns the executable lock until the inline stop completes.
        // Keep the guard after stopping so unrelated/orphaned locks still fail before writes.
        EnsureOutputDirectoryUnlocked(
            plan,
            outputDir,
            contextLabel: $"{target.Name} ({tfm}, {rid}, {style})",
            serviceName: target.Publish.Service?.ServiceName);

        var stateTransfer = PreserveStateBeforePublish(
            plan,
            outputDir,
            target.Publish.State,
            tokens,
            $"{target.Name} ({tfm}, {rid}, {style})");

        using var staging = target.Publish.UseStaging ? new PublishStagingDirectory(_logger) : null;
        var publishDir = staging?.DirectoryPath ?? outputDir;
        if (staging is not null)
            _logger.Info($"Using staging publish dir -> {publishDir}");

        var publishArgs = BuildPublishArguments(
            plan,
            target,
            tfm,
            rid,
            style,
            publishDir,
            reservationOwner,
            inputSnapshot?.TargetsPath);

        _logger.Info($"Publishing {target.Name} ({rid}) -> {publishDir}");
        provenanceLease?.ValidateUnchanged();
        RunDotnet(plan.ProjectRoot, publishArgs, plan.EnvironmentVariables);
        inputSnapshot?.ValidateUnchanged();
        provenanceLease?.ValidateUnchanged();

        var cleanup = ApplyCleanup(publishDir, target.Publish);

        if (!string.IsNullOrWhiteSpace(target.Publish.RenameTo))
            TryRenameMainExecutable(publishDir, rid, target.Publish.RenameTo!.Trim(), target.ExecutableIdentities);

        if (target.Publish.UseStaging)
        {
            if (target.Publish.ClearOutput && Directory.Exists(outputDir))
            {
                try { Directory.Delete(outputDir, recursive: true); }
                catch { /* best effort */ }
                Directory.CreateDirectory(outputDir);
            }

            DirectoryCopy(publishDir, outputDir);
        }

        if (stateTransfer is not null)
            RestorePreservedState(outputDir, stateTransfer);

        DotNetPublishServicePackageResult? servicePackage = null;
        if (target.Publish.Service is not null)
            servicePackage = TryCreateServicePackage(outputDir, target.Name, rid, target.Publish.Service);

        // Capture the clean payload before the service starts writing runtime state.
        string[]? publishedChecksumFiles = target.Publish.UseStaging
            ? CaptureStagedPublishChecksumFiles(publishDir, outputDir, servicePackage)
            : null;

        string[] signedFilePaths = Array.Empty<string>();
        if (target.Publish.Sign?.Enabled == true)
        {
            // A normal publish may execute MSBuild targets. Recheck the Git source
            // before invoking the signer, not only after signatures have been written.
            if (!plan.UseControlledSourceProvenance)
                signingProvenance = ReadPortableInventorySourceProvenance(
                    plan, outputDir, plannedPublishGeneratedPaths, publishStep);
            signedFilePaths = TrySignOutput(outputDir, target.Publish.Sign, signingProgress);
            if (signedFilePaths.Length > 0)
            {
                signingProvenance = ReadPortableInventorySourceProvenance(
                    plan,
                    outputDir,
                    plannedPublishGeneratedPaths,
                    publishStep);
                provenanceLease?.EnsureCovers(PublishProvenanceLease.BuildGuardedPaths(
                    signingProvenance.PublishInputFiles,
                    signingProvenance.NoBuildPublishInputs,
                    plan.NoBuildInPublish));
                provenanceLease?.ValidateUnchanged();
                string executable = ResolvePrimaryExecutable(
                    outputDir,
                    rid,
                    EnumerateEffectiveExecutableIdentities(target))
                    ?? throw new InvalidOperationException(
                        "Signed portable output does not contain a primary executable matching the configured project identity.");
                FileVersionInfo versionInfo = FileVersionInfo.GetVersionInfo(executable);
                string executableIdentity = ResolvePortableExecutableIdentity(
                    versionInfo.ProductName,
                    versionInfo.InternalName,
                    versionInfo.OriginalFilename,
                    executable);
                if (!PortableExecutableIdentityMatches(
                        executableIdentity,
                        target.ExecutableIdentities))
                {
                    throw new InvalidOperationException(
                        $"Signed executable identity '{executableIdentity}' does not match the configured " +
                        $"project identity for publish target '{target.Name}'. Set Publish.ExecutableIdentity " +
                        "when the signed product identity is supplied by imported or generated build properties.");
                }
                string portableVersion = FirstText(versionInfo.ProductVersion, versionInfo.FileVersion);
                provenanceLease?.ValidateUnchanged();
                SourceProvenance provenance = signingProvenance
                    ?? throw new InvalidOperationException(
                        $"Signed publish target '{target.Name}' has no verified source provenance.");
                (string inventoryPath, string signaturePath) = PowerForgePortablePayloadInventoryCms.ResolveEvidencePaths(
                    outputDir,
                    executable,
                    target.Publish.Zip,
                    target.Publish.Zip ? ResolvePublishZipPath(outputDir, plan, target, tokens) : null);
                if (target.Publish.Zip)
                    Directory.CreateDirectory(Path.GetDirectoryName(inventoryPath)!);
                else
                    PowerForgePortablePayloadInventoryCms.EnsureEvidencePathsAvailable(inventoryPath, signaturePath);
                PowerForgePortablePayloadInventory inventory = PowerForgePortablePayloadInventoryCms.Create(
                    outputDir,
                    target.Name,
                    rid,
                    tfm,
                    style.ToString(),
                    provenance.Revision ?? string.Empty,
                    ComputePortableConfigurationPolicySha256(
                        target.Name,
                        target.Kind,
                        bundleId: null,
                        target.Publish.Zip,
                        target.Publish.Sign),
                    executable,
                    executableIdentity,
                    portableVersion,
                    signedFilePaths,
                    sourceDirty: provenance.Dirty is not false,
                    includeCompleteOutput: target.Publish.Zip);
                inventory.BuildInputMode = DescribeBuildInputMode(plan);
                byte[] inventoryBytes = PowerForgePortablePayloadInventoryCms.Serialize(inventory);
                byte[] signatureBytes = SignPortableInventoryWithProgress(
                    inventoryBytes,
                    ResolvePortableInventorySigningOptions(signedFilePaths, target.Publish.Sign),
                    signingProgress, target.Name);
                if (target.Publish.Zip)
                    PowerForgePortablePayloadInventoryCms.RewriteEvidenceFiles(inventoryPath, inventoryBytes, signaturePath, signatureBytes);
                else
                    PowerForgePortablePayloadInventoryCms.WriteEvidenceFiles(inventoryPath, inventoryBytes, signaturePath, signatureBytes);
                evidencePaths = new[] { inventoryPath, signaturePath };
            }
        }

        string? zipPath = null;
        if (target.Publish.Zip)
            zipPath = CreateZip(outputDir, plan, target, rid, tokens);

        if (servicePackage is not null
            && lifecycle is not null
            && lifecycle.Enabled
            && lifecycle.Mode == DotNetPublishServiceLifecycleMode.InlineRebuild)
        {
            ExecuteServiceLifecycleInlineAfterPublish(outputDir, servicePackage, lifecycle);
        }

        var summary = SummarizeDirectory(outputDir, rid);
        string? primaryExecutable = Directory.Exists(outputDir)
            ? ResolvePrimaryExecutable(outputDir, rid, target.ExecutableIdentities)
            : null;
        finalSourceProvenance = signingProvenance ?? verifiedSourceProvenance;
        return new DotNetPublishArtefactResult
        {
            Target = target.Name,
            Kind = target.Kind,
            Runtime = rid,
            Framework = tfm,
            Style = style,
            PublishDir = publishDir,
            OutputDir = outputDir,
            ZipPath = zipPath,
            Files = summary.Files,
            TotalBytes = summary.TotalBytes,
            ExePath = primaryExecutable,
            ExeBytes = primaryExecutable is null ? null : new FileInfo(primaryExecutable).Length,
            Cleanup = cleanup,
            ServicePackage = servicePackage,
            StateTransfer = stateTransfer,
            SignedFiles = signedFilePaths.Length,
            SignedFilePaths = signedFilePaths,
            EvidencePaths = evidencePaths,
            PublishedChecksumFiles = publishedChecksumFiles
        };
    }

}
