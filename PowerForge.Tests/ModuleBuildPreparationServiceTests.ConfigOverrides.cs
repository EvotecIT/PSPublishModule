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
            var stagingPath = Path.Combine(root.FullName, ".powerforge", "approved-staging");

            var prepared = new ModuleBuildPreparationService().Prepare(new ModuleBuildPreparationRequest
            {
                ParameterSetName = "Config",
                ConfigPath = configPath,
                CurrentPath = root.FullName,
                ResolvePath = path => Path.IsPathRooted(path) ? path : Path.GetFullPath(Path.Combine(root.FullName, path)),
                UnifiedGitHubRelease = true,
                SkipInstall = true,
                StagingPath = Path.Combine(".powerforge", "approved-staging"),
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
            Assert.Equal(stagingPath, prepared.PipelineSpec.Build.StagingPath);
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

    [Fact]
    public void Prepare_from_config_explicit_no_sign_disables_delivery_signing()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-modulebuild-config-nosign-" + Guid.NewGuid().ToString("N")));
        var moduleRoot = Directory.CreateDirectory(Path.Combine(root.FullName, "Module"));
        var configPath = Path.Combine(root.FullName, "powerforge.json");
        File.WriteAllText(Path.Combine(moduleRoot.FullName, "Sample.psd1"), "@{ RootModule = 'Sample.psm1'; ModuleVersion = '1.0.0' }");
        File.WriteAllText(Path.Combine(moduleRoot.FullName, "Sample.psm1"), string.Empty);
        File.WriteAllText(
            configPath,
            """
            {
              "Build": { "Name": "Sample", "SourcePath": "Module", "Version": "1.0.0" },
              "Segments": [
                {
                  "Type": "Build",
                  "BuildModule": { "SignMerged": true }
                },
                {
                  "Type": "Options",
                  "Options": {
                    "Delivery": { "Enable": true, "Sign": true }
                  }
                }
              ]
            }
            """);

        try
        {
            var prepared = new ModuleBuildPreparationService().Prepare(new ModuleBuildPreparationRequest
            {
                ParameterSetName = "Config",
                ConfigPath = configPath,
                CurrentPath = root.FullName,
                ResolvePath = path => path,
                NoSign = true,
                NoSignWasBound = true
            });

            var delivery = Assert.Single(prepared.PipelineSpec.Segments.OfType<ConfigurationOptionsSegment>()).Options.Delivery;
            Assert.NotNull(delivery);
            Assert.False(delivery!.Sign);
            Assert.False(new ModulePipelineRunner(new NullLogger()).Plan(prepared.PipelineSpec).SignModule);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }
}
