namespace PowerForge;

public sealed partial class ProjectBuildHostService
{
    /// <summary>Reads only certificate selection from a project build JSON contract.</summary>
    public ProjectBuildSigningConfiguration LoadSigningConfiguration(string configPath)
    {
        if (configPath is null) throw new ArgumentNullException(nameof(configPath));
        var path = FrameworkCompatibility.NotNullOrWhiteSpace(configPath, nameof(configPath));
        var config = new ProjectBuildSupportService(_logger).LoadConfig(path);
        return new ProjectBuildSigningConfiguration(
            config.CertificateThumbprint,
            config.CertificateStore,
            config.TimeStampServer);
    }
}
