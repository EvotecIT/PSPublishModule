using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

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
            Assert.Empty(logger.Warnings);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Apply_RewritesTokensInsideDeferredMergedPayload()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            var logger = new CollectingLogger();
            var libCore = Directory.CreateDirectory(Path.Combine(root.FullName, "Lib", "Core"));
            File.WriteAllText(Path.Combine(libCore.FullName, moduleName + ".dll"), string.Empty);
            ModuleBootstrapperGenerator.Generate(
                root.FullName,
                moduleName,
                new ExportSet(new[] { "Get-Test" }, Array.Empty<string>(), Array.Empty<string>()),
                new[] { moduleName + ".dll" },
                handleRuntimes: false);
            var psm1Path = Path.Combine(root.FullName, moduleName + ".psm1");
            ModuleBootstrapperGenerator.InlineMergedScriptPayload(
                psm1Path,
                "function Get-Test { '{ModuleVersion}:__TOKEN__' }");

            ModulePsm1PlaceholderApplier.Apply(
                logger,
                psm1Path,
                moduleName,
                moduleVersion: "1.2.3",
                preRelease: null,
                replacements: new[] { new PlaceHolderReplacement { Find = "__TOKEN__", Replace = "done" } },
                options: null);

            var deferredPayload = DecodeDeferredPayload(File.ReadAllText(psm1Path));
            Assert.Contains("'1.2.3:done'", deferredPayload, StringComparison.Ordinal);
            Assert.DoesNotContain("{ModuleVersion}", deferredPayload, StringComparison.Ordinal);
            Assert.DoesNotContain("__TOKEN__", deferredPayload, StringComparison.Ordinal);
            Assert.Empty(logger.Warnings);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Apply_DoesNotReplaceShortTokensInsideReencodedDeferredBase64()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            const string moduleName = "TestModule";
            var logger = new CollectingLogger();
            var libCore = Directory.CreateDirectory(Path.Combine(root.FullName, "Lib", "Core"));
            File.WriteAllText(Path.Combine(libCore.FullName, moduleName + ".dll"), string.Empty);
            ModuleBootstrapperGenerator.Generate(
                root.FullName,
                moduleName,
                new ExportSet(new[] { "Get-Test" }, Array.Empty<string>(), Array.Empty<string>()),
                new[] { moduleName + ".dll" },
                handleRuntimes: false);
            var psm1Path = Path.Combine(root.FullName, moduleName + ".psm1");
            var payload = new StringBuilder();
            for (var index = 0; index <= 100; index++)
                payload.AppendLine($"function Get-Test{index} {{ param([string] $Value) return $Value }}");
            ModuleBootstrapperGenerator.InlineMergedScriptPayload(psm1Path, payload.ToString());
            Assert.Contains("lu", ExtractDeferredEncodedPayload(File.ReadAllText(psm1Path)), StringComparison.Ordinal);

            ModulePsm1PlaceholderApplier.Apply(
                logger,
                psm1Path,
                moduleName,
                moduleVersion: "1.2.3",
                preRelease: null,
                replacements: new[] { new PlaceHolderReplacement { Find = "lu", Replace = "XX" } },
                options: null);

            var content = File.ReadAllText(psm1Path);
            Assert.Contains("$VaXXe", DecodeDeferredPayload(content), StringComparison.Ordinal);
            Assert.Empty(logger.Warnings);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { /* best effort */ }
        }
    }

    [Fact]
    public void Apply_ReturnsSilentlyWhenFileDoesNotExist()
    {
        var logger = new CollectingLogger();

        ModulePsm1PlaceholderApplier.Apply(
            logger,
            psm1Path: Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N"), "Missing.psm1"),
            moduleName: "TestModule",
            moduleVersion: "1.2.3",
            preRelease: null,
            replacements: null,
            options: null);

        Assert.Empty(logger.Warnings);
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

    private static string DecodeDeferredPayload(string bootstrapper)
        => Encoding.UTF8.GetString(Convert.FromBase64String(ExtractDeferredEncodedPayload(bootstrapper)));

    private static string ExtractDeferredEncodedPayload(string bootstrapper)
    {
        const string startMarker = "$PowerForgeMergedScriptPayloadBase64 = @'";
        const string endMarker = "'@";
        var start = bootstrapper.IndexOf(startMarker, StringComparison.Ordinal);
        Assert.True(start >= 0);
        start += startMarker.Length;
        var end = bootstrapper.IndexOf(endMarker, start, StringComparison.Ordinal);
        Assert.True(end > start);
        return bootstrapper.Substring(start, end - start);
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
