namespace PowerForge.Web;

/// <summary>Resolves a CrUX API key at collection time.</summary>
public interface ICruxApiKeyProvider
{
    /// <summary>Returns the API key without persisting or logging it.</summary>
    ValueTask<string> GetApiKeyAsync(CancellationToken cancellationToken = default);
}

/// <summary>Environment-backed CrUX API-key resolver.</summary>
public sealed class CruxEnvironmentApiKeyProvider : ICruxApiKeyProvider
{
    private readonly string _environmentVariable;
    private readonly Func<string, string?> _resolver;

    private CruxEnvironmentApiKeyProvider(string environmentVariable, Func<string, string?> resolver)
    {
        _environmentVariable = environmentVariable;
        _resolver = resolver;
    }

    /// <summary>Creates a resolver from a validated environment-backed credential reference.</summary>
    public static CruxEnvironmentApiKeyProvider Create(
        WebSearchCredentialReference reference,
        Func<string, string?>? resolver = null)
    {
        ArgumentNullException.ThrowIfNull(reference);
        if (!reference.Kind.Equals("google-api-key", StringComparison.Ordinal))
            throw new ArgumentException("CrUX requires a google-api-key credential reference.", nameof(reference));
        if (string.IsNullOrWhiteSpace(reference.EnvironmentVariable))
            throw new ArgumentException("CrUX credential requires an environment variable name.", nameof(reference));
        return new CruxEnvironmentApiKeyProvider(reference.EnvironmentVariable.Trim(), resolver ?? Environment.GetEnvironmentVariable);
    }

    /// <inheritdoc />
    public ValueTask<string> GetApiKeyAsync(CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var value = _resolver(_environmentVariable)?.Trim();
        if (string.IsNullOrWhiteSpace(value))
            throw new InvalidOperationException($"CrUX credential environment variable '{_environmentVariable}' is unavailable.");
        return ValueTask.FromResult(value);
    }
}
