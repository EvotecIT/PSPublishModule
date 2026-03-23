namespace PowerForge;

internal static class StoreSubmissionSpecSanitizer
{
    public static StoreSubmissionSpec RedactSecrets(StoreSubmissionSpec spec)
    {
        if (spec is null)
            throw new ArgumentNullException(nameof(spec));

        return new StoreSubmissionSpec
        {
            Schema = spec.Schema,
            SchemaVersion = spec.SchemaVersion,
            Authentication = new StoreSubmissionAuthenticationOptions
            {
                SellerId = spec.Authentication.SellerId,
                TenantId = spec.Authentication.TenantId,
                ClientId = spec.Authentication.ClientId,
                ClientSecret = null,
                ClientSecretEnvVar = spec.Authentication.ClientSecretEnvVar,
                AccessToken = null,
                AccessTokenEnvVar = spec.Authentication.AccessTokenEnvVar,
                AuthorityHost = spec.Authentication.AuthorityHost,
                Resource = spec.Authentication.Resource,
                Scope = spec.Authentication.Scope
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
