using System.IO;

namespace PowerForge.Tests;

public sealed class ModuleInformationReaderTests
{
    [Fact]
    public void Read_PrefersPrereleaseFromPsData()
    {
        var projectRoot = CreateModuleProject(
            """
            @{
                RootModule = 'Sample.psm1'
                ModuleVersion = '1.2.3'
                PrivateData = @{
                    PSData = @{
                        Prerelease = 'preview1'
                    }
                }
            }
            """);

        var reader = new ModuleInformationReader();

        var result = reader.Read(projectRoot);

        Assert.Equal("preview1", result.PreRelease);
    }

    [Fact]
    public void Read_FallsBackToTopLevelPrerelease()
    {
        var projectRoot = CreateModuleProject(
            """
            @{
                RootModule = 'Sample.psm1'
                ModuleVersion = '1.2.3'
                Prerelease = 'beta2'
            }
            """);

        var reader = new ModuleInformationReader();

        var result = reader.Read(projectRoot);

        Assert.Equal("beta2", result.PreRelease);
    }

    private static string CreateModuleProject(string manifestContent)
    {
        var projectRoot = Path.Combine(Path.GetTempPath(), "PowerForgeModuleInformationTests", Path.GetRandomFileName());
        Directory.CreateDirectory(projectRoot);

        File.WriteAllText(Path.Combine(projectRoot, "Sample.psd1"), manifestContent);
        File.WriteAllText(Path.Combine(projectRoot, "Sample.psm1"), string.Empty);

        return projectRoot;
    }
}
