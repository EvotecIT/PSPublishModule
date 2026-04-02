using System;
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

    [Fact]
    public void Read_ParsesCoreManifestMetadataWithoutPowerShellAst()
    {
        var guid = Guid.NewGuid();
        var projectRoot = CreateModuleProject(
            $$"""
            @{
                RootModule = 'Sample.psm1'
                ModuleVersion = '1.2.3'
                PowerShellVersion = '7.4'
                GUID = '{{guid}}'
                PrivateData = @{
                    PSData = @{
                        Prerelease = 'preview2'
                    }
                }
                RequiredModules = @(
                    'Pester'
                    @{
                        ModuleName = 'PSWriteColor'
                        ModuleVersion = '1.0.0'
                        Guid = '2fd9fdd0-9e34-4eb1-a5ec-13a8b53d7d49'
                    }
                )
            }
            """);

        var reader = new ModuleInformationReader();

        var result = reader.Read(projectRoot);

        Assert.Equal("Sample", result.ModuleName);
        Assert.Equal("1.2.3", result.ModuleVersion);
        Assert.Equal("Sample.psm1", result.RootModule);
        Assert.Equal("7.4", result.PowerShellVersion);
        Assert.Equal("preview2", result.PreRelease);
        Assert.Equal(guid, result.Guid);
        Assert.Equal(2, result.RequiredModules.Length);
        Assert.Equal("Pester", result.RequiredModules[0].ModuleName);
        Assert.Equal("PSWriteColor", result.RequiredModules[1].ModuleName);
        Assert.Equal("1.0.0", result.RequiredModules[1].ModuleVersion);
        Assert.Equal("2fd9fdd0-9e34-4eb1-a5ec-13a8b53d7d49", result.RequiredModules[1].Guid);
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
