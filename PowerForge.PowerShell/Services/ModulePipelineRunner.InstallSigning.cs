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
    private ModuleSigningResult? SignChangedInstallManifest(
        ModulePipelinePlan plan,
        string manifestPath,
        string signedSourceRoot,
        ModuleSigningResult? sourceSigningResult)
    {
        manifestPath = Path.GetFullPath(manifestPath);
        var signedSourceManifest = Path.GetFullPath(Path.Combine(signedSourceRoot, plan.ModuleName + ".psd1"));
        var sourceManifestWasSigned = sourceSigningResult?.Success == true &&
            (sourceSigningResult.VerifiedFilePaths ?? Array.Empty<string>())
                .Where(static path => !string.IsNullOrWhiteSpace(path))
                .Select(Path.GetFullPath)
                .Contains(signedSourceManifest, PowerShellCompilationPathSafety.PathComparer);
        if (!sourceManifestWasSigned)
            return null;

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

        return signingResult;
    }

    private static ModuleSigningResult? ValidateAndFinalizeSignedInstall(
        ModulePipelinePlan plan,
        string signedSourceRoot,
        string installPackagePath,
        IReadOnlyList<string> installedPaths,
        IReadOnlyList<string> expectedSignedSourcePaths,
        ModuleSigningResult? sourceSigningResult,
        ModuleSigningResult? installPackageSigningResult)
    {
        var deliveredInstallManifestPaths = new List<string>();
        foreach (var installedPath in installedPaths)
        {
            var replacedSourcePaths = installPackageSigningResult is null
                ? Array.Empty<string>()
                : new[] { Path.Combine(signedSourceRoot, plan.ModuleName + ".psd1") };
            var deliveredSigningResult = CreateDeliveredSigningResult(
                sourceSigningResult,
                signedSourceRoot,
                installedPath,
                replacedSourcePaths,
                expectedSignedSourcePaths);
            if (installPackageSigningResult is not null)
            {
                var deliveredInstallPackageSigningResult = CreateDeliveredSigningResult(
                    installPackageSigningResult,
                    installPackagePath,
                    installedPath)
                    ?? throw new InvalidOperationException(
                        "The signed install manifest was not delivered byte-for-byte to the installed module.");
                deliveredInstallManifestPaths.AddRange(deliveredInstallPackageSigningResult.VerifiedFilePaths);
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

        if (installPackageSigningResult is null)
            return null;

        return new ModuleSigningResult
        {
            TotalMatched = installPackageSigningResult.TotalMatched,
            TotalAfterExclude = installPackageSigningResult.TotalAfterExclude,
            AlreadySignedByThisCert = installPackageSigningResult.AlreadySignedByThisCert,
            AlreadySignedOther = installPackageSigningResult.AlreadySignedOther,
            Attempted = installPackageSigningResult.Attempted,
            SignedNew = installPackageSigningResult.SignedNew,
            Resigned = installPackageSigningResult.Resigned,
            Failed = installPackageSigningResult.Failed,
            PrecheckFailure = installPackageSigningResult.PrecheckFailure,
            UnknownError = installPackageSigningResult.UnknownError,
            SigningException = installPackageSigningResult.SigningException,
            CertificateThumbprint = installPackageSigningResult.CertificateThumbprint,
            FailedFiles = installPackageSigningResult.FailedFiles,
            FailedFilePaths = installPackageSigningResult.FailedFilePaths,
            VerifiedFilePaths = deliveredInstallManifestPaths
                .Distinct(PowerShellCompilationPathSafety.PathComparer)
                .OrderBy(static path => path, StringComparer.Ordinal)
                .ToArray(),
            PreservedThirdPartySignatures = installPackageSigningResult.PreservedThirdPartySignatures
        };
    }

    private static IReadOnlyList<string> CaptureExpectedSignedInstallSourcePaths(
        ModuleSigningResult? sourceSigningResult,
        string signedSourceRoot,
        string installPackagePath)
    {
        if (sourceSigningResult?.Success != true ||
            sourceSigningResult.VerifiedFilePaths is not { Length: > 0 })
        {
            return Array.Empty<string>();
        }

        var sourceRoot = Path.GetFullPath(signedSourceRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        var sourcePrefix = sourceRoot + Path.DirectorySeparatorChar;
        var packageRoot = Path.GetFullPath(installPackagePath);
        var expected = new List<string>();
        foreach (var sourcePath in sourceSigningResult.VerifiedFilePaths
                     .Where(static path => !string.IsNullOrWhiteSpace(path))
                     .Select(Path.GetFullPath)
                     .Where(path => path.StartsWith(
                         sourcePrefix,
                         PowerShellCompilationPathSafety.GetPathComparison(sourcePrefix)))
                     .Distinct(PowerShellCompilationPathSafety.PathComparer))
        {
            var relativePath = FrameworkCompatibility.GetRelativePath(sourceRoot, sourcePath);
            var packagePath = Path.GetFullPath(Path.Combine(packageRoot, relativePath));
            PowerShellCompilationPathSafety.EnsureContained(
                packageRoot,
                packagePath,
                $"Install signing evidence path '{relativePath}' escapes the install package root.");
            if (File.Exists(packagePath))
                expected.Add(sourcePath);
        }

        return expected;
    }
}
