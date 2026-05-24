using System;
using System.Text.Json;
using PowerForge;
using PowerForge.Web;

namespace PowerForge.Web.Cli;

internal static partial class WebPipelineRunner
{
    private static void ExecutePrivateGalleryIndex(JsonElement step, string baseDir, WebPipelineStepResult stepResult)
    {
        var outputDirectory = ResolvePath(baseDir,
            GetString(step, "out") ??
            GetString(step, "output") ??
            GetString(step, "outputDirectory") ??
            GetString(step, "output-directory") ??
            "./data/private-gallery");
        if (string.IsNullOrWhiteSpace(outputDirectory))
            throw new InvalidOperationException("private-gallery-index requires out/outputDirectory.");

        var provider = GetString(step, "provider") ?? "azure-artifacts";
        if (!provider.Equals("azure-artifacts", StringComparison.OrdinalIgnoreCase) &&
            !provider.Equals("azureArtifacts", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException($"private-gallery-index provider '{provider}' is not supported.");
        }

        var token = GetString(step, "token");
        var tokenEnv = GetString(step, "tokenEnv") ??
                       GetString(step, "token-env") ??
                       GetString(step, "tokenEnvironmentVariable") ??
                       GetString(step, "token-environment-variable");
        var authentication = ParseAuthenticationKind(
            GetString(step, "authentication") ??
            GetString(step, "authenticationKind") ??
            GetString(step, "authentication-kind") ??
            GetString(step, "auth") ??
            GetString(step, "authKind") ??
            GetString(step, "auth-kind"));

        var result = WebPrivateGalleryGenerator.Generate(new WebPrivateGalleryOptions
        {
            OutputDirectory = outputDirectory,
            BaseDirectory = baseDir,
            Title = GetString(step, "title"),
            Organization = GetString(step, "organization") ?? GetString(step, "azureDevOpsOrganization") ?? GetString(step, "azure-devops-organization"),
            Project = GetString(step, "project") ?? GetString(step, "azureDevOpsProject") ?? GetString(step, "azure-devops-project"),
            Feed = GetString(step, "feed") ?? GetString(step, "azureArtifactsFeed") ?? GetString(step, "azure-artifacts-feed"),
            RepositoryName = GetString(step, "repositoryName") ?? GetString(step, "repository-name"),
            IncludeAllVersions = GetBool(step, "includeAllVersions") ?? GetBool(step, "include-all-versions") ?? true,
            IncludePackageContent = GetBool(step, "includePackageContent") ?? GetBool(step, "include-package-content") ?? false,
            IncludeMetrics = GetBool(step, "includeMetrics") ?? GetBool(step, "include-metrics") ?? false,
            MaxPackages = GetInt(step, "maxPackages") ?? GetInt(step, "max-packages") ?? 500,
            MaxVersionsPerPackage = GetInt(step, "maxVersionsPerPackage") ?? GetInt(step, "max-versions-per-package") ?? 1,
            MaxDocumentContentBytes = GetInt(step, "maxDocumentContentBytes") ??
                                      GetInt(step, "max-document-content-bytes") ??
                                      GetInt(step, "maxPackageDocumentBytes") ??
                                      GetInt(step, "max-package-document-bytes") ??
                                      262144,
            RequestTimeoutSeconds = GetInt(step, "timeoutSeconds") ?? GetInt(step, "timeout-seconds") ?? 30,
            Token = token,
            TokenEnvironmentVariable = tokenEnv,
            AuthenticationKind = authentication,
            TempDirectory = ResolvePath(baseDir, GetString(step, "tempDirectory") ?? GetString(step, "temp-directory"))
        });

        var warningNote = result.Warnings.Length > 0
            ? $"; warnings={result.Warnings.Length}"
            : string.Empty;
        stepResult.Success = true;
        stepResult.Message = $"private-gallery-index ok: packages={result.PackageCount}; versions={result.VersionCount}; commands={result.CommandCount}; feed={result.FeedPath}; search={result.SearchPath}{warningNote}";
    }

    private static PrivateGalleryAuthenticationKind ParseAuthenticationKind(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return PrivateGalleryAuthenticationKind.Bearer;

        var normalized = value.Trim();
        if (normalized.Equals("none", StringComparison.OrdinalIgnoreCase))
            return PrivateGalleryAuthenticationKind.None;
        if (normalized.Equals("bearer", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("oauth", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("oauth2", StringComparison.OrdinalIgnoreCase))
            return PrivateGalleryAuthenticationKind.Bearer;
        if (normalized.Equals("basic", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("basic-token", StringComparison.OrdinalIgnoreCase) ||
            normalized.Equals("pat", StringComparison.OrdinalIgnoreCase))
            return PrivateGalleryAuthenticationKind.BasicToken;

        throw new InvalidOperationException($"Unsupported private gallery authentication kind '{value}'.");
    }
}
