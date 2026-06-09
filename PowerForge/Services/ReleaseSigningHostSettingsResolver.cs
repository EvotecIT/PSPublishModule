namespace PowerForge;

/// <summary>
/// Resolves host-facing signing settings from environment variables and shared module discovery.
/// </summary>
public sealed class ReleaseSigningHostSettingsResolver
{
    private readonly Func<string, string?> _getEnvironmentVariable;
    private readonly Func<string> _resolveModulePath;

    /// <summary>
    /// Creates a new resolver using process environment variables.
    /// </summary>
    public ReleaseSigningHostSettingsResolver()
        : this(Environment.GetEnvironmentVariable, static () => string.Empty)
    {
    }

    /// <summary>
    /// Creates a new resolver using process environment variables and the provided module path resolver.
    /// </summary>
    public ReleaseSigningHostSettingsResolver(Func<string> resolveModulePath)
        : this(Environment.GetEnvironmentVariable, resolveModulePath)
    {
    }

    internal ReleaseSigningHostSettingsResolver(
        Func<string, string?> getEnvironmentVariable,
        Func<string> resolveModulePath)
    {
        _getEnvironmentVariable = getEnvironmentVariable ?? throw new ArgumentNullException(nameof(getEnvironmentVariable));
        _resolveModulePath = resolveModulePath ?? throw new ArgumentNullException(nameof(resolveModulePath));
    }

    /// <summary>
    /// Resolves signing settings for Studio/host orchestration.
    /// </summary>
    public ReleaseSigningHostSettings Resolve()
    {
        var thumbprint = TrimOrNull(_getEnvironmentVariable("RELEASE_OPS_STUDIO_SIGN_THUMBPRINT"));
        var storeName = TrimOrDefault(_getEnvironmentVariable("RELEASE_OPS_STUDIO_SIGN_STORE"), "CurrentUser");
        var timeStampServer = TrimOrDefault(_getEnvironmentVariable("RELEASE_OPS_STUDIO_SIGN_TIMESTAMP_URL"), "http://timestamp.digicert.com");
        var modulePath = TrimOrNull(_getEnvironmentVariable("RELEASE_OPS_STUDIO_PSPUBLISHMODULE_PATH"));

        modulePath = string.IsNullOrWhiteSpace(modulePath)
            ? _resolveModulePath()
            : modulePath;

        if (string.IsNullOrWhiteSpace(thumbprint))
        {
            var unresolvedModulePath = modulePath ?? string.Empty;
            return new ReleaseSigningHostSettings {
                IsConfigured = false,
                StoreName = storeName,
                TimeStampServer = timeStampServer,
                ModulePath = unresolvedModulePath,
                MissingConfigurationMessage = "Signing is not configured. Set RELEASE_OPS_STUDIO_SIGN_THUMBPRINT first."
            };
        }

        var resolvedModulePath = modulePath ?? string.Empty;
        return new ReleaseSigningHostSettings {
            IsConfigured = true,
            Thumbprint = thumbprint,
            StoreName = storeName,
            TimeStampServer = timeStampServer,
            ModulePath = resolvedModulePath
        };
    }

    private static string? TrimOrNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value!.Trim();

    private static string TrimOrDefault(string? value, string defaultValue)
        => string.IsNullOrWhiteSpace(value) ? defaultValue : value!.Trim();
}
