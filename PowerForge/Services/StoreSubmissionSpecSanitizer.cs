namespace PowerForge;

internal static class StoreSubmissionSpecSanitizer
{
    public static StoreSubmissionSpec RedactSecrets(StoreSubmissionSpec spec)
    {
        if (spec is null)
            throw new ArgumentNullException(nameof(spec));

        var authentication = spec.Authentication ?? new StoreSubmissionAuthenticationOptions();

        return new StoreSubmissionSpec
        {
            Schema = spec.Schema,
            SchemaVersion = spec.SchemaVersion,
            Authentication = new StoreSubmissionAuthenticationOptions
            {
                SellerId = authentication.SellerId,
                TenantId = authentication.TenantId,
                ClientId = authentication.ClientId,
                ClientSecret = null,
                ClientSecretEnvVar = authentication.ClientSecretEnvVar,
                AccessToken = null,
                AccessTokenEnvVar = authentication.AccessTokenEnvVar,
                AuthorityHost = authentication.AuthorityHost,
                Resource = authentication.Resource,
                Scope = authentication.Scope
            },
            Targets = (spec.Targets ?? Array.Empty<StoreSubmissionTarget>())
                .Select(CloneTarget)
                .ToArray()
        };
    }

    private static StoreSubmissionTarget CloneTarget(StoreSubmissionTarget target)
    {
        return new StoreSubmissionTarget
        {
            Name = target.Name,
            Description = target.Description,
            Provider = target.Provider,
            ApplicationId = target.ApplicationId,
            SubmissionId = target.SubmissionId,
            SourceDirectory = target.SourceDirectory,
            RecurseSourceDirectory = target.RecurseSourceDirectory,
            PackagePatterns = (target.PackagePatterns ?? Array.Empty<string>()).ToArray(),
            PackagePaths = (target.PackagePaths ?? Array.Empty<string>()).ToArray(),
            ZipPath = target.ZipPath,
            Commit = target.Commit,
            WaitForCommit = target.WaitForCommit,
            PollIntervalSeconds = target.PollIntervalSeconds,
            PollTimeoutMinutes = target.PollTimeoutMinutes,
            MinimumDirectXVersion = target.MinimumDirectXVersion,
            MinimumSystemRam = target.MinimumSystemRam,
            DesktopPackages = (target.DesktopPackages ?? Array.Empty<StoreSubmissionDesktopPackage>())
                .Select(CloneDesktopPackage)
                .ToArray()
        };
    }

    private static StoreSubmissionDesktopPackage CloneDesktopPackage(StoreSubmissionDesktopPackage package)
    {
        return new StoreSubmissionDesktopPackage
        {
            PackageUrl = package.PackageUrl,
            Languages = (package.Languages ?? Array.Empty<string>()).ToArray(),
            Architectures = (package.Architectures ?? Array.Empty<string>()).ToArray(),
            IsSilentInstall = package.IsSilentInstall,
            InstallerParameters = package.InstallerParameters,
            GenericDocUrl = package.GenericDocUrl,
            PackageType = package.PackageType
        };
    }
}
