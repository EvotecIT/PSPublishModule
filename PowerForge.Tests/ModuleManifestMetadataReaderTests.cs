using PowerForge;

namespace PowerForge.Tests;

public sealed class ModuleManifestMetadataReaderTests
{
    [Fact]
    public void Read_ResolvesVersionRootModuleAndPrerelease()
    {
        var directory = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        var manifestPath = Path.Combine(directory.FullName, "PSPublishModule.psd1");
        File.WriteAllText(
            manifestPath,
            """
            @{
                RootModule = 'PSPublishModule.psm1'
                ModuleVersion = '2.1.0'
                PrivateData = @{
                    PSData = @{
                        Prerelease = 'preview3'
                    }
                }
            }
            """);

        try
        {
            var metadata = new ModuleManifestMetadataReader().Read(manifestPath);

            Assert.Equal("PSPublishModule", metadata.ModuleName);
            Assert.Equal("2.1.0", metadata.ModuleVersion);
            Assert.Equal("preview3", metadata.PreRelease);
        }
        finally
        {
            try { Directory.Delete(directory.FullName, recursive: true); } catch { }
        }
    }
}
