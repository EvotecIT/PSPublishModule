using System.Security.Cryptography;
using System.Text;

namespace PowerForge;

/// <summary>Execution environment required by a compiled PowerShell artifact.</summary>
public enum PowerShellCompilationRuntimeRequirement
{
    /// <summary>The target host must provide PowerShell.</summary>
    PowerShell,
    /// <summary>The target host must provide the selected .NET runtime.</summary>
    DotNet,
    /// <summary>The artifact carries every managed runtime component required to start.</summary>
    None
}

/// <summary>Deployment shape selected for one explicit compiler target.</summary>
public enum PowerShellCompilationDeploymentModel
{
    /// <summary>A managed artifact that uses an installed runtime.</summary>
    FrameworkDependent,
    /// <summary>A managed artifact that carries the .NET runtime.</summary>
    SelfContained,
    /// <summary>A self-contained single-file artifact with trimming enabled.</summary>
    Trimmed,
    /// <summary>An experimental ReadyToRun publication used only for measurement.</summary>
    ReadyToRun,
    /// <summary>A native executable produced from the runtime-free managed backend.</summary>
    NativeAot
}

/// <summary>
/// Versioned semantic, execution, and deployment target consumed by artifact generation.
/// </summary>
public sealed class PowerShellCompilationTargetContract
{
    /// <summary>Target-contract schema version.</summary>
    public int SchemaVersion { get; set; } = 2;

    /// <summary>Artifact shape governed by this target.</summary>
    public PowerShellCompilationArtifactKind ArtifactKind { get; set; }

    /// <summary>Compilation/fallback mode governed by this target.</summary>
    public PowerShellCompilationMode Mode { get; set; }

    /// <summary>Exact target framework.</summary>
    public string TargetFramework { get; set; } = string.Empty;

    /// <summary>Exact runtime identifier, or empty for portable managed output.</summary>
    public string RuntimeIdentifier { get; set; } = string.Empty;

    /// <summary>Normalized target operating-system family.</summary>
    public string OperatingSystem { get; set; } = string.Empty;

    /// <summary>Normalized target processor architecture.</summary>
    public string Architecture { get; set; } = string.Empty;

    /// <summary>Runtime that must be present outside the artifact.</summary>
    public PowerShellCompilationRuntimeRequirement RuntimeRequirement { get; set; }

    /// <summary>Managed/native deployment shape.</summary>
    public PowerShellCompilationDeploymentModel Deployment { get; set; }

    /// <summary>Whether one file is the requested executable payload.</summary>
    public bool SingleFile { get; set; }

    /// <summary>Whether the generated program may execute authored PowerShell source.</summary>
    public bool AllowsPowerShellRuntimeEvaluation { get; set; }

    /// <summary>Whether the caller supplied this contract explicitly instead of using the compatibility projection. This request provenance is not part of canonical target identity.</summary>
    public bool Explicit { get; set; }

    /// <summary>Support state based on target-host execution evidence.</summary>
    public string SupportLevel { get; set; } = "Experimental";

    /// <summary>Canonical SHA-256 of this target contract.</summary>
    public string ContractSha256 { get; set; } = string.Empty;
}

/// <summary>Exact compiler and SDK identity captured for one artifact build.</summary>
public sealed class PowerShellCompilationToolchainEvidence
{
    /// <summary>Evidence schema version.</summary>
    public int SchemaVersion { get; set; } = 2;

    /// <summary>SDK selected by the generated dotnet build.</summary>
    public string DotNetSdkVersion { get; set; } = string.Empty;

    /// <summary>SHA-256 over the exact selected SDK directory and its relative file identities.</summary>
    public string DotNetSdkSha256 { get; set; } = string.Empty;

    /// <summary>PowerForge assembly version that owns compilation.</summary>
    public string CompilerVersion { get; set; } = string.Empty;

    /// <summary>SHA-256 of the exact compiler assembly that generated the project.</summary>
    public string CompilerSha256 { get; set; } = string.Empty;

    /// <summary>Build-host operating-system description.</summary>
    public string BuildOperatingSystem { get; set; } = string.Empty;

    /// <summary>Build-host process architecture.</summary>
    public string BuildArchitecture { get; set; } = string.Empty;

    /// <summary>Exact target-contract hash consumed by the build.</summary>
    public string TargetContractSha256 { get; set; } = string.Empty;

    /// <summary>Exact dependency-lock hash consumed by the build.</summary>
    public string DependencyLockSha256 { get; set; } = string.Empty;
}

/// <summary>Content-addressed generated-build cache evidence.</summary>
public sealed class PowerShellCompilationBuildCacheEvidence
{
    /// <summary>Cache evidence schema version.</summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Canonical cache key over source, compiler, target, lock, and toolchain inputs.</summary>
    public string Key { get; set; } = string.Empty;

    /// <summary>Whether a complete verified entry supplied the generated build output.</summary>
    public bool Hit { get; set; }

    /// <summary>Stable explanation for a miss or bypass.</summary>
    public string Reason { get; set; } = string.Empty;
}

/// <summary>Canonical target-contract construction and validation.</summary>
public static class PowerShellCompilationTargetContractService
{
    /// <summary>Creates the target implied by compatibility build fields.</summary>
    public static PowerShellCompilationTargetContract Create(
        PowerShellCompilationArtifactKind kind,
        PowerShellCompilationMode mode,
        string targetFramework,
        string? runtimeIdentifier,
        bool selfContained,
        bool singleFile,
        PowerShellCompilationExecutableOptimization optimization,
        bool explicitContract)
    {
        var rid = runtimeIdentifier?.Trim() ?? string.Empty;
        var deployment = optimization switch
        {
            PowerShellCompilationExecutableOptimization.Trimmed => PowerShellCompilationDeploymentModel.Trimmed,
            PowerShellCompilationExecutableOptimization.NativeAot => PowerShellCompilationDeploymentModel.NativeAot,
            _ when selfContained => PowerShellCompilationDeploymentModel.SelfContained,
            _ => PowerShellCompilationDeploymentModel.FrameworkDependent
        };
        var allowsPowerShellRuntimeEvaluation = kind == PowerShellCompilationArtifactKind.BinaryModule
            || kind == PowerShellCompilationArtifactKind.Executable && mode != PowerShellCompilationMode.Strict;
        var runtimeRequirement = kind == PowerShellCompilationArtifactKind.BinaryModule
            ? PowerShellCompilationRuntimeRequirement.PowerShell
            : deployment is PowerShellCompilationDeploymentModel.SelfContained
                or PowerShellCompilationDeploymentModel.Trimmed
                or PowerShellCompilationDeploymentModel.NativeAot
                ? PowerShellCompilationRuntimeRequirement.None
                : PowerShellCompilationRuntimeRequirement.DotNet;
        var contract = new PowerShellCompilationTargetContract
        {
            ArtifactKind = kind,
            Mode = mode,
            TargetFramework = targetFramework?.Trim() ?? string.Empty,
            RuntimeIdentifier = rid,
            OperatingSystem = GetRidPart(rid, 0),
            Architecture = GetRidPart(rid, -1),
            RuntimeRequirement = runtimeRequirement,
            Deployment = deployment,
            SingleFile = kind == PowerShellCompilationArtifactKind.Executable && singleFile,
            AllowsPowerShellRuntimeEvaluation = allowsPowerShellRuntimeEvaluation,
            Explicit = explicitContract,
            SupportLevel = GetSupportLevel(rid)
        };
        contract.ContractSha256 = ComputeSha256(contract);
        return contract;
    }

    /// <summary>Normalizes and verifies a caller-supplied target contract.</summary>
    public static PowerShellCompilationTargetContract Normalize(PowerShellCompilationTargetContract contract)
    {
        if (contract is null) throw new ArgumentNullException(nameof(contract));
        if (contract.SchemaVersion is not 1 and not 2) throw new InvalidOperationException($"Unsupported PowerShell compilation target-contract schema {contract.SchemaVersion}.");
        contract.TargetFramework = contract.TargetFramework?.Trim() ?? string.Empty;
        contract.RuntimeIdentifier = contract.RuntimeIdentifier?.Trim() ?? string.Empty;
        contract.OperatingSystem = contract.OperatingSystem?.Trim() ?? string.Empty;
        contract.Architecture = contract.Architecture?.Trim() ?? string.Empty;
        var expectedOperatingSystem = GetRidPart(contract.RuntimeIdentifier, 0);
        var expectedArchitecture = GetRidPart(contract.RuntimeIdentifier, -1);
        var expectedSupportLevel = GetSupportLevel(contract.RuntimeIdentifier);
        if (!contract.OperatingSystem.Equals(expectedOperatingSystem, StringComparison.OrdinalIgnoreCase) ||
            !contract.Architecture.Equals(expectedArchitecture, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("PowerShell compilation target operating system or architecture conflicts with its runtime identifier.");
        if (!contract.SupportLevel.Equals(expectedSupportLevel, StringComparison.Ordinal))
            throw new InvalidOperationException("PowerShell compilation target support level conflicts with its runtime identifier.");
        contract.OperatingSystem = expectedOperatingSystem;
        contract.Architecture = expectedArchitecture;
        contract.SupportLevel = expectedSupportLevel;
        var suppliedHash = contract.ContractSha256;
        contract.ContractSha256 = string.Empty;
        var actual = ComputeSha256(contract);
        if (!string.IsNullOrWhiteSpace(suppliedHash) && !suppliedHash.Equals(actual, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("PowerShell compilation target contract does not match its recorded SHA-256.");
        contract.SchemaVersion = 2;
        contract.Explicit = true;
        contract.ContractSha256 = ComputeSha256(contract);
        return contract;
    }

    private static string GetSupportLevel(string? runtimeIdentifier)
        => string.IsNullOrWhiteSpace(runtimeIdentifier) ? "PortableManaged" : "Experimental";

    /// <summary>Computes the canonical target-contract hash.</summary>
    public static string ComputeSha256(PowerShellCompilationTargetContract contract)
    {
        if (contract is null) throw new ArgumentNullException(nameof(contract));
        if (contract.SchemaVersion is not 1 and not 2)
            throw new InvalidOperationException($"Unsupported PowerShell compilation target-contract schema {contract.SchemaVersion}.");
        var values = new List<object>
        {
            contract.SchemaVersion, contract.ArtifactKind, contract.Mode, contract.TargetFramework,
            contract.RuntimeIdentifier, contract.OperatingSystem, contract.Architecture,
            contract.RuntimeRequirement, contract.Deployment, contract.SingleFile,
            contract.AllowsPowerShellRuntimeEvaluation
        };
        if (contract.SchemaVersion == 1) values.Add(contract.Explicit);
        values.Add(contract.SupportLevel);
        var text = new StringBuilder();
        foreach (var value in values)
        {
            var item = value?.ToString() ?? string.Empty;
            text.Append(item.Length).Append(':').Append(item);
        }
        using var sha = SHA256.Create();
        return string.Concat(sha.ComputeHash(Encoding.UTF8.GetBytes(text.ToString()))
            .Select(static value => value.ToString("x2", System.Globalization.CultureInfo.InvariantCulture)));
    }

    private static string GetRidPart(string runtimeIdentifier, int index)
    {
        if (string.IsNullOrWhiteSpace(runtimeIdentifier)) return string.Empty;
        var parts = runtimeIdentifier.Split('-');
        var selected = index < 0 ? parts.Length + index : index;
        return selected >= 0 && selected < parts.Length ? parts[selected] : string.Empty;
    }
}
