using NuGet.Packaging;
using NuGet.Versioning;

namespace PowerForge;

/// <summary>NuGet identity rules and the compiler's canonical stable three-part version policy.</summary>
internal static class PowerShellCompilationPackageIdentity
{
    internal static bool IsValidId(string? value)
        => value is not null && value.Length <= PackageIdValidator.MaxPackageIdLength &&
           PackageIdValidator.IsValidPackageId(value);

    internal static bool IsCanonicalVersion(string? value)
        => NuGetVersion.TryParse(value, out var version) &&
           !version.IsPrerelease && !version.HasMetadata && version.Version.Revision <= 0 &&
           string.Equals(value, version.ToNormalizedString(), StringComparison.Ordinal);
}
