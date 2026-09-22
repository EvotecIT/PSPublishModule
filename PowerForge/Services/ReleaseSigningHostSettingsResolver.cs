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
        var storeName = ValidatedStoreName(_getEnvironmentVariable("RELEASE_OPS_STUDIO_SIGN_STORE"));
        var timeStampServer = TrimOrDefault(_getEnvironmentVariable("RELEASE_OPS_STUDIO_SIGN_TIMESTAMP_URL"), "http://timestamp.digicert.com");
        var modulePath = ResolveModulePath();

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

    /// <summary>Uses a project-declared certificate when present, otherwise host settings.</summary>
    public ReleaseSigningHostSettings Resolve(ProjectBuildSigningConfiguration? project)
    {
        if (string.IsNullOrWhiteSpace(project?.CertificateThumbprint))
            return Resolve();

        return new ReleaseSigningHostSettings {
            IsConfigured = true,
            Thumbprint = project.CertificateThumbprint.Trim(),
            StoreName = ValidatedStoreName(project.CertificateStore),
            TimeStampServer = TrimOrDefault(project.TimeStampServer, "http://timestamp.digicert.com"),
            ModulePath = ResolveModulePath()
        };
    }

    private string ResolveModulePath()
        => TrimOrNull(_getEnvironmentVariable("RELEASE_OPS_STUDIO_PSPUBLISHMODULE_PATH")) ?? _resolveModulePath() ?? string.Empty;

    private static string? TrimOrNull(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value!.Trim();

    private static string TrimOrDefault(string? value, string defaultValue)
        => string.IsNullOrWhiteSpace(value) ? defaultValue : value!.Trim();

    private static string ValidatedStoreName(string? value)
    {
        var store = TrimOrDefault(value, "CurrentUser");
        if (store.Equals("CurrentUser", StringComparison.OrdinalIgnoreCase)) return "CurrentUser";
        if (store.Equals("LocalMachine", StringComparison.OrdinalIgnoreCase)) return "LocalMachine";
        throw new InvalidOperationException("Signing certificate store must be CurrentUser or LocalMachine.");
    }
}
