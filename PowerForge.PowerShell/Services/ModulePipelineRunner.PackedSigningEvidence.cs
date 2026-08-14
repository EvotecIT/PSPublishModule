using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerForge;

public sealed partial class ModulePipelineRunner
{
    private IReadOnlyList<string> FinalizeSignedPackedArtefact(
        ModulePipelinePlan plan,
        ModulePipelineRunState state,
        PackedArtefactFinalizationContext context)
    {
        SigningOptionsConfiguration signing = plan.Signing ?? throw new InvalidOperationException(
            "Signing is enabled but no signing options were provided.");
        string manifestDirectory = Path.GetDirectoryName(context.ManifestPath)
            ?? throw new InvalidOperationException("Packed module manifest directory could not be resolved.");
        string[] loadedContentFiles = ModuleManifestLoadedContent.ReadRelativePaths(context.ManifestPath)
            .Select(path => Path.GetFullPath(Path.Combine(manifestDirectory, path)))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        string rootPrefix = Path.GetFullPath(context.RootPath)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        if (loadedContentFiles.Any(path =>
                !path.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase) || !File.Exists(path)))
        {
            throw new InvalidOperationException(
                "Every manifest-loaded module file must exist inside the final packed module layout before signing.");
        }
        string sourceAttestationPath = Path.Combine(
            context.MainModulePath,
            PowerForgeModuleSourceAttestationWriter.FileName);
        if (plan.GenerateReleaseProvenance)
        {
            if (string.IsNullOrWhiteSpace(plan.SourceRevision) ||
                plan.SourceDirty ||
                string.IsNullOrWhiteSpace(plan.SourceRepositoryUrl))
            {
                throw new InvalidOperationException(
                    "Signed GitHub module release provenance was not resolved from a clean source checkout.");
            }

            string projectManifestPath = Path.Combine(plan.ProjectRoot, plan.ModuleName + ".psd1");
            if (!File.Exists(projectManifestPath) ||
                (File.GetAttributes(projectManifestPath) & FileAttributes.ReparsePoint) != 0 ||
                !File.ReadAllBytes(projectManifestPath).SequenceEqual(File.ReadAllBytes(context.ManifestPath)))
            {
                throw new InvalidOperationException(
                    "The generated project manifest does not match the final packed module manifest.");
            }

            ValidateReleaseSourceUnchanged(
                plan,
                new[]
                {
                    context.RootPath,
                    context.OutputPath,
                    plan.BuildSpec.StagingPath ?? string.Empty
                },
                new[] { projectManifestPath });

            PowerForgeModuleSourceAttestationWriter.WriteReleaseProvenance(
                context.ManifestPath,
                context.ModuleName,
                context.Version,
                plan.SourceRepositoryUrl!,
                plan.SourceRevision!,
                sourceDirty: false);
        }
        string[] packageFiles = Directory.EnumerateFiles(context.RootPath, "*", SearchOption.AllDirectories)
            .Select(Path.GetFullPath)
            .Except(loadedContentFiles, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        string[] includePatterns = BuildSigningIncludePatterns(signing);
        string[] excludeSubstrings = BuildSigningExcludeSubstrings(
            signing,
            plan.Delivery,
            excludeBundledRequiredModules: false);
        ModuleSigningResult signingResult = _hostedOperations.SignModuleOutput(
            context.ModuleName,
            context.RootPath,
            packageFiles,
            includePatterns,
            excludeSubstrings,
            signing);
        if (loadedContentFiles.Length > 0)
        {
            SigningOptionsConfiguration loadedContentSigning = CloneSigningOptions(signing)
                ?? throw new InvalidOperationException("Signing options could not be cloned.");
            loadedContentSigning.OverwriteSigned = true;
            ModuleSigningResult loadedContentResult = _hostedOperations.SignModuleOutput(
                context.ModuleName,
                context.RootPath,
                loadedContentFiles,
                new[] { "*" },
                Array.Empty<string>(),
                loadedContentSigning);
            signingResult = AggregateSigningResults(signingResult, loadedContentResult);
        }

        if (!File.Exists(sourceAttestationPath))
        {
            state.SigningResult = AggregateSigningResults(state.SigningResult, signingResult);
            return Array.Empty<string>();
        }

        PowerForgeModuleSourceAttestationWriter.BindSigningInventory(
            context.ManifestPath,
            context.RootPath,
            signingResult);
        SigningOptionsConfiguration attestationSigning = CloneSigningOptions(signing)
            ?? throw new InvalidOperationException("Signing options could not be cloned.");
        attestationSigning.OverwriteSigned = true;
        ModuleSigningResult attestationSigningResult = _hostedOperations.SignModuleOutput(
            context.ModuleName,
            context.RootPath,
            new[] { sourceAttestationPath },
            new[] { "*.psd1" },
            Array.Empty<string>(),
            attestationSigning);
        string normalizedAttestation = Path.GetFullPath(sourceAttestationPath);
        if (attestationSigningResult.Failed > 0 ||
            !(attestationSigningResult.VerifiedFilePaths ?? Array.Empty<string>())
                .Select(Path.GetFullPath)
                .Contains(normalizedAttestation, StringComparer.OrdinalIgnoreCase) ||
            !string.Equals(
                NormalizeOptionalThumbprint(signingResult.CertificateThumbprint),
                NormalizeOptionalThumbprint(attestationSigningResult.CertificateThumbprint),
                StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "The signed module source attestation could not be rebound to the packed signing inventory.");
        }
        state.SigningResult = AggregateSigningResults(state.SigningResult, signingResult);

        string evidencePath = context.OutputPath + ".signing.json";
        return new[]
        {
            PowerForgeModuleSigningEvidenceWriter.WriteFromSignedSourceAttestation(
                evidencePath,
                context.RootPath,
                context.ModuleName,
                context.Version,
                context.ManifestPath,
                signingResult)
        };
    }

    private static ModuleSigningResult AggregateSigningResults(
        ModuleSigningResult? existing,
        ModuleSigningResult current)
    {
        if (existing is null)
            return current;
        string? existingThumbprint = NormalizeOptionalThumbprint(existing.CertificateThumbprint);
        string? currentThumbprint = NormalizeOptionalThumbprint(current.CertificateThumbprint);
        if (existingThumbprint is not null && currentThumbprint is not null &&
            !string.Equals(existingThumbprint, currentThumbprint, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Packed artefact signing results used different certificates.");

        return new ModuleSigningResult
        {
            TotalMatched = existing.TotalMatched + current.TotalMatched,
            TotalAfterExclude = existing.TotalAfterExclude + current.TotalAfterExclude,
            AlreadySignedByThisCert = existing.AlreadySignedByThisCert + current.AlreadySignedByThisCert,
            AlreadySignedOther = existing.AlreadySignedOther + current.AlreadySignedOther,
            Attempted = existing.Attempted + current.Attempted,
            SignedNew = existing.SignedNew + current.SignedNew,
            Resigned = existing.Resigned + current.Resigned,
            Failed = existing.Failed + current.Failed,
            PrecheckFailure = existing.PrecheckFailure + current.PrecheckFailure,
            UnknownError = existing.UnknownError + current.UnknownError,
            SigningException = existing.SigningException + current.SigningException,
            CertificateThumbprint = existingThumbprint ?? currentThumbprint,
            FailedFiles = Concat(existing.FailedFiles, current.FailedFiles),
            FailedFilePaths = Concat(existing.FailedFilePaths, current.FailedFilePaths),
            VerifiedFilePaths = Concat(existing.VerifiedFilePaths, current.VerifiedFilePaths),
            PreservedThirdPartySignatures = (existing.PreservedThirdPartySignatures ?? Array.Empty<ModuleSigningPreservedSignature>())
                .Concat(current.PreservedThirdPartySignatures ?? Array.Empty<ModuleSigningPreservedSignature>())
                .ToArray()
        };
    }

    private static string[] Concat(string[]? existing, string[]? current)
        => (existing ?? Array.Empty<string>()).Concat(current ?? Array.Empty<string>()).ToArray();

    private static string? NormalizeOptionalThumbprint(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value!.Trim().Replace(" ", string.Empty);
}
