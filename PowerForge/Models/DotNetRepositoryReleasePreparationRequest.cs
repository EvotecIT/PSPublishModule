using System.Collections;

namespace PowerForge;

internal sealed class DotNetRepositoryReleasePreparationRequest
{
    public string CurrentPath { get; set; } = string.Empty;
    public string? RootPath { get; set; }
    public string? ExpectedVersion { get; set; }
    public IDictionary? ExpectedVersionMap { get; set; }
    public bool ExpectedVersionMapAsInclude { get; set; }
    public bool ExpectedVersionMapUseWildcards { get; set; }
    public string[]? IncludeProject { get; set; }
    public string[]? ExcludeProject { get; set; }
    public string[]? ExcludeDirectories { get; set; }
    public string[]? NugetSource { get; set; }
    public bool IncludePrerelease { get; set; }
    public string? NugetCredentialUserName { get; set; }
    public string? NugetCredentialSecret { get; set; }
    public string? NugetCredentialSecretFilePath { get; set; }
    public string? NugetCredentialSecretEnvName { get; set; }
    public string Configuration { get; set; } = "Release";
    public string? OutputPath { get; set; }
    public string? CertificateThumbprint { get; set; }
    public CertificateStoreLocation CertificateStore { get; set; } = CertificateStoreLocation.CurrentUser;
    public string? TimeStampServer { get; set; }
    public bool SkipPack { get; set; }
    public bool Publish { get; set; }
    public string? PublishSource { get; set; }
    public string? PublishApiKey { get; set; }
    public string? PublishApiKeyFilePath { get; set; }
    public string? PublishApiKeyEnvName { get; set; }
    public bool SkipDuplicate { get; set; }
    public bool PublishFailFast { get; set; }
}
