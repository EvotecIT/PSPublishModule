using System;
using System.IO;
using Xunit;

namespace PowerForge.Tests;

public sealed class PowerShellCompatibilityWorkflowServiceTests
{
    [Fact]
    public void Execute_ReturnsReportAndEmitsProgress()
    {
        var root = Directory.CreateDirectory(Path.Combine(Path.GetTempPath(), "PowerForge.Tests", Guid.NewGuid().ToString("N")));
        try
        {
            File.WriteAllText(Path.Combine(root.FullName, "a.ps1"), "using namespace System.Text\n");
            PowerShellCompatibilityProgress? captured = null;
            var service = new PowerShellCompatibilityWorkflowService(new PowerShellCompatibilityAnalyzer(new NullLogger()));

            var result = service.Execute(
                new PowerShellCompatibilityWorkflowRequest
                {
                    InputPath = root.FullName,
                    Recurse = false,
                    ExcludeDirectories = Array.Empty<string>()
                },
                progress => captured = progress);

            Assert.NotNull(result.Report);
            Assert.Single(result.Report.Files);
            Assert.NotNull(captured);
            Assert.Equal(1, captured!.Current);
            Assert.Equal(1, captured.Total);
        }
        finally
        {
            try { root.Delete(recursive: true); } catch { }
        }
    }
}

