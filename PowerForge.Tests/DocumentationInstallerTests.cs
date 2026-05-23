using System;
using System.Collections.Generic;
using System.IO;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

public class DocumentationInstallerTests
{
    [Fact]
    public void GetIntroLines_Uses_IntroText_Before_IntroFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "PFIntro_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllText(Path.Combine(root, "INTRO.md"), "from file");

        try
        {
            var delivery = new Dictionary<string, object?>
            {
                ["IntroText"] = new[] { "line 1", "line 2" },
                ["IntroFile"] = "INTRO.md"
            };

            var lines = DocumentationInstaller.GetIntroLinesForTesting(root, delivery);

            Assert.Equal(new[] { "line 1", "line 2" }, lines);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }

    [Fact]
    public void GetIntroLines_Falls_Back_To_IntroFile()
    {
        var root = Path.Combine(Path.GetTempPath(), "PFIntro_" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        File.WriteAllLines(Path.Combine(root, "INTRO.md"), new[] { "from file", "second" });

        try
        {
            var delivery = new Dictionary<string, object?>
            {
                ["IntroFile"] = "INTRO.md"
            };

            var lines = DocumentationInstaller.GetIntroLinesForTesting(root, delivery);

            Assert.Equal(new[] { "from file", "second" }, lines);
        }
        finally
        {
            try { Directory.Delete(root, recursive: true); } catch { }
        }
    }
}
