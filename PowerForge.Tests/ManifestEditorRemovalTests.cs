using System.Collections;
using System.Management.Automation.Language;

namespace PowerForge.Tests;

public sealed class ManifestEditorRemovalTests
{
    [Theory]
    [InlineData("Prerelease='preview'; Tags=@('keep')")]
    [InlineData("Tags=@('keep'); Prerelease='preview'")]
    [InlineData("Tags=@('keep'); Prerelease='preview'; ProjectUri='https://example.test'")]
    [InlineData("Tags=@('keep')\n Prerelease='preview' # retain this comment\n")]
    public void RemovePsDataKey_PreservesCompactManifestSiblings(string psData)
    {
        var path = Path.Combine(Path.GetTempPath(), "pf-manifest-" + Guid.NewGuid().ToString("N") + ".psd1");
        try
        {
            File.WriteAllText(path, "@{ RootModule='Sample.psm1'; ModuleVersion='1.0.0'; PrivateData=@{ PSData=@{ " + psData + " } } }");
            Assert.True(ManifestEditor.TryRemovePsDataKey(path, "Prerelease"));
            var manifest = ReadManifest(path);
            Assert.Equal("Sample.psm1", manifest["RootModule"]);
            Assert.Equal("1.0.0", manifest["ModuleVersion"]);
            var data = (IDictionary)((IDictionary)manifest["PrivateData"]!)["PSData"]!;
            Assert.False(data.Contains("Prerelease"));
            Assert.Equal("keep", Assert.Single((object[])data["Tags"]!));
            if (psData.Contains("# retain"))
                Assert.Contains("# retain this comment", File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }

    [Theory]
    [InlineData("\r\n")]
    [InlineData("\n")]
    public void RemoveKeys_PreservesNestedTablesAndMultilineValues(string newline)
    {
        var path = Path.Combine(Path.GetTempPath(), "pf-manifest-" + Guid.NewGuid().ToString("N") + ".psd1");
        try
        {
            var source = string.Join(newline, new[]
            {
                "@{", "  RootModule='Sample.psm1'; Description='keep'",
                "  RequiredModules=@(", "    'Dependency'", "  )",
                "  PrivateData=@{ PSData=@{ Repository=@{ Branch='main'; Paths=@('Public') } } }", "}"
            });
            File.WriteAllText(path, source);
            Assert.True(ManifestEditor.TryRemoveTopLevelKey(path, "RequiredModules"));
            Assert.True(ManifestEditor.TryRemovePsDataSubKey(path, "Repository", "Branch"));
            var manifest = ReadManifest(path);
            Assert.Equal("keep", manifest["Description"]);
            Assert.False(manifest.Contains("RequiredModules"));
            var repository = (IDictionary)((IDictionary)((IDictionary)manifest["PrivateData"]!)["PSData"]!)["Repository"]!;
            Assert.False(repository.Contains("Branch"));
            Assert.Equal("Public", Assert.Single((object[])repository["Paths"]!));
            Assert.Contains(newline + "  PrivateData", File.ReadAllText(path));
        }
        finally { File.Delete(path); }
    }

    private static IDictionary ReadManifest(string path)
    {
        var ast = Parser.ParseFile(path, out _, out var errors);
        Assert.Empty(errors);
        return (IDictionary)((HashtableAst)ast.Find(node => node is HashtableAst, false)!).SafeGetValue();
    }
}
