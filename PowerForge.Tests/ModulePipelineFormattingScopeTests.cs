using System.Reflection;
using System.Text;
using System.Text.Json;

namespace PowerForge.Tests;

public sealed class ModulePipelineFormattingScopeTests
{
    [Theory]
    [InlineData(true, true, false, false)] // Merged output does not inherit DefaultPSM1.
    [InlineData(true, true, true, true)]   // Explicit OnMergePSM1 owns merged output.
    [InlineData(true, false, false, true)] // Unmerged staging retains DefaultPSM1.
    [InlineData(true, false, true, true)]  // Explicit merge settings retain precedence.
    [InlineData(false, false, true, true)] // Project files use default settings only.
    public void FormattingRespectsMergedOutputAndProjectScopes(
        bool staging, bool merged, bool enableMerge, bool expectRoot)
    {
        var root = Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        var rootPsm1 = Path.Combine(root, "Example.psm1");
        var nestedPsm1 = Path.Combine(root, "Helper.psm1");
        var manifest = Path.Combine(root, "Example.psd1");
        var original = Encoding.UTF8.GetBytes("function Get-Example { 'value' }\n");
        File.WriteAllBytes(rootPsm1, original);
        File.WriteAllBytes(nestedPsm1, original);
        File.WriteAllText(manifest, "@{ ModuleVersion = '1.0.0' }\n");
        try
        {
            var formatting = new ConfigurationFormattingSegment();
            formatting.Options.Standard.FormatCodePSM1 = new FormatCodeOptions
            {
                Enabled = true,
                FormatterSettings = new FormatterSettingsOptions { IncludeRules = new[] { "PSUseConsistentIndentation" } }
            };
            formatting.Options.Standard.FormatCodePSD1 = new FormatCodeOptions { Enabled = true };
            formatting.Options.Merge.FormatCodePSM1 = new FormatCodeOptions
            {
                Enabled = enableMerge,
                FormatterSettings = new FormatterSettingsOptions { IncludeRules = new[] { "PSUseConsistentWhitespace" } }
            };
            var runner = new RecordingRunner();
            var pipeline = new FormattingPipeline(new NullLogger(), runner);
            var method = typeof(ModulePipelineRunner).GetMethod("FormatPowerShellTree",
                BindingFlags.NonPublic | BindingFlags.Static)!;

            var results = (FormatterResult[])method.Invoke(null,
                new object[] { root, "Example", manifest, staging, merged, formatting, pipeline })!;

            var expected = expectRoot ? new[] { rootPsm1, nestedPsm1, manifest } : new[] { nestedPsm1, manifest };
            Assert.Equal(expected.OrderBy(p => p), results.Select(r => r.Path).OrderBy(p => p));
            Assert.Equal(expected.OrderBy(p => p), runner.Files.OrderBy(p => p));
            Assert.Equal(runner.Files.Count, runner.Files.Distinct().Count());
            if (!expectRoot)
                Assert.Equal(original, File.ReadAllBytes(rootPsm1));
            else
            {
                Assert.StartsWith("\uFEFF", Encoding.UTF8.GetString(File.ReadAllBytes(rootPsm1)));
                Assert.Contains(staging && enableMerge ? "PSUseConsistentWhitespace" : "PSUseConsistentIndentation",
                    runner.Settings[rootPsm1]);
            }
            Assert.Contains("PSUseConsistentIndentation", runner.Settings[nestedPsm1]);
        }
        finally
        {
            Directory.Delete(root, recursive: true);
        }
    }

    private sealed class RecordingRunner : IPowerShellRunner
    {
        internal List<string> Files { get; } = new();
        internal Dictionary<string, string> Settings { get; } = new();

        public PowerShellRunResult Run(PowerShellRunRequest request)
        {
            using var batches = JsonDocument.Parse(File.ReadAllText(request.Arguments[1]));
            foreach (var batch in batches.RootElement.EnumerateArray())
                foreach (var path in batch.GetProperty("Files").EnumerateArray().Select(p => p.GetString()!))
                {
                    Files.Add(path);
                    Settings[path] = batch.GetProperty("SettingsJson").GetString()!;
                }
            return new PowerShellRunResult(0,
                string.Join(Environment.NewLine, Files.Select(p => $"OK::{p}::unchanged")), string.Empty, "stub");
        }
    }
}
