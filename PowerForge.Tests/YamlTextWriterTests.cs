using System;
using Xunit;

namespace PowerForge.Tests;

public class YamlTextWriterTests
{
    [Fact]
    public void WriteSequenceAndNestedScalars_UseStableYamlLayout()
    {
        var writer = new YamlTextWriter();
        writer.WriteScalar("PackageIdentifier", "EvotecIT.IntelligenceX.Tray");
        writer.WriteKey("Installers");
        writer.WriteSequenceItem("Architecture", "x64");
        using (writer.Indent())
        {
            writer.WriteScalar("InstallerUrl", "https://example.test/downloads/tray raw.zip");
            writer.WriteScalar("InstallerSha256", "ABC123");
            writer.WriteKey("NestedInstallerFiles");
            writer.WriteSequenceItem("RelativeFilePath", @"IntelligenceX Tray\IntelligenceX.Tray.exe");
        }

        var yaml = writer.ToString();

        Assert.Equal(
            """
            PackageIdentifier: EvotecIT.IntelligenceX.Tray
            Installers:
            - Architecture: x64
              InstallerUrl: "https://example.test/downloads/tray raw.zip"
              InstallerSha256: ABC123
              NestedInstallerFiles:
              - RelativeFilePath: "IntelligenceX Tray\\IntelligenceX.Tray.exe"
            """
            .ReplaceLineEndings(Environment.NewLine) + Environment.NewLine,
            yaml);
    }

    [Fact]
    public void WriteSequence_SkipsBlankItems_AndEscapesSpecialValues()
    {
        var writer = new YamlTextWriter();
        writer.WriteSequence("Tags", new[] { "stable", "my-package", "-leading-dash", "- leading dash", "needs:quote", "", "two words" });

        var yaml = writer.ToString();

        Assert.Equal(
            """
            Tags:
            - stable
            - my-package
            - -leading-dash
            - "- leading dash"
            - "needs:quote"
            - "two words"
            """
            .ReplaceLineEndings(Environment.NewLine) + Environment.NewLine,
            yaml);
    }
}
