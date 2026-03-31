using System;
using PowerForge.ConsoleShared;
using Xunit;

namespace PowerForge.Tests;

public sealed class SpectrePipelineSummaryWriterTests
{
    [Fact]
    public void NormalizeFailureMessage_PreservesStructuredImportDiagnosticsWithoutTruncation()
    {
        var detail = new string('x', 400);
        var message = string.Join(Environment.NewLine, new[]
        {
            "Import-Module failed during PowerShell/Core validation (exit 1).",
            "Cause: The module could not be loaded.",
            "Detail: " + detail,
            "Loader: Missing.Dependency.dll",
            "PowerShell: 7.5.0 (Core)",
            @"PSModulePath: C:\Users\Test\Documents\PowerShell\Modules | C:\Program Files\PowerShell\Modules",
            @"Manifest: C:\Temp\TestModule\TestModule.psd1",
            "Executable: pwsh.exe",
            @"At C:\Temp\Import-Modules.ps1:1 char:1"
        });

        var normalized = SpectrePipelineSummaryWriter.NormalizeFailureMessage(new InvalidOperationException(message));

        Assert.Contains("Import-Module failed during PowerShell/Core validation (exit 1).", normalized);
        Assert.Contains("Cause: The module could not be loaded.", normalized);
        Assert.Contains("Detail: " + detail, normalized);
        Assert.Contains("Loader: Missing.Dependency.dll", normalized);
        Assert.Contains("PowerShell: 7.5.0 (Core)", normalized);
        Assert.Contains(@"PSModulePath: C:\Users\Test\Documents\PowerShell\Modules | C:\Program Files\PowerShell\Modules", normalized);
        Assert.Contains(@"Manifest: C:\Temp\TestModule\TestModule.psd1", normalized);
        Assert.Contains("Executable: pwsh.exe", normalized);
        Assert.DoesNotContain(@"At C:\Temp\Import-Modules.ps1:1 char:1", normalized);
        Assert.DoesNotContain("…", normalized);
    }

    [Fact]
    public void NormalizeFailureMessage_TruncatesLongUnstructuredMessagesByDefault()
    {
        var message = new string('x', 2105);

        var normalized = SpectrePipelineSummaryWriter.NormalizeFailureMessage(new InvalidOperationException(message));

        Assert.Equal(2000, normalized.Length);
        Assert.EndsWith("…", normalized);
    }
}
