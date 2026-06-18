using System.Text.Json;
using Xunit;

namespace PowerForge.Tests;

public sealed class ConfigurationSegmentJsonConverterTests
{
    [Fact]
    public void Deserialize_ReadsExecuteSegment()
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
                  "Type": "Execute",
                  "Configuration": {
                    "Name": "Inspect staged module",
                    "At": "AfterStaging",
                    "InlineScript": "$ctx = Get-Content $env:POWERFORGE_CONTEXT | ConvertFrom-Json",
                    "WorkingDirectory": ".",
                    "Environment": {
                      "POWERFORGE_SAMPLE": "true"
                    },
                    "TimeoutSeconds": 30,
                    "ContinueOnError": true
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
        var segment = Assert.IsType<ConfigurationActionSegment>(Assert.Single(spec!.Segments));
        Assert.Equal("Inspect staged module", segment.Configuration.Name);
        Assert.Equal(ModulePipelineActionStage.AfterStaging, segment.Configuration.At);
        Assert.NotNull(segment.Configuration.InlineScript);
        Assert.Equal(".", segment.Configuration.WorkingDirectory);
        Assert.Equal("true", segment.Configuration.Environment?["POWERFORGE_SAMPLE"]);
        Assert.Equal(30, segment.Configuration.TimeoutSeconds);
        Assert.True(segment.Configuration.ContinueOnError);
    }

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

    [Fact]
    public void Deserialize_ReadsPackageBuildSegments()
    {
        const string json = """
            {
              "Build": {
                "Name": "PSParseHTML",
                "SourcePath": ".",
                "Version": "1.0.0"
              },
              "Install": {
                "Enabled": false
              },
              "Segments": [
                {
                  "Type": "ProjectBuild",
                  "Configuration": {
                    "Name": "Libraries",
                    "ConfigPath": "Build/project.build.json",
                    "BuildBeforeModule": true,
                    "UseAsReleaseVersionSource": true,
                    "ProvideLocalNuGetFeed": true
                  }
                },
                {
                  "Type": "PackageBuild",
                  "Configuration": {
                    "RootPath": "Sources",
                    "ExpectedVersionMap": {
                      "HtmlTinkerX": "2.0.X"
                    },
                    "BuildBeforeModule": true,
                    "PublishNuget": true
                  }
                },
                {
                  "Type": "Release",
                  "Configuration": {
                    "StageRoot": "Artifacts/Release",
                    "VersionSource": "PackageBuild",
                    "PrimaryProject": "HtmlTinkerX"
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
        var projectBuild = Assert.IsType<ConfigurationProjectBuildSegment>(spec!.Segments[0]);
        Assert.Equal("Build/project.build.json", projectBuild.Configuration.ConfigPath);
        Assert.True(projectBuild.Configuration.BuildBeforeModule);
        Assert.True(projectBuild.Configuration.UseAsReleaseVersionSource);
        Assert.True(projectBuild.Configuration.ProvideLocalNuGetFeed);

        var packageBuild = Assert.IsType<ConfigurationPackageBuildSegment>(spec.Segments[1]);
        Assert.Equal("Sources", packageBuild.Configuration.RootPath);
        Assert.Equal("2.0.X", packageBuild.Configuration.ExpectedVersionMap?["HtmlTinkerX"]);
        Assert.True(packageBuild.Configuration.BuildBeforeModule);
        Assert.True(packageBuild.Configuration.PublishNuget);

        var release = Assert.IsType<ConfigurationReleaseSegment>(spec.Segments[2]);
        Assert.Equal("Artifacts/Release", release.Configuration.StageRoot);
        Assert.Equal(ReleaseVersionSource.PackageBuild, release.Configuration.VersionSource);
        Assert.Equal("HtmlTinkerX", release.Configuration.PrimaryProject);
    }
}
