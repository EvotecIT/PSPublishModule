namespace PowerForge;

/// <summary>Versioned advertised, preview, and experimental compilation target matrix.</summary>
public sealed class PowerShellCompilationSupportMatrix
{
    /// <summary>Support-matrix schema version.</summary>
    public int SchemaVersion { get; set; } = 1;

    /// <summary>Toolchain maturity channel. Target support does not imply public package availability.</summary>
    public string ToolchainChannel { get; set; } = "Preview";

    /// <summary>Compatibility and servicing policy applied to promoted profiles.</summary>
    public string CompatibilityPolicy { get; set; } = string.Empty;

    /// <summary>Security response policy applied to supported profiles.</summary>
    public string SecurityPolicy { get; set; } = string.Empty;

    /// <summary>Exact qualified profiles.</summary>
    public PowerShellCompilationSupportProfile[] Profiles { get; set; } = Array.Empty<PowerShellCompilationSupportProfile>();
}

/// <summary>One exact artifact, semantic, framework, deployment, and runtime profile.</summary>
public sealed class PowerShellCompilationSupportProfile
{
    /// <summary>Stable qualified profile id.</summary>
    public string Id { get; set; } = string.Empty;

    /// <summary>Artifact shape.</summary>
    public PowerShellCompilationArtifactKind ArtifactKind { get; set; }

    /// <summary>Compilation mode.</summary>
    public PowerShellCompilationMode Mode { get; set; }

    /// <summary>Exact target framework.</summary>
    public string TargetFramework { get; set; } = string.Empty;

    /// <summary>Exact runtime identifier, empty for portable managed output.</summary>
    public string RuntimeIdentifier { get; set; } = string.Empty;

    /// <summary>Deployment model.</summary>
    public PowerShellCompilationDeploymentModel Deployment { get; set; }

    /// <summary>Supported, PortableManaged, or Experimental.</summary>
    public string SupportLevel { get; set; } = string.Empty;

    /// <summary>Preview or Experimental toolchain channel.</summary>
    public string Channel { get; set; } = string.Empty;

    /// <summary>Whether this exact profile is advertised for target-host use.</summary>
    public bool Advertised { get; set; }

    /// <summary>Runtime the target must provide.</summary>
    public PowerShellCompilationRuntimeRequirement RuntimeRequirement { get; set; }

    /// <summary>Evidence required to promote or retain the profile.</summary>
    public string[] RequiredEvidence { get; set; } = Array.Empty<string>();
}

/// <summary>Canonical support policy shared by target contracts and user-facing support output.</summary>
public static class PowerShellCompilationSupportMatrixService
{
    private static readonly HashSet<string> PromotedStrictExecutableRuntimeIdentifiers = new(StringComparer.OrdinalIgnoreCase)
    {
        "linux-x64",
        "win-x64"
    };

    private static readonly string[] CandidateRuntimeIdentifiers =
    {
        "linux-arm64", "linux-x64", "osx-arm64", "osx-x64", "win-arm64", "win-x64"
    };

    /// <summary>Evaluates one exact target using the same policy emitted by <see cref="Create"/>.</summary>
    public static string Evaluate(
        PowerShellCompilationArtifactKind kind,
        PowerShellCompilationMode mode,
        PowerShellCompilationDeploymentModel deployment,
        string targetFramework,
        string? runtimeIdentifier)
    {
        if (string.IsNullOrWhiteSpace(runtimeIdentifier)) return "PortableManaged";
        return kind == PowerShellCompilationArtifactKind.Executable &&
               mode == PowerShellCompilationMode.Strict &&
               targetFramework.Equals("net10.0", StringComparison.OrdinalIgnoreCase) &&
               deployment is PowerShellCompilationDeploymentModel.FrameworkDependent or PowerShellCompilationDeploymentModel.NativeAot &&
               PromotedStrictExecutableRuntimeIdentifiers.Contains(runtimeIdentifier!)
            ? "Supported"
            : "Experimental";
    }

    /// <summary>Creates the deterministic public support matrix without probing the build host.</summary>
    public static PowerShellCompilationSupportMatrix Create()
    {
        var profiles = new List<PowerShellCompilationSupportProfile>
        {
            Portable(PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Hybrid, "net8.0", PowerShellCompilationRuntimeRequirement.PowerShell),
            Portable(PowerShellCompilationArtifactKind.BinaryModule, PowerShellCompilationMode.Strict, "net8.0", PowerShellCompilationRuntimeRequirement.PowerShell),
            Portable(PowerShellCompilationArtifactKind.Library, PowerShellCompilationMode.Hybrid, "net8.0", PowerShellCompilationRuntimeRequirement.DotNet),
            Portable(PowerShellCompilationArtifactKind.Library, PowerShellCompilationMode.Strict, "net8.0", PowerShellCompilationRuntimeRequirement.DotNet),
            Portable(PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Package, "net8.0", PowerShellCompilationRuntimeRequirement.DotNet)
        };
        foreach (var rid in CandidateRuntimeIdentifiers)
        foreach (var deployment in new[]
                 {
                     PowerShellCompilationDeploymentModel.FrameworkDependent,
                     PowerShellCompilationDeploymentModel.SelfContained,
                     PowerShellCompilationDeploymentModel.Trimmed,
                     PowerShellCompilationDeploymentModel.NativeAot
                 })
        {
            var support = Evaluate(
                PowerShellCompilationArtifactKind.Executable,
                PowerShellCompilationMode.Strict,
                deployment,
                "net10.0",
                rid);
            profiles.Add(new PowerShellCompilationSupportProfile
            {
                Id = QualifiedId(PowerShellCompilationArtifactKind.Executable, PowerShellCompilationMode.Strict, "net10.0", rid, deployment),
                ArtifactKind = PowerShellCompilationArtifactKind.Executable,
                Mode = PowerShellCompilationMode.Strict,
                TargetFramework = "net10.0",
                RuntimeIdentifier = rid,
                Deployment = deployment,
                SupportLevel = support,
                Channel = support == "Supported" ? "Preview" : "Experimental",
                Advertised = support == "Supported",
                RuntimeRequirement = deployment == PowerShellCompilationDeploymentModel.FrameworkDependent
                    ? PowerShellCompilationRuntimeRequirement.DotNet
                    : PowerShellCompilationRuntimeRequirement.None,
                RequiredEvidence = ExactTargetEvidence()
            });
        }
        return new PowerShellCompilationSupportMatrix
        {
            CompatibilityPolicy = "Schema and semantic-profile changes are versioned; supported profiles receive compatibility fixes, while intentional breaks require a new schema or semantic profile and migration guidance.",
            SecurityPolicy = "Supported profiles fail closed on unreviewed locks or unverifiable closure. Security fixes may retire a profile when safe servicing cannot preserve its contract.",
            Profiles = profiles.OrderBy(static profile => profile.Id, StringComparer.Ordinal).ToArray()
        };
    }

    private static PowerShellCompilationSupportProfile Portable(
        PowerShellCompilationArtifactKind kind,
        PowerShellCompilationMode mode,
        string framework,
        PowerShellCompilationRuntimeRequirement runtimeRequirement)
        => new()
        {
            Id = QualifiedId(kind, mode, framework, "portable", PowerShellCompilationDeploymentModel.FrameworkDependent),
            ArtifactKind = kind,
            Mode = mode,
            TargetFramework = framework,
            Deployment = PowerShellCompilationDeploymentModel.FrameworkDependent,
            SupportLevel = "PortableManaged",
            Channel = "Preview",
            Advertised = true,
            RuntimeRequirement = runtimeRequirement,
            RequiredEvidence = new[] { "semantic-oracle", "dependency-lock", "clean-import-or-execution", "package-install" }
        };

    private static string QualifiedId(
        PowerShellCompilationArtifactKind kind,
        PowerShellCompilationMode mode,
        string framework,
        string rid,
        PowerShellCompilationDeploymentModel deployment)
        => $"{kind}-{mode}-{framework}-{rid}-{deployment}".ToLowerInvariant();

    private static string[] ExactTargetEvidence()
        => new[] { "semantic-oracle", "dependency-lock", "target-host-execution", "package-install", "performance-budget" };
}
