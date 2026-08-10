namespace PowerForge.Web;

/// <summary>Resolves the referenced Cloudflare API token from an environment variable.</summary>
public sealed class CloudflareEnvironmentApiTokenProvider : ICloudflareAnalyticsTokenProvider
{
    private readonly string _environmentVariable;
    private readonly Func<string, string?> _readEnvironmentVariable;

    private CloudflareEnvironmentApiTokenProvider(string environmentVariable, Func<string, string?> readEnvironmentVariable)
    {
        _environmentVariable = environmentVariable;
        _readEnvironmentVariable = readEnvironmentVariable;
    }

    /// <summary>Creates a provider from a non-secret environment-variable credential reference.</summary>
    public static CloudflareEnvironmentApiTokenProvider Create(
        WebSearchCredentialReference credential,
        Func<string, string?>? readEnvironmentVariable = null)
    {
        ArgumentNullException.ThrowIfNull(credential);
        if (!string.Equals(credential.Kind?.Trim(), "cloudflare-api-token", StringComparison.OrdinalIgnoreCase))
            throw new ArgumentException("Cloudflare analytics requires a cloudflare-api-token credential.", nameof(credential));
        if (string.IsNullOrWhiteSpace(credential.EnvironmentVariable))
            throw new ArgumentException("Cloudflare analytics credential requires an environment variable reference.", nameof(credential));
        return new CloudflareEnvironmentApiTokenProvider(
            credential.EnvironmentVariable.Trim(),
            readEnvironmentVariable ?? Environment.GetEnvironmentVariable);
    }

    /// <inheritdoc />
    public Task<string> GetTokenAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var token = _readEnvironmentVariable(_environmentVariable)?.Trim();
        if (string.IsNullOrWhiteSpace(token))
            throw new InvalidOperationException($"Cloudflare API token environment variable '{_environmentVariable}' is unavailable.");
        return Task.FromResult(token);
    }
}
