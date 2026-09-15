using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace PowerForge;

public sealed partial class ModulePipelineRunner
{
    /// <summary>
    /// Finalizes and re-signs a derived install manifest before any installed copy is written.
    /// Public/package artefacts retain their release version and signature, while a versioned local
    /// install receives a separately signed manifest whose ModuleVersion matches its install folder.
    /// </summary>
    private ModuleSigningResult SignChangedInstallManifest(
        ModulePipelinePlan plan,
        string manifestPath,
        ModuleSigningResult? sourceSigningResult)
    {
        manifestPath = Path.GetFullPath(manifestPath);

        var installManifestSigning = CloneSigningOptions(plan.Signing)
            ?? throw new InvalidOperationException("Signing is enabled but no signing options were provided.");
        installManifestSigning.OverwriteSigned = true;

        var signingResult = _hostedOperations.SignModuleOutput(
            plan.ModuleName,
            Path.GetDirectoryName(manifestPath)
                ?? throw new InvalidOperationException("The install manifest directory could not be resolved."),
            new[] { manifestPath },
            new[] { "*.psd1" },
            Array.Empty<string>(),
            installManifestSigning);
        var verifiedManifestPaths = (signingResult.VerifiedFilePaths ?? Array.Empty<string>())
            .Where(static path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .ToArray();
        if (!signingResult.Success ||
            !verifiedManifestPaths.Contains(manifestPath, PowerShellCompilationPathSafety.PathComparer))
        {
            throw new InvalidOperationException(
                $"The versioned install manifest was not successfully re-signed: '{manifestPath}'.");
        }

        if (sourceSigningResult is not null)
            _ = AggregateSigningResults(sourceSigningResult, signingResult);

        return signingResult;
    }

    private static void ValidateAndFinalizeSignedInstall(
        ModulePipelinePlan plan,
        string signedSourceRoot,
        string installPackagePath,
        IReadOnlyList<string> installedPaths,
        ModuleSigningResult? sourceSigningResult,
        ModuleSigningResult? installPackageSigningResult)
    {
        foreach (var installedPath in installedPaths)
        {
            var deliveredSigningResult = CreateDeliveredSigningResult(
                sourceSigningResult,
                signedSourceRoot,
                installedPath);
            if (installPackageSigningResult is not null)
            {
                var deliveredInstallPackageSigningResult = CreateDeliveredSigningResult(
                    installPackageSigningResult,
                    installPackagePath,
                    installedPath)
                    ?? throw new InvalidOperationException(
                        "The signed install manifest was not delivered byte-for-byte to the installed module.");
                deliveredSigningResult = deliveredSigningResult is null
                    ? deliveredInstallPackageSigningResult
                    : AggregateSigningResults(deliveredSigningResult, deliveredInstallPackageSigningResult);
            }

            _ = PowerShellModuleCompilationIntegrator.FinalizeDeliveredCanonicalManifest(
                installedPath,
                plan.ModuleName,
                deliveredSigningResult,
                plan.Signing);
        }
    }
}
