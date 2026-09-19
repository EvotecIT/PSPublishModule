namespace PowerForge.Tests;

public sealed partial class PowerForgeReleaseServiceTests
{
    [Theory]
    [InlineData(true, "0.1.9758")]
    [InlineData(false, "0.1.0")]
    public void DotNetPortableOutputPaths_UseMsiVersionOnlyWhenAppliedToPublish(bool applyToPublish, string expectedVersion)
    {
        var root = CreateSandbox();
        try
        {
            var plan = new DotNetPublishPlan
            {
                ProjectRoot = root,
                Targets = [new DotNetPublishTargetPlan
                {
                    Name = "Studio.Windows", Version = "0.1.0",
                    Publish = new DotNetPublishPublishOptions
                    {
                        Framework = "net10.0", Style = DotNetPublishStyle.PortableCompat,
                        OutputPath = "Artifacts/{target}/{version}/{rid}", Zip = true,
                        ZipNameTemplate = "Studio-{version}-{rid}.zip"
                    },
                    Combinations = [new DotNetPublishTargetCombination
                    {
                        Framework = "net10.0", Runtime = "win-x64", Style = DotNetPublishStyle.PortableCompat
                    }]
                }],
                Installers = [new DotNetPublishInstallerPlan
                {
                    Id = "Studio.MSI", PrepareFromTarget = "Studio.Windows",
                    Versioning = new DotNetPublishMsiVersionOptions { Enabled = true, ApplyToPublish = applyToPublish }
                }],
                MsiVersions = new Dictionary<string, DotNetPublishMsiVersionPlan>(StringComparer.OrdinalIgnoreCase)
                {
                    ["Studio.MSI|Studio.Windows|net10.0|win-x64|PortableCompat"] = new() { Version = "0.1.9758" }
                },
                Steps = [new DotNetPublishStep
                {
                    Kind = DotNetPublishStepKind.Publish, TargetName = "Studio.Windows",
                    Framework = "net10.0", Runtime = "win-x64", Style = DotNetPublishStyle.PortableCompat
                }]
            };

            string[] outputs = DotNetPublishPipelineRunner.ResolvePlannedPublishGeneratedPaths(plan);
            Assert.Contains(outputs, path => path.EndsWith(
                Path.Combine("Studio.Windows", expectedVersion, "win-x64"),
                StringComparison.OrdinalIgnoreCase));
            Assert.Contains(outputs, path => path.EndsWith(
                $"Studio-{expectedVersion}-win-x64.zip", StringComparison.OrdinalIgnoreCase));
        }
        finally
        {
            TryDelete(root);
        }
    }

    [Theory]
    [InlineData(true, "0.1.9758")]
    [InlineData(false, "0.1.0")]
    public void DotNetPortableAsset_UsesResolvedMsiReleaseVersionOnlyWhenAppliedToPublish(bool applyToPublish, string expectedVersion)
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
                    Versioning = new DotNetPublishMsiVersionOptions { Enabled = true, ApplyToPublish = applyToPublish }
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
            Assert.Equal(expectedVersion, entry.Version);
        }
        finally
        {
            TryDelete(root);
        }
    }
}
