using System.Text.Json;

namespace PowerForge.Tests;

public sealed class PowerForgeReleaseConfigScaffoldServiceTests
{
    [Fact]
    public void Generate_prefers_module_json_over_legacy_build_script()
    {
        var root = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            "pf-release-scaffold-json-" + Guid.NewGuid().ToString("N")));

        try
        {
            File.WriteAllText(
                Path.Combine(root.FullName, "powerforge.json"),
                """
                {
                  "SchemaVersion": 1,
                  "Build": { "Name": "Sample", "SourcePath": "CustomModule", "Version": "1.0.0" },
                  "Segments": [
                    {
                      "Type": "Manifest",
                      "Configuration": { "ModuleVersion": "1.2.0" }
                    },
                    {
                      "Type": "Packed",
                      "Configuration": {
                        "Enabled": true,
                        "Path": "Output/<TagModuleVersionWithPreRelease>"
                      }
                    }
                  ]
                }
                """);
            var moduleDirectory = Directory.CreateDirectory(Path.Combine(root.FullName, "CustomModule"));
            File.WriteAllText(
                Path.Combine(moduleDirectory.FullName, "Sample.psd1"),
                "@{ ModuleVersion = '1.0.0' }");
            var legacyDirectory = Directory.CreateDirectory(Path.Combine(root.FullName, "Module", "Build"));
            File.WriteAllText(Path.Combine(legacyDirectory.FullName, "Build-Module.ps1"), "# legacy");

            var result = new PowerForgeReleaseConfigScaffoldService().Generate(new PowerForgeReleaseConfigScaffoldRequest
            {
                ProjectRoot = root.FullName,
                WorkingDirectory = root.FullName,
                SkipPackages = true,
                SkipTools = true
            });

            Assert.Equal(Path.Combine(root.FullName, "powerforge.json"), result.ModuleConfigPath);
            Assert.Null(result.ModuleScriptPath);
            using var release = JsonDocument.Parse(File.ReadAllText(result.ConfigPath));
            var module = release.RootElement.GetProperty("Module");
            Assert.Equal("Sample", module.GetProperty("ModuleName").GetString());
            Assert.Equal("powerforge.json", module.GetProperty("ConfigPath").GetString());
            Assert.False(module.TryGetProperty("ScriptPath", out _));
            Assert.Equal("CustomModule/Sample.psd1", module.GetProperty("ManifestPath").GetString());
            Assert.Equal("1.2.0", module.GetProperty("ModuleVersion").GetString());
            Assert.Equal(
                "CustomModule/Output/<TagModuleVersionWithPreRelease>",
                module.GetProperty("ArtifactPaths")[0].GetString());
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Generate_module_json_without_artifact_segments_uses_pipeline_defaults()
    {
        var root = Directory.CreateDirectory(Path.Combine(
            Path.GetTempPath(),
            "pf-release-scaffold-defaults-" + Guid.NewGuid().ToString("N")));

        try
        {
            File.WriteAllText(
                Path.Combine(root.FullName, "powerforge.json"),
                """
                {
                  "Build": { "Name": "Sample", "SourcePath": "Module" },
                  "Segments": []
                }
                """);
            Directory.CreateDirectory(Path.Combine(root.FullName, "Module"));

            var result = new PowerForgeReleaseConfigScaffoldService().Generate(new PowerForgeReleaseConfigScaffoldRequest
            {
                ProjectRoot = root.FullName,
                WorkingDirectory = root.FullName,
                SkipPackages = true,
                SkipTools = true
            });

            using var release = JsonDocument.Parse(File.ReadAllText(result.ConfigPath));
            var paths = release.RootElement
                .GetProperty("Module")
                .GetProperty("ArtifactPaths")
                .EnumerateArray()
                .Select(static element => element.GetString()!)
                .ToArray();
            Assert.Equal(
                ["Module/Artefacts/Packed", "Module/Artefacts/PackedWithModules", "Module/Artefacts/Unpacked"],
                paths);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }
}
