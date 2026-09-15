using System;
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
    private ModuleSigningResult? PrepareSignedInstallPackage(
        ModulePipelinePlan plan,
        ModuleInstallSpec installSpec,
        ModuleSigningResult? sourceSigningResult)
    {
        if (!plan.SignModule || !installSpec.UpdateManifestToResolvedVersion)
            return null;

        var resolvedVersion = ModuleInstaller.ResolveTargetVersion(
            installSpec.Roots,
            installSpec.Name,
            installSpec.Version,
            installSpec.Strategy);
        var manifestPath = Path.GetFullPath(Path.Combine(
            installSpec.StagingPath,
            installSpec.Name + ".psd1"));

        if (!ManifestEditor.TryGetTopLevelString(manifestPath, "ModuleVersion", out var currentVersion) ||
            string.IsNullOrWhiteSpace(currentVersion))
        {
            throw new InvalidOperationException(
                $"The install manifest ModuleVersion could not be read before signed delivery: '{manifestPath}'.");
        }

        installSpec.Version = resolvedVersion;
        installSpec.Strategy = InstallationStrategy.Exact;
        installSpec.UpdateManifestToResolvedVersion = false;

        if (string.Equals(currentVersion, resolvedVersion, StringComparison.OrdinalIgnoreCase))
            return null;

        if (!_manifestMutator.TrySetTopLevelModuleVersion(manifestPath, resolvedVersion))
        {
            throw new InvalidOperationException(
                $"The signed install manifest could not be updated to resolved version '{resolvedVersion}': '{manifestPath}'.");
        }

        var installManifestSigning = CloneSigningOptions(plan.Signing)
            ?? throw new InvalidOperationException("Signing is enabled but no signing options were provided.");
        installManifestSigning.OverwriteSigned = true;

        var signingResult = _hostedOperations.SignModuleOutput(
            installSpec.Name,
            installSpec.StagingPath,
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
}
