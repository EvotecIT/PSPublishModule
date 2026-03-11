using System.Collections;
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
}
