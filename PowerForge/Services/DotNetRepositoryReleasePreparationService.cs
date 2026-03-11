using System.Collections;

namespace PowerForge;

internal sealed class DotNetRepositoryReleasePreparationService
{
    public DotNetRepositoryReleasePreparedContext Prepare(DotNetRepositoryReleasePreparationRequest request)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));
        if (string.IsNullOrWhiteSpace(request.CurrentPath))
            throw new ArgumentException("Current path is required.", nameof(request));

        var rootPath = string.IsNullOrWhiteSpace(request.RootPath)
            ? request.CurrentPath
            : ProjectBuildSupportService.ResolveOptionalPath(request.RootPath, request.CurrentPath)!;

        var secret = ProjectBuildSupportService.ResolveSecret(
            request.NugetCredentialSecret,
            request.NugetCredentialSecretFilePath,
            request.NugetCredentialSecretEnvName,
            request.CurrentPath);
        var credential = (!string.IsNullOrWhiteSpace(request.NugetCredentialUserName) || !string.IsNullOrWhiteSpace(secret))
            ? new RepositoryCredential
            {
                UserName = string.IsNullOrWhiteSpace(request.NugetCredentialUserName) ? null : request.NugetCredentialUserName!.Trim(),
                Secret = secret
            }
            : null;

        var publishApiKey = ProjectBuildSupportService.ResolveSecret(
            request.PublishApiKey,
            request.PublishApiKeyFilePath,
            request.PublishApiKeyEnvName,
            request.CurrentPath);

        var expectedByProject = ParseExpectedVersionMap(request.ExpectedVersionMap);
        var mappedStore = request.CertificateStore == CertificateStoreLocation.LocalMachine
            ? PowerForge.CertificateStoreLocation.LocalMachine
            : PowerForge.CertificateStoreLocation.CurrentUser;

        return new DotNetRepositoryReleasePreparedContext
        {
            RootPath = rootPath,
            Spec = new DotNetRepositoryReleaseSpec
            {
                RootPath = rootPath,
                ExpectedVersion = request.ExpectedVersion,
                ExpectedVersionsByProject = expectedByProject.Count == 0 ? null : expectedByProject,
                ExpectedVersionMapAsInclude = request.ExpectedVersionMapAsInclude,
                ExpectedVersionMapUseWildcards = request.ExpectedVersionMapUseWildcards,
                IncludeProjects = request.IncludeProject,
                ExcludeProjects = request.ExcludeProject,
                ExcludeDirectories = request.ExcludeDirectories,
                VersionSources = request.NugetSource,
                VersionSourceCredential = credential,
                IncludePrerelease = request.IncludePrerelease,
                Configuration = string.IsNullOrWhiteSpace(request.Configuration) ? "Release" : request.Configuration,
                OutputPath = ProjectBuildSupportService.ResolveOptionalPath(request.OutputPath, rootPath),
                CertificateThumbprint = request.CertificateThumbprint,
                CertificateStore = mappedStore,
                TimeStampServer = request.TimeStampServer,
                Pack = !request.SkipPack,
                Publish = request.Publish,
                PublishSource = request.PublishSource,
                PublishApiKey = publishApiKey,
                SkipDuplicate = request.SkipDuplicate,
                PublishFailFast = request.PublishFailFast
            }
        };
    }

    private static Dictionary<string, string> ParseExpectedVersionMap(IDictionary? entries)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (entries is null || entries.Count == 0)
            return map;

        foreach (DictionaryEntry entry in entries)
        {
            var key = entry.Key?.ToString()?.Trim();
            var value = entry.Value?.ToString()?.Trim();

            if (string.IsNullOrWhiteSpace(key) || string.IsNullOrWhiteSpace(value))
                throw new ArgumentException("ExpectedVersionMap entries must include both project name and version.", nameof(entries));

            map[key!] = value!;
        }

        return map;
    }
}
