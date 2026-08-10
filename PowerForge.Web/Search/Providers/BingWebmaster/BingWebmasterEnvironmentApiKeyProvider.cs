namespace PowerForge.Web;

/// <summary>Resolves a Bing Webmaster API key from a configured environment variable.</summary>
public sealed class BingWebmasterEnvironmentApiKeyProvider : IBingWebmasterApiKeyProvider
{
    private readonly string _environmentVariable;
    private readonly Func<string, string?> _environmentResolver;

    private BingWebmasterEnvironmentApiKeyProvider(
        string environmentVariable,
        Func<string, string?> environmentResolver)
    {
        _environmentVariable = environmentVariable;
        _environmentResolver = environmentResolver;
    }

    /// <summary>Creates an environment-backed provider from a validated fleet credential reference.</summary>
    public static BingWebmasterEnvironmentApiKeyProvider Create(
        WebSearchCredentialReference credential,
        Func<string, string?>? environmentResolver = null)
    {
        ArgumentNullException.ThrowIfNull(credential);
        if (!string.Equals(credential.Kind, "bing-api-key", StringComparison.Ordinal))
            throw new ArgumentException("Bing Webmaster requires a bing-api-key credential reference.", nameof(credential));
        if (string.IsNullOrWhiteSpace(credential.EnvironmentVariable))
            throw new ArgumentException("Bing Webmaster credential requires an environment variable name.", nameof(credential));

        return new BingWebmasterEnvironmentApiKeyProvider(
            credential.EnvironmentVariable,
            environmentResolver ?? Environment.GetEnvironmentVariable);
    }

    /// <inheritdoc />
    public Task<string> GetApiKeyAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var value = _environmentResolver(_environmentVariable);
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"Bing Webmaster credential environment variable '{_environmentVariable}' is empty.");
        return Task.FromResult(value.Trim());
    }
}
