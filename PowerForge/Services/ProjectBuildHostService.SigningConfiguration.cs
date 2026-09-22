namespace PowerForge;

public sealed partial class ProjectBuildHostService
{
    /// <summary>Reads only certificate selection from a project build JSON contract.</summary>
    public ProjectBuildSigningConfiguration LoadSigningConfiguration(string configPath)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(configPath);
        var config = new ProjectBuildSupportService(_logger).LoadConfig(configPath);
        return new ProjectBuildSigningConfiguration(
            config.CertificateThumbprint,
            config.CertificateStore,
            config.TimeStampServer);
    }
}
