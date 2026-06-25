using System.Collections;
using System.Management.Automation;
using System.Management.Automation.Runspaces;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed class ModuleDevelopmentBootstrapperConfigurationTests
{
    [Fact]
    public void Schemas_allow_development_binary_bootstrapper_options()
    {
        using var segmentsSchema = JsonDocument.Parse(File.ReadAllText(SchemaPath("powerforge.segments.schema.json")));
        var buildLibrariesProperties = segmentsSchema.RootElement
            .GetProperty("$defs")
            .GetProperty("BuildLibrariesConfiguration")
            .GetProperty("properties");

        Assert.True(buildLibrariesProperties.TryGetProperty("DevelopmentBinaries", out _));
        Assert.True(buildLibrariesProperties.TryGetProperty("DevelopmentBinariesMode", out _));
        Assert.True(buildLibrariesProperties.TryGetProperty("DevelopmentBinariesPath", out _));
        Assert.True(buildLibrariesProperties.TryGetProperty("DevelopmentBinariesEnvironmentVariable", out _));
        Assert.True(buildLibrariesProperties.TryGetProperty("DevelopmentConfigurationEnvironmentVariable", out _));

        using var buildSpecSchema = JsonDocument.Parse(File.ReadAllText(SchemaPath("powerforge.buildspec.schema.json")));
        var buildSpecProperties = buildSpecSchema.RootElement.GetProperty("properties");

        Assert.True(buildSpecProperties.TryGetProperty("DevelopmentBinariesMode", out _));
        Assert.True(buildSpecProperties.TryGetProperty("DevelopmentBinariesPath", out _));
        Assert.True(buildSpecProperties.TryGetProperty("DevelopmentBinariesEnvironmentVariable", out _));
        Assert.True(buildSpecProperties.TryGetProperty("DevelopmentConfigurationEnvironmentVariable", out _));
    }

    [Fact]
    public void Legacy_adapter_maps_development_binary_options_from_steps_build_libraries()
    {
        var legacy = new Hashtable
        {
            ["Steps"] = new Hashtable
            {
                ["BuildLibraries"] = new Hashtable
                {
                    ["DevelopmentBinaries"] = true,
                    ["DevelopmentBinariesMode"] = "Auto",
                    ["DevelopmentBinariesPath"] = "Sources/Demo/bin",
                    ["DevelopmentBinariesEnvironmentVariable"] = "DEMO_DEV",
                    ["DevelopmentConfigurationEnvironmentVariable"] = "DEMO_CONFIGURATION"
                }
            }
        };

        var libraries = Assert.IsType<ConfigurationBuildLibrariesSegment>(
            Assert.Single(LegacySegmentAdapter.CollectFromLegacyConfiguration(legacy)));

        Assert.True(libraries.BuildLibraries.DevelopmentBinaries);
        Assert.Equal(ModuleDevelopmentBinaryMode.Auto, libraries.BuildLibraries.DevelopmentBinariesMode);
        Assert.Equal("Sources/Demo/bin", libraries.BuildLibraries.DevelopmentBinariesPath);
        Assert.Equal("DEMO_DEV", libraries.BuildLibraries.DevelopmentBinariesEnvironmentVariable);
        Assert.Equal("DEMO_CONFIGURATION", libraries.BuildLibraries.DevelopmentConfigurationEnvironmentVariable);
    }

    [Fact]
    public void Legacy_adapter_maps_development_binary_options_from_segment_dictionary()
    {
        var settings = ScriptBlock.Create("""
            @{
                Type = 'BuildLibraries'
                BuildLibraries = @{
                    DevelopmentBinaries = $true
                    DevelopmentBinariesMode = 'Environment'
                    DevelopmentBinariesPath = 'Sources/Demo/bin'
                    DevelopmentBinariesEnvironmentVariable = 'DEMO_DEV'
                    DevelopmentConfigurationEnvironmentVariable = 'DEMO_CONFIGURATION'
                }
            }
            """);

        using var runspace = RunspaceFactory.CreateRunspace();
        runspace.Open();
        var previousRunspace = Runspace.DefaultRunspace;
        Runspace.DefaultRunspace = runspace;

        try
        {
            var libraries = Assert.IsType<ConfigurationBuildLibrariesSegment>(
                Assert.Single(LegacySegmentAdapter.CollectFromSettings(settings)));

            Assert.True(libraries.BuildLibraries.DevelopmentBinaries);
            Assert.Equal(ModuleDevelopmentBinaryMode.Environment, libraries.BuildLibraries.DevelopmentBinariesMode);
            Assert.Equal("Sources/Demo/bin", libraries.BuildLibraries.DevelopmentBinariesPath);
            Assert.Equal("DEMO_DEV", libraries.BuildLibraries.DevelopmentBinariesEnvironmentVariable);
            Assert.Equal("DEMO_CONFIGURATION", libraries.BuildLibraries.DevelopmentConfigurationEnvironmentVariable);
        }
        finally
        {
            Runspace.DefaultRunspace = previousRunspace;
        }
    }

    private static string SchemaPath(string fileName)
        => Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "Schemas", fileName));
}
