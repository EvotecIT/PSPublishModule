using PowerForge;

namespace PowerForge.Tests;

public sealed class ArtefactBuilderManifestLoadedFilesTests
{
    // A format file outside the default root includes used to be dropped silently, so the packaged module
    // failed at Import-Module. Manifest-loaded files now ship whatever the include patterns say, and a
    // manifest entry with no file stops packaging with the key and the name.
    [Fact]
    public void ResolveModulePackageSourceFiles_ShipsManifestLoadedFilesAndRejectsMissingOnes()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-manifest-loaded-" + Guid.NewGuid().ToString("N"))).FullName;
        try
        {
            File.WriteAllText(Path.Combine(root, "Demo.psd1"),
                "@{ RootModule = 'Demo.psm1'; ModuleVersion = '1.0.0'; FormatsToProcess = @('Demo.Format.ps1xml'); TypesToProcess = 'Types\\Demo.Types.ps1xml'; RequiredAssemblies = @('System.Xml') }");
            File.WriteAllText(Path.Combine(root, "Demo.psm1"), "function Get-Demo { }");
            File.WriteAllText(Path.Combine(root, "Demo.Format.ps1xml"), "<Configuration />");
            Directory.CreateDirectory(Path.Combine(root, "Types"));
            File.WriteAllText(Path.Combine(root, "Types", "Demo.Types.ps1xml"), "<Types />");
            File.WriteAllText(Path.Combine(root, "Notes.txt"), "not loaded by the manifest");

            var files = ArtefactBuilder.ResolveModulePackageSourceFiles(root, information: null, delivery: null)
                .Select(path => Path.GetRelativePath(root, path).Replace('\\', '/'))
                .ToArray();

            Assert.Contains("Demo.Format.ps1xml", files);
            Assert.Contains("Types/Demo.Types.ps1xml", files);
            Assert.DoesNotContain("Notes.txt", files);

            File.Delete(Path.Combine(root, "Demo.Format.ps1xml"));
            var error = Assert.Throws<InvalidOperationException>(() =>
                ArtefactBuilder.ResolveModulePackageSourceFiles(root, information: null, delivery: null));
            Assert.Equal(
                "Module manifest 'Demo.psd1' loads files that are not in the staged module: FormatsToProcess 'Demo.Format.ps1xml'. Add them to the module or remove them from the manifest.",
                error.Message);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }
}
