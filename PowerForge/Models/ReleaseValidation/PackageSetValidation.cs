namespace PowerForge;

/// <summary>Expected identities, contents, dependencies and signatures of a staged NuGet set.</summary>
public sealed class PackageSetValidation
{
    /// <summary>Directory containing packages. Supports caller variables such as {PackageRoot}.</summary>
    public string Path { get; set; } = "{PackageRoot}";
    /// <summary>Require the directory to contain exactly the declared non-symbol packages.</summary>
    public bool ExactSet { get; set; } = true;
    /// <summary>Require all declared packages to share the release version and pass it as PackageVersion to consumers.
    /// When false, consumer projects select individual versions; validation still requires the exact staged archives.</summary>
    public bool SameVersion { get; set; } = true;
    /// <summary>Use NuGet to verify package signatures.</summary>
    public bool VerifySignatures { get; set; }
    /// <summary>Require an author signature, rather than only a repository signature.</summary>
    public bool RequireAuthorSignature { get; set; }
    /// <summary>Allowed author certificate SHA-256 fingerprints; empty accepts any trusted author.</summary>
    public string[] AuthorCertificateFingerprints { get; set; } = Array.Empty<string>();
    /// <summary>Product-specific package contracts.</summary>
    public PackageArtifactContract[] Items { get; set; } = Array.Empty<PackageArtifactContract>();
}

/// <summary>Declarative requirements for one NuGet package.</summary>
public sealed class PackageArtifactContract
{
    /// <summary>Expected package identity.</summary>
    public string Id { get; set; } = string.Empty;
    /// <summary>Required archive paths, supporting * and ? wildcards.</summary>
    public string[] RequiredEntries { get; set; } = Array.Empty<string>();
    /// <summary>Archive paths that must not be present.</summary>
    public string[] ForbiddenEntries { get; set; } = Array.Empty<string>();
    /// <summary>Required entries in the matching .snupkg. Nonempty requires the symbol package.</summary>
    public string[] SymbolEntries { get; set; } = Array.Empty<string>();
    /// <summary>Forbidden entries in the matching symbol package.</summary>
    public string[] ForbiddenSymbolEntries { get; set; } = Array.Empty<string>();
    /// <summary>Expected dependency-group frameworks, using NuGet framework names.</summary>
    public string[] DependencyFrameworks { get; set; } = Array.Empty<string>();
    /// <summary>Required dependency identities in every dependency group.</summary>
    public string[] RequiredDependencies { get; set; } = Array.Empty<string>();
    /// <summary>Dependency identities forbidden in every group.</summary>
    public string[] ForbiddenDependencies { get; set; } = Array.Empty<string>();
    /// <summary>Dependency identities that must exclude compile assets in every group.</summary>
    public string[] RuntimeOnlyDependencies { get; set; } = Array.Empty<string>();
}
