using PowerForge;

namespace PowerForge.Net472SmokeTests;

public sealed class ArtefactConfigurationFactoryNet472SmokeTests
{
    [Fact]
    public void Create_NormalizesWindowsPathsUnderNet472()
    {
        var factory = new ArtefactConfigurationFactory(new NullLogger());

        var artefact = factory.Create(new ArtefactConfigurationRequest {
            Type = ArtefactType.Unpacked,
            Path = "output/packages",
            RequiredModulesPath = "dependencies/modules",
            ModulesPath = "module/content",
            CopyFiles = new[] {
                new ArtefactCopyMapping {
                    Source = "src/file.txt",
                    Destination = "dest/file.txt"
                }
            }
        });

        Assert.Equal(@"output\packages", artefact.Configuration.Path);
        Assert.Equal(@"dependencies\modules", artefact.Configuration.RequiredModules.Path);
        Assert.Equal(@"module\content", artefact.Configuration.RequiredModules.ModulesPath);
        Assert.NotNull(artefact.Configuration.FilesOutput);
        Assert.Single(artefact.Configuration.FilesOutput!);
        Assert.Equal(@"src\file.txt", artefact.Configuration.FilesOutput![0].Source);
        Assert.Equal(@"dest\file.txt", artefact.Configuration.FilesOutput![0].Destination);
    }
}
