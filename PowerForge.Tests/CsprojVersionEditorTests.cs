using Xunit;

namespace PowerForge.Tests;

public sealed class CsprojVersionEditorTests
{
    [Fact]
    public void TryGetVersion_ReadsPrereleaseVersion()
    {
        var path = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N") + ".csproj");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        try
        {
            File.WriteAllText(
                path,
                "<Project Sdk=\"Microsoft.NET.Sdk\">" + Environment.NewLine +
                "  <PropertyGroup>" + Environment.NewLine +
                "    <Version>1.0.0-preview.3</Version>" + Environment.NewLine +
                "  </PropertyGroup>" + Environment.NewLine +
                "</Project>");

            Assert.True(CsprojVersionEditor.TryGetVersion(path, out var version));
            Assert.Equal("1.0.0-preview.3", version);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Fact]
    public void UpdateVersionText_ReplacesPrereleaseVersionWithoutDuplicatingTag()
    {
        var content =
            "<Project Sdk=\"Microsoft.NET.Sdk\">" + Environment.NewLine +
            "  <PropertyGroup>" + Environment.NewLine +
            "    <VersionPrefix>1.0.0-preview.3</VersionPrefix>" + Environment.NewLine +
            "  </PropertyGroup>" + Environment.NewLine +
            "</Project>";

        var updated = CsprojVersionEditor.UpdateVersionText(content, "2.0.0-rc.1", out var hadVersionTag);

        Assert.True(hadVersionTag);
        Assert.Contains("<VersionPrefix>2.0.0</VersionPrefix>", updated, StringComparison.Ordinal);
        Assert.Contains("<VersionSuffix>rc.1</VersionSuffix>", updated, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(updated, "<VersionPrefix>", StringComparison.Ordinal));
        Assert.DoesNotContain("1.0.0-preview.3", updated, StringComparison.Ordinal);
    }

    [Fact]
    public void TryGetVersion_PrefersVersionPrefixOverSuffixAndInformationalVersion()
    {
        var path = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N") + ".csproj");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);

        try
        {
            File.WriteAllText(path, "<Project><PropertyGroup><VersionPrefix>2.0.0</VersionPrefix><VersionSuffix Condition=\"'$(Configuration)' == 'Debug'\">rc.1</VersionSuffix><InformationalVersion>9.9.9+sha</InformationalVersion></PropertyGroup></Project>");

            Assert.True(CsprojVersionEditor.TryGetVersion(path, out var version));
            Assert.Equal("2.0.0", version);
        }
        finally
        {
            if (File.Exists(path))
                File.Delete(path);
        }
    }

    [Theory]
    [InlineData("<VersionSuffix></VersionSuffix>")]
    [InlineData("<VersionSuffix />")]
    [InlineData("<VersionSuffix Condition=\"'$(Configuration)' == 'Debug'\"></VersionSuffix>")]
    public void UpdateVersionText_ReplacesExistingEmptyVersionSuffix(string suffixElement)
    {
        var content = $"<Project><PropertyGroup><VersionPrefix>1.0.0</VersionPrefix>{suffixElement}</PropertyGroup></Project>";

        var updated = CsprojVersionEditor.UpdateVersionText(content, "2.0.0-rc.1", out _);

        Assert.Contains("<VersionPrefix>2.0.0</VersionPrefix>", updated, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(updated, "<VersionSuffix>", StringComparison.Ordinal));
        Assert.Contains("<VersionSuffix>rc.1</VersionSuffix>", updated, StringComparison.Ordinal);
    }

    [Fact]
    public void UpdateVersionText_KeepsAssemblyAndFileVersionsNumericForPrereleasePackages()
    {
        var content =
            "<Project Sdk=\"Microsoft.NET.Sdk\"><PropertyGroup>" +
            "<Version>1.0.0</Version>" +
            "<PackageVersion>1.0.0</PackageVersion>" +
            "<InformationalVersion>1.0.0</InformationalVersion>" +
            "<AssemblyVersion>1.0.0</AssemblyVersion>" +
            "<FileVersion>1.0.0</FileVersion>" +
            "</PropertyGroup></Project>";

        var updated = CsprojVersionEditor.UpdateVersionText(content, "2.1.0-beta.1", out var hadVersionTag);

        Assert.True(hadVersionTag);
        Assert.Contains("<Version>2.1.0-beta.1</Version>", updated, StringComparison.Ordinal);
        Assert.Contains("<PackageVersion>2.1.0-beta.1</PackageVersion>", updated, StringComparison.Ordinal);
        Assert.Contains("<InformationalVersion>2.1.0-beta.1</InformationalVersion>", updated, StringComparison.Ordinal);
        Assert.Contains("<AssemblyVersion>2.1.0</AssemblyVersion>", updated, StringComparison.Ordinal);
        Assert.Contains("<FileVersion>2.1.0</FileVersion>", updated, StringComparison.Ordinal);
    }

    private static int CountOccurrences(string input, string value, StringComparison comparison)
    {
        var count = 0;
        var index = 0;
        while ((index = input.IndexOf(value, index, comparison)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}
