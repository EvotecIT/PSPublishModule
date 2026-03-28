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
        Assert.Contains("<VersionPrefix>2.0.0-rc.1</VersionPrefix>", updated, StringComparison.Ordinal);
        Assert.Equal(1, CountOccurrences(updated, "<VersionPrefix>", StringComparison.Ordinal));
        Assert.DoesNotContain("1.0.0-preview.3", updated, StringComparison.Ordinal);
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
