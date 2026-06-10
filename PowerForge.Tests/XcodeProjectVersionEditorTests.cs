using System;
using System.IO;
using Xunit;

namespace PowerForge.Tests;

public sealed class XcodeProjectVersionEditorTests
{
    [Fact]
    public void ReadText_ReturnsDistinctMarketingAndBuildVersions()
    {
        const string content = """
            MARKETING_VERSION = 1.0.0;
            CURRENT_PROJECT_VERSION = 3;
            MARKETING_VERSION = 1.0.0;
            CURRENT_PROJECT_VERSION = 3;
            """;

        var info = XcodeProjectVersionEditor.ReadText("project.pbxproj", content);

        Assert.True(info.IsConsistent);
        Assert.Equal("1.0.0", info.MarketingVersion);
        Assert.Equal("3", info.BuildNumber);
    }

    [Fact]
    public void ReadText_FlagsDriftWhenTargetsDisagree()
    {
        const string content = """
            MARKETING_VERSION = 1.0;
            CURRENT_PROJECT_VERSION = 3;
            MARKETING_VERSION = 1.0.0;
            CURRENT_PROJECT_VERSION = 4;
            """;

        var info = XcodeProjectVersionEditor.ReadText("project.pbxproj", content);

        Assert.False(info.IsConsistent);
        Assert.Null(info.MarketingVersion);
        Assert.Null(info.BuildNumber);
        Assert.Equal(new[] { "1.0", "1.0.0" }, info.MarketingVersions);
        Assert.Equal(new[] { "3", "4" }, info.BuildNumbers);
    }

    [Fact]
    public void UpdateVersionText_UpdatesAllMarketingAndBuildValues()
    {
        const string content = """
            MARKETING_VERSION = 1.0;
            CURRENT_PROJECT_VERSION = 2;
            MARKETING_VERSION = 1.0;
            CURRENT_PROJECT_VERSION = 2;
            """;

        var updated = XcodeProjectVersionEditor.UpdateVersionText(content, "1.0.0", "3", out var changed);

        Assert.True(changed);
        Assert.DoesNotContain("MARKETING_VERSION = 1.0;", updated, StringComparison.Ordinal);
        Assert.DoesNotContain("CURRENT_PROJECT_VERSION = 2;", updated, StringComparison.Ordinal);
        Assert.Equal(2, Count(updated, "MARKETING_VERSION = 1.0.0;"));
        Assert.Equal(2, Count(updated, "CURRENT_PROJECT_VERSION = 3;"));
    }

    [Fact]
    public void Update_WritesResolvedPbxprojInsideXcodeProjectDirectory()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var xcodeproj = Directory.CreateDirectory(Path.Combine(root.FullName, "Tactra.xcodeproj"));
            var pbxproj = Path.Combine(xcodeproj.FullName, "project.pbxproj");
            File.WriteAllText(
                pbxproj,
                """
                    MARKETING_VERSION = 1.0;
                    CURRENT_PROJECT_VERSION = 2;
                    """);

            var result = new XcodeProjectVersionEditor().Update(xcodeproj.FullName, "1.0.0", "3");

            Assert.True(result.Changed);
            Assert.Equal(pbxproj, result.ProjectFilePath);
            Assert.Equal("1.0", result.Before.MarketingVersion);
            Assert.Equal("2", result.Before.BuildNumber);
            Assert.Equal("1.0.0", result.After.MarketingVersion);
            Assert.Equal("3", result.After.BuildNumber);

            var written = File.ReadAllText(pbxproj);
            Assert.Contains("MARKETING_VERSION = 1.0.0;", written, StringComparison.Ordinal);
            Assert.Contains("CURRENT_PROJECT_VERSION = 3;", written, StringComparison.Ordinal);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Update_WhatIfDoesNotWriteFile()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var pbxproj = Path.Combine(root.FullName, "project.pbxproj");
            File.WriteAllText(
                pbxproj,
                """
                    MARKETING_VERSION = 1.0;
                    CURRENT_PROJECT_VERSION = 2;
                    """);

            var result = new XcodeProjectVersionEditor().Update(pbxproj, "1.0.0", "3", whatIf: true);

            Assert.True(result.Changed);
            Assert.True(result.WhatIf);
            Assert.Equal("1.0.0", result.After.MarketingVersion);
            Assert.Equal("3", result.After.BuildNumber);

            var written = File.ReadAllText(pbxproj);
            Assert.Contains("MARKETING_VERSION = 1.0;", written, StringComparison.Ordinal);
            Assert.Contains("CURRENT_PROJECT_VERSION = 2;", written, StringComparison.Ordinal);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Update_ThrowsWhenMarketingVersionIsMissing()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var pbxproj = Path.Combine(root.FullName, "project.pbxproj");
            File.WriteAllText(pbxproj, "CURRENT_PROJECT_VERSION = 2;");

            var ex = Assert.Throws<InvalidOperationException>(
                () => new XcodeProjectVersionEditor().Update(pbxproj, "1.0.0", "3"));

            Assert.Contains("No MARKETING_VERSION entries", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void Update_ThrowsWhenBuildNumberIsRequestedButMissing()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var pbxproj = Path.Combine(root.FullName, "project.pbxproj");
            File.WriteAllText(pbxproj, "MARKETING_VERSION = 1.0;");

            var ex = Assert.Throws<InvalidOperationException>(
                () => new XcodeProjectVersionEditor().Update(pbxproj, "1.0.0", "3"));

            Assert.Contains("No CURRENT_PROJECT_VERSION entries", ex.Message, StringComparison.Ordinal);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    private static int Count(string text, string value)
    {
        var count = 0;
        var index = 0;
        while ((index = text.IndexOf(value, index, StringComparison.Ordinal)) >= 0)
        {
            count++;
            index += value.Length;
        }

        return count;
    }
}
