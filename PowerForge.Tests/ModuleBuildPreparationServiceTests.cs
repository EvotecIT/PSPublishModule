using System.Collections;
using System.Text.Json;
using System.Text.Json.Serialization;
using PowerForge;

namespace PowerForge.Tests;

public sealed class ModuleBuildPreparationServiceTests
{
    [Fact]
    public void Prepare_from_modern_request_builds_project_paths_and_spec()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-modulebuild-modern-" + Guid.NewGuid().ToString("N")));

        try
        {
            var request = new ModuleBuildPreparationRequest
            {
                ParameterSetName = "Modern",
                ModuleName = "SampleModule",
                InputPath = root.FullName,
                CurrentPath = root.FullName,
                ResolvePath = path => path,
                DotNetFramework = new[] { "net8.0" },
                DotNetFrameworkWasBound = false,
                Legacy = true,
                ExcludeDirectories = new[] { ".git", "bin" },
                ExcludeFiles = new[] { ".gitignore" },
                JsonOnly = true
            };

            var prepared = new ModuleBuildPreparationService().Prepare(request);

            Assert.Equal("SampleModule", prepared.ModuleName);
            Assert.Equal(Path.Combine(root.FullName, "SampleModule"), prepared.ProjectRoot);
            Assert.Equal(root.FullName, prepared.BasePathForScaffold);
            Assert.True(prepared.UseLegacy);
            Assert.Empty(prepared.PipelineSpec.Build.Frameworks);
            Assert.Contains(".gitignore", prepared.PipelineSpec.Build.ExcludeFiles);
            Assert.Contains("SampleModule.Tests.ps1", prepared.PipelineSpec.Build.ExcludeFiles);
            Assert.Equal(Path.Combine(root.FullName, "SampleModule", "powerforge.json"), prepared.JsonOutputPath);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Prepare_from_configuration_uses_legacy_module_name_and_manifest_version()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-modulebuild-config-" + Guid.NewGuid().ToString("N")));

        try
        {
            File.WriteAllText(
                Path.Combine(root.FullName, "SampleModule.psd1"),
                "@{ ModuleVersion = '2.4.6' }");

            var configuration = new Hashtable
            {
                ["Information"] = new Hashtable
                {
                    ["ModuleName"] = "SampleModule"
                }
            };

            var prepared = new ModuleBuildPreparationService().Prepare(new ModuleBuildPreparationRequest
            {
                ParameterSetName = "Configuration",
                Configuration = configuration,
                CurrentPath = root.FullName,
                ResolvePath = path => path,
                SkipInstall = true,
                DiagnosticsBaselinePath = Path.Combine(root.FullName, ".powerforge", "baseline.json"),
                FailOnNewDiagnostics = true
            });

            Assert.Equal("SampleModule", prepared.ModuleName);
            Assert.Equal(root.FullName, prepared.ProjectRoot);
            Assert.Null(prepared.BasePathForScaffold);
            Assert.True(prepared.UseLegacy);
            Assert.Equal("2.4.6", prepared.PipelineSpec.Build.Version);
            Assert.False(prepared.PipelineSpec.Install.Enabled);
            Assert.Equal(Path.Combine(root.FullName, ".powerforge", "baseline.json"), prepared.PipelineSpec.Diagnostics.BaselinePath);
            Assert.True(prepared.PipelineSpec.Diagnostics.FailOnNewDiagnostics);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Prepare_prefers_configured_manifest_version_over_source_manifest_version()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-modulebuild-configured-version-" + Guid.NewGuid().ToString("N")));

        try
        {
            File.WriteAllText(
                Path.Combine(root.FullName, "SampleModule.psd1"),
                "@{ ModuleVersion = '3.0.0' }");

            var configuration = new Hashtable
            {
                ["Information"] = new Hashtable
                {
                    ["ModuleName"] = "SampleModule",
                    ["Manifest"] = new Hashtable
                    {
                        ["ModuleVersion"] = "3.0.X",
                        ["CompatiblePSEditions"] = new[] { "Desktop", "Core" },
                        ["Author"] = "Przemyslaw Klys"
                    }
                }
            };

            var prepared = new ModuleBuildPreparationService().Prepare(new ModuleBuildPreparationRequest
            {
                ParameterSetName = "Configuration",
                Configuration = configuration,
                CurrentPath = root.FullName,
                ResolvePath = path => path
            });

            Assert.Equal("3.0.X", prepared.PipelineSpec.Build.Version);
            var manifestSegment = Assert.IsType<ConfigurationManifestSegment>(Assert.Single(prepared.PipelineSpec.Segments));
            Assert.Equal("3.0.X", manifestSegment.Configuration.ModuleVersion);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void WritePipelineSpecJson_rewrites_paths_relative_to_output()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-modulebuild-json-" + Guid.NewGuid().ToString("N")));

        try
        {
            var jsonPath = Path.Combine(root.FullName, ".powerforge", "powerforge.json");
            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = "SampleModule",
                    SourcePath = root.FullName,
                    StagingPath = Path.Combine(root.FullName, "staging"),
                    CsprojPath = Path.Combine(root.FullName, "src", "SampleModule.csproj")
                },
                Diagnostics = new ModulePipelineDiagnosticsOptions
                {
                    BaselinePath = Path.Combine(root.FullName, ".powerforge", "baseline.json")
                }
            };

            new ModuleBuildPreparationService().WritePipelineSpecJson(spec, jsonPath);

            var json = File.ReadAllText(jsonPath);
            Assert.Contains("\"SourcePath\": \"..\"", json, StringComparison.Ordinal);
            Assert.Contains("\"StagingPath\": \"../staging\"", json, StringComparison.Ordinal);
            Assert.Contains("\"CsprojPath\": \"../src/SampleModule.csproj\"", json, StringComparison.Ordinal);
            Assert.Contains("\"BaselinePath\": \"baseline.json\"", json, StringComparison.Ordinal);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void WritePipelineSpecJson_preserves_configured_manifest_version_in_build_spec()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-modulebuild-json-version-" + Guid.NewGuid().ToString("N")));

        try
        {
            var jsonPath = Path.Combine(root.FullName, ".powerforge", "powerforge.json");
            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = "SampleModule",
                    SourcePath = root.FullName,
                    Version = "3.0.X"
                },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationManifestSegment
                    {
                        Configuration = new ManifestConfiguration
                        {
                            ModuleVersion = "3.0.X",
                            Author = "Przemyslaw Klys"
                        }
                    }
                }
            };

            new ModuleBuildPreparationService().WritePipelineSpecJson(spec, jsonPath);

            var json = File.ReadAllText(jsonPath);
            Assert.Contains("\"Version\": \"3.0.X\"", json, StringComparison.Ordinal);
            Assert.Contains("\"ModuleVersion\": \"3.0.X\"", json, StringComparison.Ordinal);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void WritePipelineSpecJson_round_trips_pipeline_plan_without_losing_publish_or_version_data()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-modulebuild-json-parity-" + Guid.NewGuid().ToString("N")));

        try
        {
            const string moduleName = "SampleModule";
            File.WriteAllText(Path.Combine(root.FullName, $"{moduleName}.psd1"), "@{ ModuleVersion = '3.0.0' }");
            File.WriteAllText(Path.Combine(root.FullName, $"{moduleName}.psm1"), string.Empty);

            var spec = new ModulePipelineSpec
            {
                Build = new ModuleBuildSpec
                {
                    Name = moduleName,
                    SourcePath = root.FullName,
                    Version = "3.0.X",
                    Configuration = "Release",
                    Frameworks = new[] { "net8.0", "net472" }
                },
                Install = new ModulePipelineInstallOptions
                {
                    Enabled = true,
                    Strategy = InstallationStrategy.AutoRevision,
                    KeepVersions = 3
                },
                Segments = new IConfigurationSegment[]
                {
                    new ConfigurationManifestSegment
                    {
                        Configuration = new ManifestConfiguration
                        {
                            ModuleVersion = "3.0.X",
                            CompatiblePSEditions = new[] { "Desktop", "Core" },
                            Guid = "eb76426a-1992-40a5-82cd-6480f883ef4d",
                            Author = "Przemyslaw Klys"
                        }
                    },
                    new ConfigurationArtefactSegment
                    {
                        ArtefactType = ArtefactType.Unpacked,
                        Configuration = new ArtefactConfiguration
                        {
                            Enabled = true,
                            Path = "Artefacts/Unpacked/<TagModuleVersionWithPreRelease>"
                        }
                    },
                    new ConfigurationPublishSegment
                    {
                        Configuration = new PublishConfiguration
                        {
                            Destination = PublishDestination.PowerShellGallery,
                            Enabled = true,
                            RepositoryName = "PSGallery"
                        }
                    },
                    new ConfigurationPublishSegment
                    {
                        Configuration = new PublishConfiguration
                        {
                            Destination = PublishDestination.GitHub,
                            Enabled = true,
                            ID = "ToGitHub",
                            UserName = "EvotecIT",
                            OverwriteTagName = "<TagModuleVersionWithPreRelease>"
                        }
                    }
                }
            };

            var runner = new ModulePipelineRunner(new NullLogger());
            var directPlan = runner.Plan(spec);

            var jsonPath = Path.Combine(root.FullName, ".powerforge", "powerforge.json");
            new ModuleBuildPreparationService().WritePipelineSpecJson(spec, jsonPath);

            var json = File.ReadAllText(jsonPath);
            var jsonSpec = JsonSerializer.Deserialize<ModulePipelineSpec>(json, CreateJsonOptions());
            Assert.NotNull(jsonSpec);

            ResolvePipelineSpecPathsLikeCli(jsonSpec!, jsonPath);
            var roundTrippedPlan = runner.Plan(jsonSpec!);

            Assert.Equal(directPlan.ExpectedVersion, roundTrippedPlan.ExpectedVersion);
            Assert.Equal(directPlan.ResolvedVersion, roundTrippedPlan.ResolvedVersion);
            Assert.Equal(directPlan.BuildSpec.Version, roundTrippedPlan.BuildSpec.Version);
            Assert.Equal(directPlan.Publishes.Length, roundTrippedPlan.Publishes.Length);
            Assert.Equal(directPlan.Artefacts.Length, roundTrippedPlan.Artefacts.Length);
            Assert.Equal(directPlan.InstallEnabled, roundTrippedPlan.InstallEnabled);
            Assert.Equal(directPlan.InstallStrategy, roundTrippedPlan.InstallStrategy);
            Assert.Equal(directPlan.InstallKeepVersions, roundTrippedPlan.InstallKeepVersions);
            Assert.Equal(
                directPlan.Publishes.Select(p => p.Configuration.Destination).ToArray(),
                roundTrippedPlan.Publishes.Select(p => p.Configuration.Destination).ToArray());
            Assert.Equal(
                directPlan.Publishes.Select(p => p.Configuration.ID ?? string.Empty).ToArray(),
                roundTrippedPlan.Publishes.Select(p => p.Configuration.ID ?? string.Empty).ToArray());
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true,
            DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull
        };
        options.Converters.Add(new JsonStringEnumConverter());
        options.Converters.Add(new ConfigurationSegmentJsonConverter());
        return options;
    }

    private static void ResolvePipelineSpecPathsLikeCli(ModulePipelineSpec spec, string configFullPath)
    {
        var baseDir = Path.GetDirectoryName(configFullPath) ?? Directory.GetCurrentDirectory();

        if (!string.IsNullOrWhiteSpace(spec.Build.SourcePath))
            spec.Build.SourcePath = Path.GetFullPath(Path.IsPathRooted(spec.Build.SourcePath) ? spec.Build.SourcePath : Path.Combine(baseDir, spec.Build.SourcePath));

        if (!string.IsNullOrWhiteSpace(spec.Build.StagingPath))
            spec.Build.StagingPath = Path.GetFullPath(Path.IsPathRooted(spec.Build.StagingPath) ? spec.Build.StagingPath! : Path.Combine(baseDir, spec.Build.StagingPath!));

        if (!string.IsNullOrWhiteSpace(spec.Build.CsprojPath))
            spec.Build.CsprojPath = Path.GetFullPath(Path.IsPathRooted(spec.Build.CsprojPath) ? spec.Build.CsprojPath! : Path.Combine(baseDir, spec.Build.CsprojPath!));
    }
}
