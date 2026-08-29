using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace PowerForge;

/// <summary>Builds integrity-bound, redacted reproduction evidence from canonical compiler owners.</summary>
internal static class PowerShellCompilationReproductionEvidenceBuilder
{
    internal static PowerShellCompilationDiagnostic[] MakeDiagnosticsPortable(
        PowerShellCompilationPlan plan,
        IEnumerable<PowerShellCompilationDiagnostic> diagnostics)
    {
        if (plan is null) throw new ArgumentNullException(nameof(plan));
        if (diagnostics is null) throw new ArgumentNullException(nameof(diagnostics));
        var sourcePaths = plan.Files.ToDictionary(
            static file => Path.GetFullPath(file.FullPath),
            static file => NormalizeRelativePath(file.RelativePath, Path.GetFileName(file.FullPath)),
            PowerShellCompilationPathSafety.PathComparer);
        return diagnostics.Select(diagnostic => new PowerShellCompilationDiagnostic(
                diagnostic.Code,
                diagnostic.Message,
                GetPortableDiagnosticPath(diagnostic.FilePath, sourcePaths),
                diagnostic.Line,
                diagnostic.Column,
                diagnostic.FeatureId))
            .ToArray();
    }

    internal static PowerShellCompilationReproductionEvidence Create(
        PowerShellCompilationPlan plan,
        PowerShellCompilationArtifactKind kind,
        PowerShellCompilationUnitDispositionLedger unitDispositionLedger,
        PowerShellCompilationExplanation decisionTrace,
        PowerShellCompilationToolchainEvidence toolchain,
        PowerShellCompilationSemanticProfile? semanticProfile,
        PowerShellCompilationAbiManifest? publicAbi,
        string generatedSourceSha256,
        IReadOnlyCollection<PowerShellCompilationArtifactFile> files,
        IReadOnlyCollection<PowerShellCompilationDiagnostic> diagnostics,
        IReadOnlyCollection<PowerShellCompilationCommandProviderContract> providers,
        PowerShellCompilationIrSnapshotEvidence irSnapshots,
        PowerShellCompilationFailureMap failureMap,
        PowerShellCompilationAuditTrail diagnosticAudit,
        PowerShellCompilationDiagnosticsPolicy diagnosticsPolicy)
    {
        if (plan is null) throw new ArgumentNullException(nameof(plan));
        if (unitDispositionLedger is null) throw new ArgumentNullException(nameof(unitDispositionLedger));
        if (decisionTrace is null) throw new ArgumentNullException(nameof(decisionTrace));
        if (toolchain is null) throw new ArgumentNullException(nameof(toolchain));

        var sources = plan.Files
            .OrderBy(static file => NormalizePath(file.RelativePath), StringComparer.Ordinal)
            .Select(file => new PowerShellCompilationReproductionSource
            {
                RelativePath = NormalizeRelativePath(file.RelativePath, Path.GetFileName(file.FullPath)),
                Sha256 = File.Exists(file.FullPath) ? ComputeFileSha256(file.FullPath) : string.Empty
            })
            .ToArray();
        var sourceMapSha256 = GetSourceMapSha256(files);
        var evidence = new PowerShellCompilationReproductionEvidence
        {
            SchemaVersion = 3,
            Mode = plan.Mode,
            Kind = kind,
            Sources = sources,
            CompilerVersion = toolchain.CompilerVersion,
            CompilerSha256 = toolchain.CompilerSha256,
            SemanticProfileName = semanticProfile?.Name ?? string.Empty,
            SemanticProfileVersion = semanticProfile?.Version ?? string.Empty,
            TargetContractSha256 = toolchain.TargetContractSha256,
            ProviderContractsSha256 = ComputeProviderContractsSha256(providers),
            DependencyLockSha256 = toolchain.DependencyLockSha256,
            GeneratedSourceSha256 = generatedSourceSha256 ?? string.Empty,
            PublicAbiSha256 = publicAbi?.Sha256 ?? string.Empty,
            SourceMapSha256 = sourceMapSha256,
            UnitDispositionLedgerSha256 = ComputeTextSha256(Serialize(unitDispositionLedger)),
            DecisionTraceSha256 = ComputeTextSha256(Serialize(decisionTrace)),
            DiagnosticsSha256 = ComputeDiagnosticsSha256(diagnostics),
            IrSnapshotsSha256 = irSnapshots.Sha256,
            FailureMapSha256 = failureMap.Sha256,
            DiagnosticAuditSha256 = diagnosticAudit.Sha256,
            DiagnosticsPolicySha256 = PowerShellCompilationDiagnosticsEvidenceBuilder.Hash(diagnosticsPolicy),
            DotNetSdkVersion = toolchain.DotNetSdkVersion,
            DotNetSdkSha256 = toolchain.DotNetSdkSha256
        };
        evidence.EvidenceSha256 = ComputeEvidenceSha256(evidence);
        return evidence;
    }

    internal static void Validate(PowerShellCompilationArtifactManifest manifest)
    {
        if (manifest is null) throw new ArgumentNullException(nameof(manifest));
        var evidence = manifest.Reproduction
            ?? throw new InvalidOperationException("Canonical compilation evidence is missing its reproduction contract.");
        var decisionTrace = manifest.DecisionTrace
            ?? throw new InvalidOperationException("Canonical compilation evidence is missing its final decision trace.");
        var unitDispositionLedger = manifest.UnitDispositionLedger
            ?? throw new InvalidOperationException("Canonical compilation evidence is missing its final unit-disposition ledger.");
        var failureMap = manifest.FailureMap
            ?? throw new InvalidOperationException("Canonical compilation evidence is missing its portable failure map.");
        var diagnosticAudit = manifest.DiagnosticAudit
            ?? throw new InvalidOperationException("Canonical compilation evidence is missing its diagnostic audit trail.");
        var diagnosticsPolicy = manifest.DiagnosticsPolicy
            ?? throw new InvalidOperationException("Canonical compilation evidence is missing its diagnostics policy.");
        var failureMapSha256 = PowerShellCompilationDiagnosticsEvidenceBuilder.ComputeFailureMapSha256(failureMap);
        var diagnosticAuditSha256 = PowerShellCompilationDiagnosticsEvidenceBuilder.ComputeAuditTrailSha256(diagnosticAudit);
        var diagnosticsPolicySha256 = PowerShellCompilationDiagnosticsEvidenceBuilder.Hash(diagnosticsPolicy);
        if (evidence.SchemaVersion != 3 ||
            evidence.Mode != manifest.Mode ||
            evidence.Kind != manifest.Kind ||
            !EqualsIgnoreCase(evidence.CompilerVersion, manifest.Toolchain?.CompilerVersion) ||
            !EqualsIgnoreCase(evidence.CompilerSha256, manifest.Toolchain?.CompilerSha256) ||
            !EqualsIgnoreCase(evidence.TargetContractSha256, manifest.Toolchain?.TargetContractSha256) ||
            !EqualsIgnoreCase(evidence.DependencyLockSha256, manifest.Toolchain?.DependencyLockSha256) ||
            !EqualsIgnoreCase(evidence.GeneratedSourceSha256, manifest.GeneratedSourceSha256) ||
            !EqualsIgnoreCase(evidence.PublicAbiSha256, manifest.PublicAbi?.Sha256) ||
            !EqualsIgnoreCase(evidence.SemanticProfileName, manifest.SemanticProfile?.Name) ||
            !EqualsIgnoreCase(evidence.SemanticProfileVersion, manifest.SemanticProfile?.Version) ||
            !EqualsIgnoreCase(evidence.SourceMapSha256, GetSourceMapSha256(manifest.Files)) ||
            !EqualsIgnoreCase(evidence.ProviderContractsSha256, ComputeProviderContractsSha256(manifest.CommandProviders)) ||
            !EqualsIgnoreCase(evidence.UnitDispositionLedgerSha256, ComputeTextSha256(Serialize(unitDispositionLedger))) ||
            !EqualsIgnoreCase(evidence.DecisionTraceSha256, ComputeTextSha256(Serialize(decisionTrace))) ||
            !EqualsIgnoreCase(evidence.DiagnosticsSha256, ComputeDiagnosticsSha256(manifest.Diagnostics)) ||
            !EqualsIgnoreCase(evidence.IrSnapshotsSha256, manifest.IrSnapshots?.Sha256) ||
            !EqualsIgnoreCase(failureMap.Sha256, failureMapSha256) ||
            !EqualsIgnoreCase(evidence.FailureMapSha256, failureMapSha256) ||
            !EqualsIgnoreCase(diagnosticAudit.Sha256, diagnosticAuditSha256) ||
            !EqualsIgnoreCase(evidence.DiagnosticAuditSha256, diagnosticAuditSha256) ||
            !EqualsIgnoreCase(evidence.DiagnosticsPolicySha256, diagnosticsPolicySha256))
        {
            throw new InvalidOperationException("Canonical compilation reproduction evidence does not match its compiler manifest.");
        }

        if (!EqualsIgnoreCase(evidence.EvidenceSha256, ComputeEvidenceSha256(evidence)))
            throw new InvalidOperationException("Canonical compilation reproduction evidence failed its integrity check.");
    }

    private static string Serialize<T>(T value)
        => JsonSerializer.Serialize(value, new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            WriteIndented = false
        });

    private static string ComputeProviderContractsSha256(IEnumerable<PowerShellCompilationCommandProviderContract> providers)
        => ComputeTextSha256(Serialize(providers
            .OrderBy(static provider => provider.ProviderId, StringComparer.Ordinal)
            .ThenBy(static provider => provider.ProviderVersion, StringComparer.Ordinal)
            .ThenBy(static provider => provider.CommandName, StringComparer.Ordinal)
            .ToArray()));

    private static string ComputeEvidenceSha256(PowerShellCompilationReproductionEvidence evidence)
        => ComputeTextSha256(Serialize(new
        {
            evidence.SchemaVersion,
            evidence.Mode,
            evidence.Kind,
            evidence.Sources,
            evidence.CompilerVersion,
            evidence.CompilerSha256,
            evidence.SemanticProfileName,
            evidence.SemanticProfileVersion,
            evidence.TargetContractSha256,
            evidence.ProviderContractsSha256,
            evidence.DependencyLockSha256,
            evidence.GeneratedSourceSha256,
            evidence.PublicAbiSha256,
            evidence.SourceMapSha256,
            evidence.UnitDispositionLedgerSha256,
            evidence.DecisionTraceSha256,
            evidence.DiagnosticsSha256,
            evidence.IrSnapshotsSha256,
            evidence.FailureMapSha256,
            evidence.DiagnosticAuditSha256,
            evidence.DiagnosticsPolicySha256,
            evidence.DotNetSdkVersion,
            evidence.DotNetSdkSha256
        }));

    private static string ComputeDiagnosticsSha256(IEnumerable<PowerShellCompilationDiagnostic> diagnostics)
        => ComputeTextSha256(Serialize(diagnostics
            .OrderBy(static diagnostic => NormalizePath(diagnostic.FilePath), StringComparer.Ordinal)
            .ThenBy(static diagnostic => diagnostic.Line)
            .ThenBy(static diagnostic => diagnostic.Column)
            .ThenBy(static diagnostic => diagnostic.Code)
            .ThenBy(static diagnostic => diagnostic.FeatureId, StringComparer.Ordinal)
            .Select(static diagnostic => new
            {
                diagnostic.Code,
                diagnostic.FeatureId,
                FilePath = NormalizePath(diagnostic.FilePath),
                diagnostic.Line,
                diagnostic.Column,
                diagnostic.Message
            })
            .ToArray()));

    private static string GetPortableDiagnosticPath(
        string filePath,
        IReadOnlyDictionary<string, string> sourcePaths)
    {
        if (string.IsNullOrWhiteSpace(filePath)) return string.Empty;
        if (!Path.IsPathRooted(filePath)) return NormalizePath(filePath);
        var fullPath = Path.GetFullPath(filePath);
        return sourcePaths.TryGetValue(fullPath, out var relativePath)
            ? relativePath
            : NormalizePath(Path.GetFileName(fullPath));
    }

    private static string GetSourceMapSha256(IEnumerable<PowerShellCompilationArtifactFile> files)
        => files
            .Where(static file => file.Role.Equals("GeneratedSourceMap", StringComparison.Ordinal))
            .OrderBy(static file => NormalizePath(file.Path), StringComparer.Ordinal)
            .Select(static file => file.Sha256)
            .FirstOrDefault() ?? string.Empty;

    private static bool EqualsIgnoreCase(string? left, string? right)
        => string.Equals(left ?? string.Empty, right ?? string.Empty, StringComparison.OrdinalIgnoreCase);

    private static string NormalizeRelativePath(string path, string fallback)
    {
        var normalized = NormalizePath(path);
        return Path.IsPathRooted(normalized) || string.IsNullOrWhiteSpace(normalized)
            ? NormalizePath(fallback)
            : normalized;
    }

    private static string NormalizePath(string path) => (path ?? string.Empty).Replace('\\', '/');

    private static string ComputeFileSha256(string path)
    {
        using var stream = File.OpenRead(path);
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", string.Empty).ToLowerInvariant();
    }

    private static string ComputeTextSha256(string text)
    {
        using var sha = SHA256.Create();
        return BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(text))).Replace("-", string.Empty).ToLowerInvariant();
    }
}
