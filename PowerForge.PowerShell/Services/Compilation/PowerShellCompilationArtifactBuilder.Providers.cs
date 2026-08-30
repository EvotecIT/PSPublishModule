namespace PowerForge;

public sealed partial class PowerShellCompilationArtifactBuilder
{
    private static PowerShellCompilationProviderResolution ResolveProviderPackages(PowerShellCompilationBuildSpec spec)
    {
        var references = spec.ProviderPackages ?? Array.Empty<PowerShellCompilationProviderPackageReference>();
        if (references.Length == 0)
        {
            if (spec.ExpectedProviderLock is { Packages.Length: > 0 })
                throw new InvalidOperationException("A reviewed provider lock was supplied, but no provider packages were selected.");
            return new PowerShellCompilationProviderPackageReader().Resolve(Array.Empty<PowerShellCompilationProviderPackageReference>());
        }
        if (spec.ExpectedProviderLock is null && !spec.AllowUnreviewedProviderResolution)
            throw new InvalidOperationException(
                "PowerShell compilation requires a separately reviewed provider lock. Supply ExpectedProviderLock from non-executing provider resolution, or explicitly set AllowUnreviewedProviderResolution for a development build.");
        if (spec.ExpectedProviderLock is not null && spec.AllowUnreviewedProviderResolution)
            throw new ArgumentException("ExpectedProviderLock and AllowUnreviewedProviderResolution are mutually exclusive.", nameof(spec));

        var resolution = new PowerShellCompilationProviderPackageReader().Resolve(
            references,
            spec.ProviderTrustPolicy ?? new PowerShellCompilationProviderTrustPolicy());
        if (spec.ExpectedProviderLock is not null)
            PowerShellCompilationProviderPackageReader.EnsureMatches(spec.ExpectedProviderLock, resolution.Lock);
        return resolution;
    }
}
