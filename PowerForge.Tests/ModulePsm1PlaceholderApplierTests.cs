using System;
using System.Collections.Generic;
using System.IO;

namespace PowerForge.Tests;

public sealed class ModulePsm1PlaceholderApplierTests
{
    [Fact]
    public void Apply_ReplacesBuiltinAndCustomTokens()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var logger = new CollectingLogger();
            var psm1Path = Path.Combine(root.FullName, "TestModule.psm1");

            File.WriteAllText(
                psm1Path,
                "Name={ModuleName};Version=<ModuleVersion>;Tag=<TagModuleVersionWithPreRelease>;Custom=__TOKEN__");

            ModulePsm1PlaceholderApplier.Apply(
                logger,
                psm1Path,
                moduleName: "TestModule",
                moduleVersion: "1.2.3",
                preRelease: "preview1",
                replacements: new[]
                {
                    new PlaceHolderReplacement
                    {
                        Find = "__TOKEN__",
                        Replace = "done"
                    }
                },
                options: null);

            var updated = File.ReadAllText(psm1Path);
            Assert.Contains("Name=TestModule", updated, StringComparison.Ordinal);
            Assert.Contains("Version=1.2.3", updated, StringComparison.Ordinal);
            Assert.Contains("Tag=v1.2.3-preview1", updated, StringComparison.Ordinal);
            Assert.Contains("Custom=done", updated, StringComparison.Ordinal);
            Assert.Empty(logger.Warnings);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Apply_HonorsSkipBuiltinReplacements()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            var logger = new CollectingLogger();
            var psm1Path = Path.Combine(root.FullName, "TestModule.psm1");

            File.WriteAllText(psm1Path, "BuiltIn={ModuleName};Custom=__TOKEN__");

            ModulePsm1PlaceholderApplier.Apply(
                logger,
                psm1Path,
                moduleName: "TestModule",
                moduleVersion: "1.2.3",
                preRelease: null,
                replacements: new[]
                {
                    new PlaceHolderReplacement
                    {
                        Find = "__TOKEN__",
                        Replace = "done"
                    }
                },
                options: new PlaceHolderOptionConfiguration
                {
                    SkipBuiltinReplacements = true
                });

            var updated = File.ReadAllText(psm1Path);
            Assert.Contains("BuiltIn={ModuleName}", updated, StringComparison.Ordinal);
            Assert.Contains("Custom=done", updated, StringComparison.Ordinal);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void BuildReplacements_OmitsBuiltinTokensWhenRequested()
    {
        var replacements = ModulePsm1PlaceholderApplier.BuildReplacements(
            moduleName: "TestModule",
            moduleVersion: "1.2.3",
            preRelease: "preview1",
            replacements: new List<PlaceHolderReplacement>
            {
                new()
                {
                    Find = "__TOKEN__",
                    Replace = "done"
                }
            },
            skipBuiltinReplacements: true);

        Assert.Single(replacements);
        Assert.Equal("__TOKEN__", replacements[0].Find);
        Assert.Equal("done", replacements[0].Replace);
    }

    private sealed class CollectingLogger : ILogger
    {
        public List<string> Warnings { get; } = new();

        public bool IsVerbose => false;

        public void Info(string message)
        {
        }

        public void Success(string message)
        {
        }

        public void Warn(string message) => Warnings.Add(message ?? string.Empty);

        public void Error(string message)
        {
        }

        public void Verbose(string message)
        {
        }
    }
}
