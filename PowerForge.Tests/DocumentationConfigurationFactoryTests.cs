using PowerForge;

namespace PowerForge.Tests;

public sealed class DocumentationConfigurationFactoryTests
{
    [Fact]
    public void Create_emits_only_documentation_segment_when_no_build_settings_requested()
    {
        var factory = new DocumentationConfigurationFactory();

        var segments = factory.Create(new DocumentationConfigurationRequest
        {
            Path = "Docs",
            PathReadme = "Docs\\Readme.md"
        });

        var documentation = Assert.IsType<ConfigurationDocumentationSegment>(Assert.Single(segments));
        Assert.Equal("Docs", documentation.Configuration.Path);
        Assert.Equal("Docs\\Readme.md", documentation.Configuration.PathReadme);
    }

    [Fact]
    public void Create_emits_build_documentation_segment_when_options_are_requested()
    {
        var factory = new DocumentationConfigurationFactory();

        var segments = factory.Create(new DocumentationConfigurationRequest
        {
            Enable = true,
            StartClean = true,
            UpdateWhenNew = true,
            SyncExternalHelpToProjectRoot = true,
            SkipAboutTopics = true,
            SkipFallbackExamples = true,
            SkipExternalHelp = true,
            ExternalHelpCulture = "pl-PL",
            ExternalHelpCultureSpecified = true,
            ExternalHelpFileName = "custom-help.xml",
            ExternalHelpFileNameSpecified = true,
            AboutTopicsSourcePath = ["Help\\About", "Internals\\About"],
            AboutTopicsSourcePathSpecified = true,
            Path = "Docs",
            PathReadme = "Docs\\Readme.md"
        });

        Assert.Equal(2, segments.Count);
        var documentation = Assert.IsType<ConfigurationDocumentationSegment>(segments[0]);
        Assert.Equal("Docs", documentation.Configuration.Path);

        var buildDocumentation = Assert.IsType<ConfigurationBuildDocumentationSegment>(segments[1]);
        Assert.True(buildDocumentation.Configuration.Enable);
        Assert.True(buildDocumentation.Configuration.StartClean);
        Assert.True(buildDocumentation.Configuration.UpdateWhenNew);
        Assert.True(buildDocumentation.Configuration.SyncExternalHelpToProjectRoot);
        Assert.False(buildDocumentation.Configuration.IncludeAboutTopics);
        Assert.False(buildDocumentation.Configuration.GenerateFallbackExamples);
        Assert.False(buildDocumentation.Configuration.GenerateExternalHelp);
        Assert.Equal("pl-PL", buildDocumentation.Configuration.ExternalHelpCulture);
        Assert.Equal("custom-help.xml", buildDocumentation.Configuration.ExternalHelpFileName);
        Assert.Equal(["Help\\About", "Internals\\About"], buildDocumentation.Configuration.AboutTopicsSourcePath);
    }
}
