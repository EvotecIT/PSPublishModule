using PowerForge;

namespace PowerForge.Tests;

public sealed class ArtefactBuilderModuleLoaderPackagingTests
{
    // The generated bootstrapper loads Loader/<edition>/PowerForge.ModuleLoader.V1.dll, so packages always carry that
    // exact folder. On a case-sensitive file system an IncludeAll entry such as 'loader' names a different folder.
    [Fact]
    public void ResolveModulePackageSourceFiles_AlwaysShipsTheExactLoaderFolder()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-loader-package-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "Demo.psd1"), "@{ RootModule = 'Demo.psm1'; ModuleVersion = '1.0.0' }");
            File.WriteAllText(Path.Combine(root, "Demo.psm1"), "# bootstrapper");
            ModuleLoaderAssets.Write(Path.Combine(root, ModuleLoaderAssets.PackagedFolderName));

            var files = ArtefactBuilder.ResolveModulePackageSourceFiles(root,
                    new InformationConfiguration { IncludeAll = new[] { "Lib", "loader" } }, delivery: null)
                .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
                .ToArray();

            Assert.Contains("Loader/Core/PowerForge.ModuleLoader.V1.dll", files);
            Assert.Contains("Loader/Desktop/PowerForge.ModuleLoader.V1.dll", files);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
