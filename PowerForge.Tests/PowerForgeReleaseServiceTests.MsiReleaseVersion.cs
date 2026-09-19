namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests
{
    [Fact]
    public void DotNetPortableAsset_UsesResolvedMsiReleaseVersion()
    {
        var root = CreateSandbox();
        try
        {
            var archive = Path.Combine(root, "studio.zip");
            File.WriteAllText(archive, "archive");
            var plan = new DotNetPublishPlan
            {
                Targets = [new DotNetPublishTargetPlan
                {
                    Name = "Studio.Windows", Version = "0.1.0",
                    Combinations = [new DotNetPublishTargetCombination
                    {
                        Framework = "net10.0", Runtime = "win-x64", Style = DotNetPublishStyle.PortableCompat
                    }]
                }],
                Installers = [new DotNetPublishInstallerPlan
                {
                    Id = "Studio.MSI", PrepareFromTarget = "Studio.Windows",
                    Versioning = new DotNetPublishMsiVersionOptions { Enabled = true, ApplyToPublish = true }
                }],
                MsiVersions = new Dictionary<string, DotNetPublishMsiVersionPlan>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Studio.MSI|Studio.Windows|net10.0|win-x64|PortableCompat"] = new() { Version = "0.1.9758" }
                }
            };
            var artifact = new DotNetPublishArtefactResult
            {
                Category = DotNetPublishArtefactCategory.Bundle,
                Target = "Studio.Windows", Framework = "net10.0", Runtime = "win-x64",
                Style = DotNetPublishStyle.PortableCompat, ZipPath = archive
            };

            var entry = Assert.Single(PowerForgeReleaseService.CreateDotNetArtefactEntries(artifact, plan, null));
            Assert.Equal("0.1.9758", entry.Version);
        }
        finally
        {
            TryDelete(root);
        }
    }
}
