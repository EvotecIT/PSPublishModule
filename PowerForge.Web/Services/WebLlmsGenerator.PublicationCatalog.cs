namespace PowerForge.Web;

public static partial class WebLlmsGenerator
{
    private static int ApplyInstallCommandPolicy(
        WebLlmsOptions options,
        IReadOnlyList<PackageInfo> packages,
        string? packageId,
        string? version,
        bool projectIsPowerShellModule,
        bool projectIsDotNetTool,
        List<string> warnings,
        ref string? legacyInstallCommand)
    {
        if (options.ContentKind != WebLlmsContentKind.Package)
            return 0;

        if (options.InstallCommandPolicy == WebLlmsInstallCommandPolicy.None)
        {
            legacyInstallCommand = null;
            foreach (var package in packages)
                package.InstallCommand = null;
            return 0;
        }

        if (options.InstallCommandPolicy == WebLlmsInstallCommandPolicy.Declared)
            return packages.Count == 0
                ? string.IsNullOrWhiteSpace(legacyInstallCommand) ? 0 : 1
                : packages.Count(HasInstallCommand);

        if (string.IsNullOrWhiteSpace(options.PublicationCatalogPath))
            throw new InvalidOperationException(
                "VerifiedCatalog install policy requires publicationCatalogPath.");
        var catalog = WebPublicationCatalog.Load(
            options.PublicationCatalogPath,
            options.PublicationCatalogMaxAgeHours,
            "LLMS");

        if (packages.Count == 0)
        {
            var failure = string.IsNullOrWhiteSpace(packageId) ? "Package identifier is unavailable." :
                ReadVerificationFailure(catalog, packageId, version, projectIsPowerShellModule, options);
            if (failure is not null)
            {
                legacyInstallCommand = null;
                warnings.Add(InstallOmissionMessage(catalog, packageId ?? string.Empty, version, projectIsPowerShellModule, failure));
            }
            else
            {
                legacyInstallCommand = CreateInstallCommand(
                    packageId!,
                    projectIsPowerShellModule,
                    projectIsDotNetTool,
                    version);
            }
            return string.IsNullOrWhiteSpace(legacyInstallCommand) ? 0 : 1;
        }

        foreach (var package in packages)
        {
            var failure = ReadVerificationFailure(catalog, package.Id, package.Version, package.IsPowerShellModule, options);
            if (failure is not null)
            {
                package.InstallCommand = null;
                warnings.Add(InstallOmissionMessage(catalog, package.Id, package.Version, package.IsPowerShellModule, failure));
            }
            else
                package.InstallCommand = CreateInstallCommand(
                    package.Id,
                    package.IsPowerShellModule,
                    package.IsDotNetTool,
                    package.Version);
        }

        return packages.Count(HasInstallCommand);
    }

    private static string InstallOmissionMessage(WebPublicationCatalog catalog, string id, string? version, bool isModule, string failure)
    {
        var observed = catalog.GetPackageVersion(id, isModule);
        var registry = isModule ? "powershellgallery" : "nuget";
        return $"VerifiedCatalog omitted installation for '{registry}:{id}': source version '{version ?? "unknown"}', " +
               $"catalog version '{observed ?? "not found"}'. {failure}";
    }

    private static string? ReadVerificationFailure(
        WebPublicationCatalog catalog,
        string packageId,
        string? expectedVersion,
        bool isPowerShellModule,
        WebLlmsOptions options)
        => catalog.GetPackageVerificationFailure(
            isPowerShellModule ? "powershellgallery" : "nuget",
            packageId,
            expectedVersion,
            isPowerShellModule ? options.PowerShellGalleryOwner : options.NuGetOwner);
}
