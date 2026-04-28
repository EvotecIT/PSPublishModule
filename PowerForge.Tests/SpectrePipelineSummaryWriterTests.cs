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
    public void NormalizeFailureMessage_PreservesStructuredContinuationLines()
    {
        var message = string.Join(Environment.NewLine, new[]
        {
            "Import-Module failed during PowerShell/Core validation (exit 1).",
            "Cause: Could not load file or assembly 'Microsoft.Win32.SystemEvents.dll'.",
            "Detail: Import-Module: C:\\Temp\\modulepipeline.ps1:128:7",
            "Line |",
            " 128 | Import-Module -Name $ModulePath -Force -ErrorAction Stop",
            "     | Could not load file or assembly 'Microsoft.Win32.SystemEvents.dll'.",
            "PowerShell: 7.5.5 (Core)",
            "Manifest: C:\\Temp\\ADPlayground\\ADPlayground.psd1"
        });

        var normalized = SpectrePipelineSummaryWriter.NormalizeFailureMessage(new InvalidOperationException(message));

        Assert.Contains("Detail: Import-Module: C:\\Temp\\modulepipeline.ps1:128:7", normalized);
        Assert.Contains("Line |", normalized);
        Assert.Contains("128 | Import-Module -Name $ModulePath -Force -ErrorAction Stop", normalized);
        Assert.Contains("PowerShell: 7.5.5 (Core)", normalized);
    }

    [Fact]
    public void BuildFailureDetailRows_SplitsStructuredFieldsForReadableSummary()
    {
        var message = string.Join(Environment.NewLine, new[]
        {
            "Import-Module failed during PowerShell/Core validation (exit 1).",
            "Cause: Could not load file or assembly 'Microsoft.Win32.SystemEvents.dll'.",
            "PowerShell: 7.5.5 (Core)",
            "Manifest: C:\\Temp\\ADPlayground\\ADPlayground.psd1"
        });

        var rows = SpectrePipelineSummaryWriter.BuildFailureDetailRows(message);

        Assert.Collection(
            rows,
            row =>
            {
                Assert.Equal("Failure", row.Field);
                Assert.Contains("PowerShell/Core", row.Detail);
            },
            row =>
            {
                Assert.Equal("Cause", row.Field);
                Assert.Contains("Microsoft.Win32.SystemEvents.dll", row.Detail);
            },
            row => Assert.Equal(("PowerShell", "7.5.5 (Core)"), row),
            row => Assert.Equal(("Manifest", "C:\\Temp\\ADPlayground\\ADPlayground.psd1"), row));
    }

    [Fact]
    public void BuildFailureDetailRows_AppendsContinuationLinesToStructuredDetail()
    {
        var message = string.Join(Environment.NewLine, new[]
        {
            "Import-Module failed during PowerShell/Core validation (exit 1).",
            "Detail: Import-Module: C:\\Temp\\modulepipeline.ps1:128:7",
            "Line |",
            " 128 | Import-Module -Name $ModulePath -Force -ErrorAction Stop"
        });

        var rows = SpectrePipelineSummaryWriter.BuildFailureDetailRows(message);

        Assert.Collection(
            rows,
            row => Assert.Equal("Failure", row.Field),
            row =>
            {
                Assert.Equal("Detail", row.Field);
                Assert.Contains("Import-Module: C:\\Temp\\modulepipeline.ps1:128:7", row.Detail);
                Assert.Contains("Line |", row.Detail);
                Assert.Contains("128 | Import-Module -Name $ModulePath -Force -ErrorAction Stop", row.Detail);
            });
    }

    [Fact]
    public void BuildFailureDetailRows_LabelsUnstructuredFirstLineAsMessage()
    {
        var rows = SpectrePipelineSummaryWriter.BuildFailureDetailRows("plain failure");

        Assert.Collection(
            rows,
            row => Assert.Equal(("Message", "plain failure"), row));
    }

    [Fact]
    public void NormalizeFailureMessage_DeduplicatesStructuredLinesAfterContinuation()
    {
        var message = string.Join(Environment.NewLine, new[]
        {
            "Import-Module failed during PowerShell/Core validation (exit 1).",
            "Detail: Import-Module: C:\\Temp\\modulepipeline.ps1:128:7",
            "Line |",
            "Detail: Import-Module: C:\\Temp\\modulepipeline.ps1:128:7"
        });

        var normalized = SpectrePipelineSummaryWriter.NormalizeFailureMessage(new InvalidOperationException(message));

        Assert.Equal(1, normalized.Split("Detail: Import-Module: C:\\Temp\\modulepipeline.ps1:128:7").Length - 1);
        Assert.Contains("Line |", normalized);
    }

    [Fact]
    public void NormalizeFailureMessage_SkipsContinuationLinesAfterDuplicateStructuredHeader()
    {
        var message = string.Join(Environment.NewLine, new[]
        {
            "Import-Module failed during PowerShell/Core validation (exit 1).",
            "Detail: Import-Module: C:\\Temp\\modulepipeline.ps1:128:7",
            "Line |",
            "Detail: Import-Module: C:\\Temp\\modulepipeline.ps1:128:7",
            " 128 | duplicate code"
        });

        var normalized = SpectrePipelineSummaryWriter.NormalizeFailureMessage(new InvalidOperationException(message));

        Assert.Equal(1, normalized.Split("Detail: Import-Module: C:\\Temp\\modulepipeline.ps1:128:7").Length - 1);
        Assert.Contains("Line |", normalized);
        Assert.DoesNotContain("duplicate code", normalized);
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
