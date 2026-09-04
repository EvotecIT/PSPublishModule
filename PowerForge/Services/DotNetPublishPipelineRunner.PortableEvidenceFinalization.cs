using System.Diagnostics;

namespace PowerForge;

public sealed partial class DotNetPublishPipelineRunner
{
    internal void FinalizePortableEvidence(
        DotNetPublishPlan plan,
        IReadOnlyList<DotNetPublishArtefactResult> artefacts,
        IReadOnlyDictionary<string, SourceProvenance>? verifiedProvenanceByArtifact = null)
    {
        if (plan is null) throw new ArgumentNullException(nameof(plan));
        if (artefacts is null) throw new ArgumentNullException(nameof(artefacts));

        foreach (DotNetPublishArtefactResult artefact in artefacts)
        {
            if (artefact.Category != DotNetPublishArtefactCategory.Publish &&
                artefact.Category != DotNetPublishArtefactCategory.Bundle)
            {
                continue;
            }

            DotNetPublishTargetPlan? target = (plan.Targets ?? Array.Empty<DotNetPublishTargetPlan>())
                .SingleOrDefault(candidate => string.Equals(
                    candidate.Name,
                    artefact.Target,
                    StringComparison.OrdinalIgnoreCase));
            DotNetPublishSignOptions? sign = target?.Publish?.Sign;
            if (sign?.Enabled != true)
                continue;
            if (target is null || artefact.SignedFilePaths.Length == 0)
                throw new InvalidOperationException(
                    $"Signed portable artifact '{artefact.Target}' does not retain its publisher-owned signed-file inventory.");

            DotNetPublishBundlePlan? bundle = artefact.Category == DotNetPublishArtefactCategory.Bundle
                ? (plan.Bundles ?? Array.Empty<DotNetPublishBundlePlan>()).SingleOrDefault(candidate =>
                    string.Equals(candidate.Id, artefact.BundleId, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(candidate.PrepareFromTarget, artefact.Target, StringComparison.OrdinalIgnoreCase))
                : null;
            if (artefact.Category == DotNetPublishArtefactCategory.Bundle && bundle is null)
                throw new InvalidOperationException($"Bundle '{artefact.BundleId}' was not found while finalizing release evidence.");

            bool archivePayload = bundle?.Zip ?? target.Publish.Zip;
            if (archivePayload && (string.IsNullOrWhiteSpace(artefact.ZipPath) || !File.Exists(artefact.ZipPath)))
                throw new InvalidOperationException(
                    $"Signed portable artifact '{artefact.Target}' is missing its configured ZIP output.");

            string outputDirectory = Path.GetFullPath(artefact.OutputDir);
            string primaryExecutable = !string.IsNullOrWhiteSpace(artefact.ExePath) && File.Exists(artefact.ExePath)
                ? Path.GetFullPath(artefact.ExePath)
                : ResolvePrimaryExecutable(
                    outputDirectory,
                    artefact.Runtime,
                    target.ExecutableIdentities,
                    recursive: artefact.Category == DotNetPublishArtefactCategory.Bundle)
                  ?? throw new InvalidOperationException(
                      $"Signed portable artifact '{artefact.Target}' no longer contains its primary executable.");
            FileVersionInfo versionInfo = FileVersionInfo.GetVersionInfo(primaryExecutable);
            string executableIdentity = ResolvePortableExecutableIdentity(
                versionInfo.ProductName,
                versionInfo.InternalName,
                versionInfo.OriginalFilename,
                primaryExecutable);
            if (!PortableExecutableIdentityMatches(executableIdentity, target.ExecutableIdentities))
                throw new InvalidOperationException(
                    $"Signed executable identity '{executableIdentity}' no longer matches publish target '{target.Name}'.");

            string[] signedFilePaths = artefact.SignedFilePaths
                .Select(Path.GetFullPath)
                .ToArray();
            DotNetPublishSignOptions inventorySign = ResolvePortableInventorySigningOptions(signedFilePaths, sign);
            string configurationPolicySha256 = ComputePortableConfigurationPolicySha256(
                target.Name,
                target.Kind,
                artefact.BundleId,
                archivePayload,
                sign);
            var publishStep = new DotNetPublishStep
            {
                TargetName = artefact.Target,
                Framework = artefact.Framework,
                Runtime = artefact.Runtime,
                Style = artefact.Style
            };
            SourceProvenance provenance = verifiedProvenanceByArtifact is not null &&
                                          verifiedProvenanceByArtifact.TryGetValue(
                                              BuildArtifactProvenanceKey(artefact),
                                              out SourceProvenance? verifiedProvenance)
                ? verifiedProvenance
                : ReadPortableInventorySourceProvenance(
                    plan,
                    outputDirectory,
                    EnumerateBundleGeneratedArtefactPaths(artefacts),
                    publishStep);
            string portableVersion = FirstText(versionInfo.ProductVersion, versionInfo.FileVersion);
            PowerForgePortablePayloadInventory inventory = archivePayload
                ? PowerForgePortablePayloadInventoryCms.CreateFromArchive(
                    artefact.ZipPath!,
                    outputDirectory,
                    target.Name,
                    artefact.Runtime,
                    artefact.Framework,
                    artefact.Style.ToString(),
                    plan.SourceRevision,
                    configurationPolicySha256,
                    primaryExecutable,
                    executableIdentity,
                    portableVersion,
                    signedFilePaths,
                    artefact.BundleId,
                    sourceDirty: provenance.Dirty is not false,
                    requireSignedDlls: sign.IncludeDlls)
                : PowerForgePortablePayloadInventoryCms.Create(
                    outputDirectory,
                    target.Name,
                    artefact.Runtime,
                    artefact.Framework,
                    artefact.Style.ToString(),
                    plan.SourceRevision,
                    configurationPolicySha256,
                    primaryExecutable,
                    executableIdentity,
                    portableVersion,
                    signedFilePaths,
                    artefact.BundleId,
                    sourceDirty: provenance.Dirty is not false,
                    includeCompleteOutput: false);

            byte[] inventoryBytes = PowerForgePortablePayloadInventoryCms.Serialize(inventory);
            byte[] signatureBytes = _signPortableInventory(inventoryBytes, inventorySign);
            if (archivePayload)
            {
                PowerForgePortablePayloadInventoryCms.RewriteArchiveEvidence(
                    artefact.ZipPath!,
                    inventoryBytes,
                    signatureBytes);
                artefact.EvidencePaths = Array.Empty<string>();
            }
            else
            {
                (string inventoryPath, string signaturePath) =
                    PowerForgePortablePayloadInventoryCms.ResolveEvidencePaths(
                        outputDirectory,
                        primaryExecutable,
                        archivePayload: false);
                PowerForgePortablePayloadInventoryCms.RewriteEvidenceFiles(
                    inventoryPath,
                    inventoryBytes,
                    signaturePath,
                    signatureBytes);
                artefact.EvidencePaths = new[] { inventoryPath, signaturePath };
            }

            var summary = SummarizeDirectory(outputDirectory, artefact.Runtime);
            artefact.Files = summary.Files;
            artefact.TotalBytes = summary.TotalBytes;
            artefact.ExePath = primaryExecutable;
            artefact.ExeBytes = new FileInfo(primaryExecutable).Length;
        }
    }
}
