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
        string[] packageFiles = Directory.EnumerateFiles(context.RootPath, "*", SearchOption.AllDirectories)
            .Select(Path.GetFullPath)
            .ToArray();
        ModuleSigningResult signingResult = _hostedOperations.SignModuleOutput(
            context.ModuleName,
            context.RootPath,
            packageFiles,
            BuildSigningIncludePatterns(signing),
            BuildSigningExcludeSubstrings(signing, plan.Delivery, excludeBundledRequiredModules: false),
            signing);
        state.SigningResult = AggregateSigningResults(state.SigningResult, signingResult);

        string sourceAttestationPath = Path.Combine(
            context.MainModulePath,
            PowerForgeModuleSourceAttestationWriter.FileName);
        if (!File.Exists(sourceAttestationPath))
            return Array.Empty<string>();

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
