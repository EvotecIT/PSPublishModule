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
        state.SigningResult = signingResult;

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
}
