using System.Collections;
using PowerForge;

namespace PowerForge.Tests;

public sealed class FileConsistencyConfigurationFactoryTests
{
    [Fact]
    public void Create_parses_scope_project_kind_and_overrides()
    {
        var factory = new FileConsistencyConfigurationFactory();

        var segment = factory.Create(new FileConsistencyConfigurationRequest
        {
            Enable = true,
            FailOnInconsistency = true,
            ProjectKind = PowerForge.ProjectKind.PowerShell,
            ProjectKindSpecified = true,
            Scope = FileConsistencyScope.StagingAndProject,
            ScopeSpecified = true,
            ExcludeDirectories = ["Artefacts", "Ignore"],
            EncodingOverrides = new Hashtable
            {
                [" docs/*.md "] = "utf8",
                ["ps1/*.ps1"] = FileConsistencyEncoding.UTF8BOM
            },
            LineEndingOverrides = new Hashtable
            {
                ["docs/*.md"] = "LF",
                ["ps1/*.ps1"] = FileConsistencyLineEnding.CRLF
            }
        });

        var settings = Assert.IsType<FileConsistencySettings>(segment.Settings);
        Assert.True(settings.Enable);
        Assert.True(settings.FailOnInconsistency);
        Assert.Equal(PowerForge.ProjectKind.PowerShell, settings.ProjectKind);
        Assert.Equal(FileConsistencyScope.StagingAndProject, settings.Scope);
        Assert.Equal(["Artefacts", "Ignore"], settings.ExcludeDirectories);

        var encodings = Assert.IsType<Dictionary<string, FileConsistencyEncoding>>(settings.EncodingOverrides);
        Assert.Equal(FileConsistencyEncoding.UTF8, encodings["docs/*.md"]);
        Assert.Equal(FileConsistencyEncoding.UTF8BOM, encodings["ps1/*.ps1"]);

        var endings = Assert.IsType<Dictionary<string, FileConsistencyLineEnding>>(settings.LineEndingOverrides);
        Assert.Equal(FileConsistencyLineEnding.LF, endings["docs/*.md"]);
        Assert.Equal(FileConsistencyLineEnding.CRLF, endings["ps1/*.ps1"]);
    }

    [Fact]
    public void Create_throws_on_invalid_encoding_override()
    {
        var factory = new FileConsistencyConfigurationFactory();

        var ex = Assert.Throws<ArgumentException>(() => factory.Create(new FileConsistencyConfigurationRequest
        {
            EncodingOverrides = new Hashtable
            {
                ["file.txt"] = 123
            }
        }));

        Assert.Contains("EncodingOverrides", ex.Message, StringComparison.Ordinal);
    }

    [Fact]
    public void Create_throws_on_invalid_line_ending_override()
    {
        var factory = new FileConsistencyConfigurationFactory();

        var ex = Assert.Throws<ArgumentException>(() => factory.Create(new FileConsistencyConfigurationRequest
        {
            LineEndingOverrides = new Hashtable
            {
                ["file.txt"] = "not-a-line-ending"
            }
        }));

        Assert.Contains("LineEndingOverrides", ex.Message, StringComparison.Ordinal);
    }
}
