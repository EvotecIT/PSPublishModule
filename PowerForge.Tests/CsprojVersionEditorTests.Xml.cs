using System.Xml.Linq;

namespace PowerForge.Tests;

public sealed partial class CsprojVersionEditorTests
{
    [Theory]
    [InlineData("<PropertyGroup><VersionPrefix>1.0.0</VersionPrefix><VersionSuffix Condition=\"'$(Configuration)' == 'Debug'\">dev</VersionSuffix></PropertyGroup>", "2.0.0-rc.1")]
    [InlineData("<PropertyGroup><VersionPrefix>1.0.0</VersionPrefix></PropertyGroup><PropertyGroup Condition=\"'$(Configuration)' == 'Debug'\"><VersionSuffix>dev</VersionSuffix></PropertyGroup>", "2.0.0")]
    [InlineData("<PropertyGroup><Version Condition=\"'$(Configuration)' == 'Debug'\">1.0.0-dev</Version></PropertyGroup>", "2.0.0-rc.1")]
    [InlineData("<PropertyGroup Condition=\"'$(Configuration)' == 'Debug'\"><VersionPrefix>1.0.0</VersionPrefix></PropertyGroup>", "2.0.0-rc.1")]
    public void UpdateVersionText_ConditionalDefinitionsRetainAnEffectiveDefault(string declarations, string version)
    {
        var document = XDocument.Parse(CsprojVersionEditor.UpdateVersionText("<Project>" + declarations + "</Project>", version, out _));
        var defaults = document.Root!.Elements("PropertyGroup").Where(group => group.Attribute("Condition") is null)
            .Elements().Where(property => property.Attribute("Condition") is null).ToArray();
        var full = defaults.SingleOrDefault(property => property.Name.LocalName == "Version");
        var actual = full?.Value ?? defaults.Single(property => property.Name.LocalName == "VersionPrefix").Value;
        if (full is null && defaults.SingleOrDefault(property => property.Name.LocalName == "VersionSuffix") is { Value.Length: > 0 } suffix)
            actual += "-" + suffix.Value;
        Assert.Equal(version, actual);
        Assert.Contains(document.Descendants(), property => property.Attribute("Condition")?.Value == "'$(Configuration)' == 'Debug'");
    }

    [Theory]
    [InlineData("<ItemGroup><PackageReference Include=\"Existing.Dependency\"><Version>1.2.3</Version></PackageReference></ItemGroup>")]
    [InlineData("<Target Name=\"SetToolVersion\"><PropertyGroup><Version>1.2.3</Version></PropertyGroup></Target>")]
    [InlineData("<!-- <PropertyGroup><Version>1.2.3</Version></PropertyGroup> -->")]
    [InlineData("<PropertyGroup><ToolSettings><PropertyGroup><Version>1.2.3</Version></PropertyGroup></ToolSettings></PropertyGroup>")]
    public void UpdateVersionText_PreservesNonEvaluationVersionValues(string unrelated)
    {
        var original = "<Project><PropertyGroup><Version>2.0.0</Version></PropertyGroup>" + unrelated + "</Project>";

        var updated = CsprojVersionEditor.UpdateVersionText(original, "2.0.1", out var hadVersion);

        Assert.True(hadVersion);
        Assert.Equal(original.Replace("<Version>2.0.0", "<Version>2.0.1", StringComparison.Ordinal), updated);
    }

    [Theory]
    [InlineData("<Project />")]
    [InlineData("<Project><PropertyGroup /></Project>")]
    [InlineData("<Project><PropertyGroup></PropertyGroup></Project>")]
    [InlineData("<Project><ItemGroup><PackageReference Include=\"Dependency\"><Version>1.2.3</Version></PackageReference></ItemGroup></Project>")]
    public void UpdateVersionText_InsertsARealProjectProperty(string original)
    {
        var updated = CsprojVersionEditor.UpdateVersionText(original, "2.0.1", out var hadVersion);

        Assert.False(hadVersion);
        var document = XDocument.Parse(updated);
        Assert.Equal("2.0.1", Assert.Single(document.Root!.Elements("PropertyGroup").Elements("VersionPrefix")).Value);
        if (original.Contains("PackageReference", StringComparison.Ordinal))
            Assert.Equal("1.2.3", Assert.Single(document.Descendants("PackageReference").Elements("Version")).Value);
    }

    [Fact]
    public void TryGetPackageVersion_IgnoresDependencyMetadataAndReadsCdata()
    {
        var path = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N") + ".csproj");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        try
        {
            File.WriteAllText(path, "<Project><ItemGroup><PackageReference Include=\"Dependency\"><Version>1.2.3</Version></PackageReference></ItemGroup><PropertyGroup><Version><![CDATA[2.0.0]]></Version></PropertyGroup></Project>");
            Assert.True(CsprojVersionEditor.TryGetPackageVersion(path, out var version));
            Assert.Equal("2.0.0", version);
        }
        finally { File.Delete(path); }
    }
}
