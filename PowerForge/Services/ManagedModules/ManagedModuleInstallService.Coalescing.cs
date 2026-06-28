namespace PowerForge;

public sealed partial class ManagedModuleInstallService
{
    internal static string? TryCreateInstallCoalescingKey(
        ManagedModuleInstallRequest request,
        string version,
        string moduleRoot)
    {
        if (request.Force || request.Credential is not null)
            return null;

        return string.Join(
            "|",
            NormalizeCoalescingValue(moduleRoot),
            NormalizeCoalescingValue(request.Name),
            NormalizeCoalescingValue(version),
            request.Repository.Kind.ToString(),
            NormalizeCoalescingValue(request.Repository.Name),
            NormalizeRepositorySource(request.Repository.Source),
            request.IncludePrerelease ? "pre" : "stable",
            request.AllowClobber ? "clobber" : "no-clobber",
            request.AcceptLicense ? "license" : "no-license",
            request.AuthenticodeCheck ? "authenticode" : "no-authenticode",
            NormalizeCoalescingValue(request.ExpectedPackageSha256),
            FingerprintTrustPolicy(request.TrustPolicy),
            request.SkipDependencyCheck ? "skip-deps" : "deps");
    }

    private static string FingerprintTrustPolicy(ManagedModuleTrustPolicy? trustPolicy)
    {
        if (trustPolicy is null || !ManagedModuleTrustEvaluator.HasPolicy(trustPolicy))
            return "trust:none";

        var authors = ManagedModuleTrustEvaluator.NormalizeAuthors(trustPolicy.AllowedAuthors)
            .OrderBy(static author => author, StringComparer.OrdinalIgnoreCase);
        return string.Join(
            ";",
            "trust",
            trustPolicy.RequireTrustedRepository ? "trusted-repository" : "repository-any",
            trustPolicy.ApplyToDependencies ? "dependency-policy" : "root-policy",
            string.Join(",", authors.Select(NormalizeCoalescingValue)));
    }

    private static string NormalizeRepositorySource(string source)
    {
        var trimmed = NormalizeCoalescingValue(source);
        if (Path.IsPathRooted(trimmed))
            return Path.GetFullPath(trimmed).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);

        return trimmed.TrimEnd('/', '\\');
    }

    private static string NormalizeCoalescingValue(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? string.Empty
            : value.Trim().Trim('"');
}
