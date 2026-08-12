namespace PowerForge.Tests;

public sealed class VirusTotalMonitorReleaseConfigurationTests
{
    [Theory]
    [InlineData("/{Project}/{Bogus}/{RelativePath}")]
    [InlineData("/{Project}/{Version}/{RelativePath}", "unsupported {Bogus}")]
    public void ValidateConfiguration_UnsupportedTemplateToken_FailsPreflight(
        string destinationTemplate,
        string? detailsTemplate = null)
    {
        var options = new PowerForgeVirusTotalOptions
        {
            Enabled = true,
            ApiKeyEnvName = "VIRUSTOTAL_MONITOR_API_KEY",
            ArtifactKinds = [VirusTotalArtifactKind.MsiPackage],
            DestinationPathTemplate = destinationTemplate,
            DetailsTemplate = detailsTemplate
        };

        var exception = Assert.Throws<InvalidOperationException>(
            () => VirusTotalReleaseArtifactSelector.ValidateConfiguration(options));

        Assert.Contains("unsupported token", exception.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Theory]
    [InlineData("relative/{FileName}")]
    [InlineData("/{Project}//{FileName}")]
    [InlineData("/{Project}/{FileName}/")]
    public void ValidateConfiguration_InvalidDestinationStructure_FailsPreflight(string destinationTemplate)
    {
        var options = new PowerForgeVirusTotalOptions
        {
            Enabled = true,
            ApiKeyEnvName = "VIRUSTOTAL_MONITOR_API_KEY",
            ArtifactKinds = [VirusTotalArtifactKind.MsiPackage],
            DestinationPathTemplate = destinationTemplate
        };

        var exception = Assert.Throws<InvalidOperationException>(
            () => VirusTotalReleaseArtifactSelector.ValidateConfiguration(options));

        Assert.Contains("path structure", exception.Message, StringComparison.OrdinalIgnoreCase);
    }
}
