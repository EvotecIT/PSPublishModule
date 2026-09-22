using PowerForge;
using PowerForgeStudio.Domain.Queue;
using PowerForgeStudio.Orchestrator.Catalog;
using PowerForgeStudio.Orchestrator.Portfolio;

namespace PowerForgeStudio.Orchestrator.Queue;

/// <summary>Resolves release signing from the captured project contract or the host fallback.</summary>
public sealed class ReleaseSigningSettingsService
{
    private readonly ReleaseSigningHostSettingsResolver _host;
    private readonly CertificateFingerprintResolver _certificates;

    /// <summary>Creates the production resolver for Studio release readiness and signing.</summary>
    public ReleaseSigningSettingsService()
        : this(new ReleaseSigningHostSettingsResolver(Host.PowerForgeStudioHostPaths.ResolvePSPublishModulePath),
            new CertificateFingerprintResolver()) { }

    internal ReleaseSigningSettingsService(ReleaseSigningHostSettingsResolver host, CertificateFingerprintResolver certificates)
    {
        _host = host;
        _certificates = certificates;
    }

    /// <summary>Selects certificate settings for each build adapter against the captured build configuration.</summary>
    public IReadOnlyDictionary<string, ReleaseSigningSettingsSelection> Resolve(ReleaseQueueItem item, ReleaseBuildExecutionResult build)
    {
        ArgumentNullException.ThrowIfNull(item);
        ArgumentNullException.ThrowIfNull(build);
        var kinds = build.AdapterResults.Select(adapter => adapter.AdapterKind).Distinct().ToArray();
        var selected = new Dictionary<string, ReleaseSigningSettingsSelection>(StringComparer.OrdinalIgnoreCase);
        foreach (var kind in kinds.Where(kind => kind != ReleaseBuildAdapterKind.ProjectBuild))
            selected.Add(kind.ToString(), new(_host.Resolve(), "Host environment"));
        if (!kinds.Contains(ReleaseBuildAdapterKind.ProjectBuild))
            return selected;

        var repository = new RepositoryCatalogScanner().InspectRepository(item.RootPath);
        var configPath = string.IsNullOrWhiteSpace(repository.ProjectBuildScriptPath)
            ? null
            : RepositoryPlanPreviewService.ResolveProjectConfigPath(repository.ProjectBuildScriptPath, item.RootPath);
        if (string.IsNullOrWhiteSpace(configPath))
        {
            if (!string.IsNullOrWhiteSpace(build.ProjectBuildConfigSha256))
                throw new InvalidOperationException("Project build JSON is missing. Rebuild before signing.");
            selected.Add(ReleaseBuildAdapterKind.ProjectBuild.ToString(), new(_host.Resolve(), "Host environment"));
            return selected;
        }

        try
        {
            UnifiedReleaseConfigFingerprint.ValidateProjectBuildConfig(configPath, build.ProjectBuildConfigSha256);
            var project = new ProjectBuildHostService().LoadSigningConfiguration(configPath);
            UnifiedReleaseConfigFingerprint.ValidateProjectBuildConfig(configPath, build.ProjectBuildConfigSha256);
            selected.Add(ReleaseBuildAdapterKind.ProjectBuild.ToString(), new(_host.Resolve(project),
                string.IsNullOrWhiteSpace(project.CertificateThumbprint) ? "Host environment" : "Project JSON"));
            return selected;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidOperationException or System.Text.Json.JsonException)
        {
            throw new InvalidOperationException("Project build signing configuration changed, is invalid, or could not be read. Check CertificateStore in project.build.json. Rebuild before signing.", ex);
        }
    }

    /// <summary>Checks that every selected adapter certificate is available before signing is offered.</summary>
    public ReleaseSigningReadiness Check(ReleaseQueueItem item, ReleaseBuildExecutionResult build)
    {
        IReadOnlyDictionary<string, ReleaseSigningSettingsSelection> selected;
        try { selected = Resolve(item, build); }
        catch (InvalidOperationException ex) { return new(false, ex.Message); }
        var statuses = new List<string>();
        var available = selected.Count > 0;
        foreach (var (adapter, selection) in selected)
        {
            var label = selected.Count == 1 ? selection.Source : $"{adapter}: {selection.Source}";
            if (!selection.Settings.IsConfigured)
            {
                available = false;
                var guidance = adapter == ReleaseBuildAdapterKind.ProjectBuild.ToString()
                    ? "Add CertificateThumbprint to project.build.json and rebuild, or set RELEASE_OPS_STUDIO_SIGN_THUMBPRINT and prepare again."
                    : "Set RELEASE_OPS_STUDIO_SIGN_THUMBPRINT and prepare again.";
                statuses.Add($"{label} signing certificate is not configured. {guidance}");
            }
            else if (string.IsNullOrWhiteSpace(_certificates.ResolveSha256(selection.Settings.Thumbprint!, selection.Settings.StoreName)))
            {
                available = false;
                statuses.Add($"{label} selects a certificate that is not available in {selection.Settings.StoreName}\\My on this machine. Make the certificate available, then prepare again.");
            }
            else
                statuses.Add($"{label} signing certificate found in {selection.Settings.StoreName}\\My.");
        }
        if (available) statuses.Add("Signing starts only when you choose Sign artifacts.");
        return new(available, string.Join(" ", statuses));
    }
}

/// <summary>Adapter signing settings and their non-secret source label.</summary>
public sealed record ReleaseSigningSettingsSelection(ReleaseSigningHostSettings Settings, string Source);
/// <summary>Availability and operator-facing guidance for a prepared release.</summary>
public sealed record ReleaseSigningReadiness(bool IsAvailable, string Status);
