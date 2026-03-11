using PowerForge;

namespace PowerForge.Tests;

public sealed class InformationConfigurationFactoryTests
{
    [Fact]
    public void Create_normalizes_include_to_array_entries()
    {
        var factory = new InformationConfigurationFactory();

        var segment = factory.Create(new InformationConfigurationRequest
        {
            FunctionsToExportFolder = "Public",
            IncludeCustomCode = "Write-Host 'hello'",
            IncludeToArray =
            [
                new IncludeToArrayEntry
                {
                    Key = " Docs ",
                    Values = [" README.md ", " CHANGELOG.md "]
                },
                new IncludeToArrayEntry
                {
                    Key = " ",
                    Values = ["ignored"]
                },
                new IncludeToArrayEntry
                {
                    Key = "Empty",
                    Values = [" ", ""]
                }
            ]
        });

        var config = Assert.IsType<InformationConfiguration>(segment.Configuration);
        Assert.Equal("Public", config.FunctionsToExportFolder);
        Assert.Equal("Write-Host 'hello'", config.IncludeCustomCode);

        var entries = Assert.IsType<IncludeToArrayEntry[]>(config.IncludeToArray);
        var entry = Assert.Single(entries);
        Assert.Equal("Docs", entry.Key);
        Assert.Equal(["README.md", "CHANGELOG.md"], entry.Values);
    }

    [Fact]
    public void Create_returns_null_include_to_array_when_entries_do_not_resolve()
    {
        var factory = new InformationConfigurationFactory();

        var segment = factory.Create(new InformationConfigurationRequest
        {
            IncludeToArray =
            [
                new IncludeToArrayEntry
                {
                    Key = " ",
                    Values = ["x"]
                },
                new IncludeToArrayEntry
                {
                    Key = "Docs",
                    Values = [" "]
                }
            ]
        });

        var config = Assert.IsType<InformationConfiguration>(segment.Configuration);
        Assert.Null(config.IncludeToArray);
    }
}
