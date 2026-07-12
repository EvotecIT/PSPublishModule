using System.Net;
using System.Net.Http;
using System.Text;

namespace PowerForge;

public sealed partial class ManagedModuleRepositoryClient
{
    private const int MaximumRepositoryResponseDetailLength = 2048;
    private const int MaximumRepositoryResponseBodyBytes = MaximumRepositoryResponseDetailLength * 4;

    private static ManagedModuleRepositoryException CreateRepositoryHttpException(
        ManagedModuleRepository repository,
        string operation,
        HttpStatusCode statusCode,
        string detail,
        string? responseDetail = null)
        => new(
            operation,
            repository.Name,
            repository.Source,
            string.IsNullOrWhiteSpace(responseDetail)
                ? $"{detail} Repository returned {(int)statusCode} {statusCode}."
                : $"{detail} Repository returned {(int)statusCode} {statusCode}. Repository response: {responseDetail}",
            ResolveHttpRemediation(statusCode),
            (int)statusCode);

    private static async Task<ManagedModuleRepositoryException> CreateRepositoryHttpExceptionAsync(
        ManagedModuleRepository repository,
        string operation,
        HttpResponseMessage response,
        string detail,
        RepositoryCredential? credential,
        CancellationToken cancellationToken)
    {
        var responseDetail = await ReadRepositoryResponseDetailAsync(response, credential, cancellationToken).ConfigureAwait(false);
        return CreateRepositoryHttpException(repository, operation, response.StatusCode, detail, responseDetail);
    }

    private static async Task<string?> ReadRepositoryResponseDetailAsync(
        HttpResponseMessage response,
        RepositoryCredential? credential,
        CancellationToken cancellationToken)
    {
        var values = new List<string>();
        if (!string.IsNullOrWhiteSpace(response.ReasonPhrase))
            values.Add(response.ReasonPhrase!);

        if (response.Content is not null)
        {
            try
            {
                var body = await ReadRepositoryResponseBodyAsync(response.Content, cancellationToken).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(body))
                    values.Add(body!);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or ObjectDisposedException or InvalidOperationException)
            {
                // Response diagnostics are best effort and must not replace the repository failure.
            }
        }

        if (values.Count == 0)
            return null;

        var normalized = NormalizeRepositoryResponseDetail(string.Join(" ", values), credential);
        if (normalized.Length <= MaximumRepositoryResponseDetailLength)
            return normalized;

        return normalized.Substring(0, MaximumRepositoryResponseDetailLength) + "...";
    }

    private static async Task<string?> ReadRepositoryResponseBodyAsync(
        HttpContent content,
        CancellationToken cancellationToken)
    {
        using var stream = await ReadContentStreamAsync(content, cancellationToken).ConfigureAwait(false);
        var buffer = new byte[MaximumRepositoryResponseBodyBytes];
        var bytesRead = 0;
        while (bytesRead < buffer.Length)
        {
            var read = await stream.ReadAsync(
                    buffer,
                    bytesRead,
                    buffer.Length - bytesRead,
                    cancellationToken)
                .ConfigureAwait(false);
            if (read == 0)
                break;

            bytesRead += read;
        }

        if (bytesRead == 0)
            return null;

        var encoding = ResolveRepositoryResponseEncoding(content);
        return encoding.GetString(buffer, 0, bytesRead);
    }

    private static Encoding ResolveRepositoryResponseEncoding(HttpContent content)
    {
        var charset = content.Headers.ContentType?.CharSet;
        if (!string.IsNullOrWhiteSpace(charset))
        {
            charset = charset!.Trim().Trim('"');
            try
            {
                return Encoding.GetEncoding(charset);
            }
            catch (ArgumentException)
            {
                // Fall back to UTF-8 when the repository declares an invalid charset.
            }
        }

        return Encoding.UTF8;
    }

    private static string NormalizeRepositoryResponseDetail(string value, RepositoryCredential? credential)
    {
        var secret = credential?.Secret;
        if (!string.IsNullOrEmpty(secret))
        {
            value = value.Replace(secret!, "[REDACTED]");
            var userName = credential?.UserName;
            if (!string.IsNullOrWhiteSpace(userName))
            {
                var basicBytes = Encoding.ASCII.GetBytes($"{userName}:{secret}");
                var encodedBasicCredential = Convert.ToBase64String(basicBytes);
                value = value.Replace(encodedBasicCredential, "[REDACTED]");
            }
        }

        var builder = new StringBuilder(value.Length);
        var previousWasWhitespace = false;
        foreach (var character in value)
        {
            if (char.IsWhiteSpace(character) || char.IsControl(character))
            {
                if (!previousWasWhitespace)
                    builder.Append(' ');
                previousWasWhitespace = true;
                continue;
            }

            builder.Append(character);
            previousWasWhitespace = false;
        }

        return builder.ToString().Trim();
    }

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
