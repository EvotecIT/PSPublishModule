using System.Collections;
using System.Text;
using PowerForge;

namespace PowerForge.Tests;

public sealed class ProjectConsistencyWorkflowServiceTests
{
    [Fact]
    public void Analyze_filters_files_by_project_type_and_returns_resolved_patterns()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-consistency-" + Guid.NewGuid().ToString("N")));

        try
        {
            File.WriteAllText(Path.Combine(root.FullName, "Module.ps1"), "Write-Host 'ok'", new UTF8Encoding(true));
            File.WriteAllText(Path.Combine(root.FullName, "notes.txt"), "ignore me", new UTF8Encoding(false));

            var service = new ProjectConsistencyWorkflowService(new NullLogger());
            var result = service.Analyze(new ProjectConsistencyWorkflowRequest
            {
                Path = root.FullName,
                ProjectType = "PowerShell",
                IncludeDetails = true
            });

            Assert.Equal(Path.GetFullPath(root.FullName), result.RootPath);
            Assert.Contains("*.ps1", result.Patterns, StringComparer.OrdinalIgnoreCase);
            Assert.Equal(ProjectKind.PowerShell, result.Kind);
            Assert.Equal(1, result.Report.Summary.TotalFiles);
            Assert.Single(result.Report.Files!);
            Assert.Equal("Module.ps1", result.Report.Files![0].RelativePath);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void ConvertAndAnalyze_runs_both_conversions_when_no_specific_switch_is_provided()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "pf-convert-" + Guid.NewGuid().ToString("N")));

        try
        {
            var filePath = Path.Combine(root.FullName, "Module.ps1");
            File.WriteAllText(filePath, "function Test-Me {\n    'ok'\n}\n", new UTF8Encoding(false));

            var service = new ProjectConsistencyWorkflowService(new NullLogger());
            var result = service.ConvertAndAnalyze(new ProjectConsistencyWorkflowRequest
            {
                Path = root.FullName,
                ProjectType = "PowerShell",
                RequiredEncoding = FileConsistencyEncoding.UTF8BOM,
                RequiredLineEnding = FileConsistencyLineEnding.CRLF
            });

            Assert.NotNull(result.EncodingConversion);
            Assert.NotNull(result.LineEndingConversion);
            Assert.Equal(1, result.Report.Summary.TotalFiles);

            var bytes = File.ReadAllBytes(filePath);
            Assert.True(bytes.Length >= 3);
            Assert.Equal((byte)0xEF, bytes[0]);
            Assert.Equal((byte)0xBB, bytes[1]);
            Assert.Equal((byte)0xBF, bytes[2]);

            var text = File.ReadAllText(filePath);
            Assert.Contains("\r\n", text, StringComparison.Ordinal);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }

    [Fact]
    public void ParseEncodingOverrides_reads_hashtable_values_case_insensitively()
    {
        var overrides = new Hashtable(StringComparer.OrdinalIgnoreCase)
        {
            ["*.xml"] = "utf8",
            ["*.ps1"] = FileConsistencyEncoding.UTF8BOM
        };

        var parsed = ProjectConsistencyWorkflowService.ParseEncodingOverrides(overrides);

        Assert.NotNull(parsed);
        Assert.Equal(FileConsistencyEncoding.UTF8, parsed!["*.xml"]);
        Assert.Equal(FileConsistencyEncoding.UTF8BOM, parsed["*.ps1"]);
    }
}
