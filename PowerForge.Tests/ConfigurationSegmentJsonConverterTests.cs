using System.Text.Json;
using Xunit;

namespace PowerForge.Tests;

public sealed class ConfigurationSegmentJsonConverterTests
{
    [Fact]
    public void Deserialize_ReadsXcodeProjectVersionSegment()
    {
        const string json = """
            {
              "Build": {
                "Name": "PSPublishModule",
                "SourcePath": ".",
                "Version": "1.0.0"
              },
              "Install": {
                "Enabled": false
              },
              "Segments": [
                {
                  "Type": "XcodeProjectVersion",
                  "Configuration": {
                    "Path": "Tactra.xcodeproj",
                    "UseResolvedVersion": true,
                    "BuildNumber": "4"
                  }
                }
              ]
            }
            """;

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        options.Converters.Add(new ConfigurationSegmentJsonConverter());

        var spec = JsonSerializer.Deserialize<ModulePipelineSpec>(json, options);

        Assert.NotNull(spec);
        var segment = Assert.IsType<ConfigurationXcodeProjectVersionSegment>(Assert.Single(spec!.Segments));
        Assert.Equal("Tactra.xcodeproj", segment.Configuration.Path);
        Assert.True(segment.Configuration.UseResolvedVersion);
        Assert.Equal("4", segment.Configuration.BuildNumber);
    }

    [Fact]
    public void Deserialize_ReadsAppleAppSegment()
    {
        const string json = """
            {
              "Build": {
                "Name": "PSPublishModule",
                "SourcePath": ".",
                "Version": "1.0.0"
              },
              "Install": {
                "Enabled": false
              },
              "Segments": [
                {
                  "Type": "AppleApp",
                  "Configuration": {
                    "Name": "Tactra",
                    "BundleId": "com.example.Tactra",
                    "Platform": "iOS",
                    "ProjectPath": "Tactra.xcodeproj",
                    "Scheme": "Tactra",
                    "UseResolvedVersion": true,
                    "BuildNumberPolicy": "IncrementExisting"
                  }
                }
              ]
            }
            """;

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        options.Converters.Add(new System.Text.Json.Serialization.JsonStringEnumConverter());
        options.Converters.Add(new ConfigurationSegmentJsonConverter());

        var spec = JsonSerializer.Deserialize<ModulePipelineSpec>(json, options);

        Assert.NotNull(spec);
        var segment = Assert.IsType<ConfigurationAppleAppSegment>(Assert.Single(spec!.Segments));
        Assert.Equal("Tactra", segment.Configuration.Name);
        Assert.Equal("com.example.Tactra", segment.Configuration.BundleId);
        Assert.Equal(ApplePlatform.iOS, segment.Configuration.Platform);
        Assert.Equal("Tactra.xcodeproj", segment.Configuration.ProjectPath);
        Assert.True(segment.Configuration.UseResolvedVersion);
        Assert.Equal(AppleBuildNumberPolicy.IncrementExisting, segment.Configuration.BuildNumberPolicy);
    }
}
