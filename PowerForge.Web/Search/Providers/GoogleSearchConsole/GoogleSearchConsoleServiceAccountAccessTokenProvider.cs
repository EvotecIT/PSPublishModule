using Google.Apis.Auth.OAuth2;
using System.Security.Cryptography;
using System.Text.Json;

namespace PowerForge.Web;

/// <summary>Loads an explicitly configured Google service account and obtains read-only Search Console OAuth tokens.</summary>
public sealed class GoogleSearchConsoleServiceAccountAccessTokenProvider : IGoogleSearchConsoleAccessTokenProvider
{
    private const string ReadOnlyScope = "https://www.googleapis.com/auth/webmasters.readonly";
    private readonly ITokenAccess _credential;

    private GoogleSearchConsoleServiceAccountAccessTokenProvider(ServiceAccountCredential serviceAccountCredential)
    {
        var credential = GoogleCredential
            .FromServiceAccountCredential(serviceAccountCredential)
            .CreateScoped(ReadOnlyScope);
        _credential = credential.UnderlyingCredential as ITokenAccess
            ?? throw new InvalidOperationException("Google service-account credential cannot issue access tokens.");
    }

    /// <summary>Creates a token provider from the environment-backed credential reference in fleet configuration.</summary>
    /// <param name="credentialReference">Credential kind and environment variable name.</param>
    /// <param name="environmentResolver">Optional environment resolver for alternate hosts and tests.</param>
    /// <returns>Validated service-account token provider.</returns>
    public static GoogleSearchConsoleServiceAccountAccessTokenProvider Create(
        WebSearchCredentialReference credentialReference,
        Func<string, string?>? environmentResolver = null)
    {
        ArgumentNullException.ThrowIfNull(credentialReference);
        environmentResolver ??= Environment.GetEnvironmentVariable;

        var environmentVariable = credentialReference.EnvironmentVariable?.Trim() ?? string.Empty;
        if (environmentVariable.Length == 0)
            throw new ArgumentException("Google Search Console credential environment variable name is required.", nameof(credentialReference));

        var value = environmentResolver(environmentVariable);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"Google Search Console credential environment variable '{environmentVariable}' is unavailable.");

        ServiceAccountCredential credential;
        switch (credentialReference.Kind?.Trim())
        {
            case "google-service-account-json":
                credential = LoadServiceAccount(() => CredentialFactory.FromJson<ServiceAccountCredential>(value));
                break;
            case "google-service-account-file":
                var fullPath = Path.GetFullPath(value.Trim().Trim('"'));
                if (!File.Exists(fullPath))
                    throw new FileNotFoundException(
                        $"Google Search Console service-account file configured by environment variable '{environmentVariable}' was not found.");
                credential = LoadServiceAccount(() => CredentialFactory.FromFile<ServiceAccountCredential>(fullPath));
                break;
            default:
                throw new NotSupportedException($"Google Search Console credential kind '{credentialReference.Kind}' is not supported.");
        }

        return new GoogleSearchConsoleServiceAccountAccessTokenProvider(credential);
    }

    private static ServiceAccountCredential LoadServiceAccount(Func<ServiceAccountCredential> loader)
    {
        try
        {
            return loader();
        }
        catch (Exception ex) when (ex is ArgumentException or InvalidOperationException or JsonException or FormatException or CryptographicException or IOException or UnauthorizedAccessException)
        {
            throw new InvalidOperationException("Google Search Console service-account credential is invalid.");
        }
    }

    /// <inheritdoc />
    public async Task<string> GetAccessTokenAsync(Uri requestUri, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(requestUri);
        var token = await _credential
            .GetAccessTokenForRequestAsync(requestUri.AbsoluteUri, cancellationToken)
            .ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException("Google Search Console authentication returned an empty access token.");
        return token;
    }
}
