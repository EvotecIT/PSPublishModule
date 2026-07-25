namespace PowerForge.Tests;

public sealed partial class ModuleBuildPreparationServiceTests
{
    [Fact]
    public void Prepare_from_config_applies_diagnostics_and_parent_publication_overrides()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-modulebuild-config-overrides-" + Guid.NewGuid().ToString("N")));
        try
        {
            var configPath = Path.Combine(root.FullName, "powerforge.json");
            File.WriteAllText(
                configPath,
                """
                {
                  "Build": { "Name": "Sample", "SourcePath": "." },
                  "Diagnostics": {
                    "GenerateBaseline": true,
                    "UpdateBaseline": false,
                    "FailOnNewDiagnostics": false,
                    "FailOnSeverity": "Warning"
                  },
                  "Segments": [
                    {
                      "Type": "GitHubNuget",
                      "Configuration": { "Destination": "GitHub", "Enabled": true }
                    },
                    {
                      "Type": "GalleryNuget",
                      "Configuration": { "Destination": "PowerShellGallery", "Enabled": true }
                    }
                  ]
                }
                """);
            var baselinePath = Path.Combine(root.FullName, ".powerforge", "override.json");

            var prepared = new ModuleBuildPreparationService().Prepare(new ModuleBuildPreparationRequest
            {
                ParameterSetName = "Config",
                ConfigPath = configPath,
                CurrentPath = root.FullName,
                ResolvePath = path => Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(root.FullName, path)),
                UnifiedGitHubRelease = true,
                SkipInstall = true,
                DiagnosticsBaselinePath = baselinePath,
                DiagnosticsBaselinePathWasBound = true,
                GenerateDiagnosticsBaseline = false,
                GenerateDiagnosticsBaselineWasBound = true,
                UpdateDiagnosticsBaseline = true,
                UpdateDiagnosticsBaselineWasBound = true,
                FailOnNewDiagnostics = true,
                FailOnNewDiagnosticsWasBound = true,
                FailOnDiagnosticsSeverity = BuildDiagnosticSeverity.Error,
                FailOnDiagnosticsSeverityWasBound = true
            });

            Assert.Equal(baselinePath, prepared.PipelineSpec.Diagnostics.BaselinePath);
            Assert.False(prepared.PipelineSpec.Install.Enabled);
            Assert.False(prepared.PipelineSpec.Diagnostics.GenerateBaseline);
            Assert.True(prepared.PipelineSpec.Diagnostics.UpdateBaseline);
            Assert.True(prepared.PipelineSpec.Diagnostics.FailOnNewDiagnostics);
            Assert.Equal(BuildDiagnosticSeverity.Error, prepared.PipelineSpec.Diagnostics.FailOnSeverity);
            var publish = Assert.Single(prepared.PipelineSpec.Segments.OfType<ConfigurationPublishSegment>());
            Assert.Equal(PublishDestination.PowerShellGallery, publish.Configuration.Destination);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }
}
