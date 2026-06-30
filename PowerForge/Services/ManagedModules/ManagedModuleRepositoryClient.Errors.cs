using System.Net;

namespace PowerForge;

public sealed partial class ManagedModuleRepositoryClient
{
    private static ManagedModuleRepositoryException CreateRepositoryHttpException(
        ManagedModuleRepository repository,
        string operation,
        HttpStatusCode statusCode,
        string detail)
        => new(
            operation,
            repository.Name,
            repository.Source,
            $"{detail} Repository returned {(int)statusCode} {statusCode}.",
            ResolveHttpRemediation(statusCode),
            (int)statusCode);

    private static ManagedModuleRepositoryException CreateRepositoryContractException(
        ManagedModuleRepository repository,
        string operation,
        string detail)
        => new(
            operation,
            repository.Name,
            repository.Source,
            detail,
            "Verify the repository source points to a NuGet v3 service index or compatible endpoint, then retry with Verbose output enabled.");

    private static ManagedModuleRepositoryException CreateRepositoryJsonException(
        ManagedModuleRepository repository,
        string operation,
        string detail,
        Exception innerException)
        => new(
            operation,
            repository.Name,
            repository.Source,
            detail,
            "Verify the repository endpoint returns valid NuGet v3 JSON and is not an HTML sign-in, proxy, or error page.",
            innerException: innerException);

    private static ManagedModuleRepositoryException CreateLocalRepositoryException(
        ManagedModuleRepository repository,
        string operation,
        string detail)
        => new(
            operation,
            repository.Name,
            repository.Source,
            detail,
            "Verify the local feed path exists and contains .nupkg files readable by the current process.");

    internal static bool IsRepositoryPackageNotFound(ManagedModuleRepositoryException exception)
        => exception.StatusCode == (int)HttpStatusCode.NotFound;

    private static string ResolveHttpRemediation(HttpStatusCode statusCode)
    {
        var numeric = (int)statusCode;
        if (statusCode is HttpStatusCode.Unauthorized or HttpStatusCode.Forbidden)
            return "Verify repository credentials, API token permissions, and profile authentication settings.";
        if (statusCode == HttpStatusCode.NotFound)
            return "Verify the repository URL, package id, package version, and whether prerelease versions are allowed.";
        if (statusCode == HttpStatusCode.Conflict)
            return "The package already exists or the repository rejected the duplicate. Use Force only when the target feed supports replacement.";
        if (statusCode == HttpStatusCode.RequestTimeout || statusCode == (HttpStatusCode)429 || numeric >= 500)
            return "Retry later or adjust repository retry/proxy settings; the feed returned a transient server-side response.";

        return "Verify repository URL, credentials, proxy/TLS settings, and provider compatibility.";
    }
}
