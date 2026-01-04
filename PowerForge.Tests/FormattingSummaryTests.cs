using Xunit;

namespace PowerForge.Tests;

public sealed class FormattingSummaryTests
{
    #region Message Classification Tests

    [Fact]
    public void IsErrorMessage_StillMatchesNoResultReturnedWithDetailsSuffix()
    {
        Assert.True(FormattingSummary.IsErrorMessage("No result returned; pre=0; pssa=0; norm=0"));
    }

    [Fact]
    public void IsSkippedMessage_StillMatchesSkippedWithDetailsSuffix()
    {
        Assert.True(FormattingSummary.IsSkippedMessage("Skipped: Timeout; pre=0; pssa=0; norm=0"));
        Assert.False(FormattingSummary.IsErrorMessage("Skipped: Timeout; pre=0; pssa=0; norm=0"));
    }

    [Fact]
    public void IsSkippedMessage_DoesNotGetClassifiedAsErrorEvenIfContainsFailedWord()
    {
        var msg = "Skipped: PSSA failed (exit 1); pre=0; pssa=0; norm=0";
        Assert.True(FormattingSummary.IsSkippedMessage(msg));
        Assert.False(FormattingSummary.IsErrorMessage(msg));
    }

    [Fact]
    public void IsErrorMessage_RecognizesErrorPrefix()
    {
        Assert.True(FormattingSummary.IsErrorMessage("Error: Failed to process file"));
        Assert.True(FormattingSummary.IsErrorMessage("error: something went wrong"));
    }

    [Fact]
    public void IsErrorMessage_RecognizesNoResultReturned()
    {
        Assert.True(FormattingSummary.IsErrorMessage("No result returned"));
        Assert.True(FormattingSummary.IsErrorMessage("no result returned"));
    }

    [Fact]
    public void IsErrorMessage_ReturnsFalseForNullOrEmpty()
    {
        Assert.False(FormattingSummary.IsErrorMessage(null!));
        Assert.False(FormattingSummary.IsErrorMessage(""));
        Assert.False(FormattingSummary.IsErrorMessage("   "));
    }

    [Fact]
    public void IsErrorMessage_ReturnsFalseForNormalMessages()
    {
        Assert.False(FormattingSummary.IsErrorMessage("Formatted successfully"));
        Assert.False(FormattingSummary.IsErrorMessage("Unchanged"));
    }

    [Fact]
    public void IsSkippedMessage_RecognizesSkippedPrefix()
    {
        Assert.True(FormattingSummary.IsSkippedMessage("Skipped: Missing tool"));
        Assert.True(FormattingSummary.IsSkippedMessage("skipped: timeout"));
    }

    [Fact]
    public void IsSkippedMessage_ReturnsFalseForNullOrEmpty()
    {
        Assert.False(FormattingSummary.IsSkippedMessage(null!));
        Assert.False(FormattingSummary.IsSkippedMessage(""));
        Assert.False(FormattingSummary.IsSkippedMessage("   "));
    }

    [Fact]
    public void IsSkippedMessage_ReturnsFalseForNormalMessages()
    {
        Assert.False(FormattingSummary.IsSkippedMessage("Formatted successfully"));
        Assert.False(FormattingSummary.IsSkippedMessage("Unchanged"));
    }

    #endregion

    #region FromResults Aggregation Tests

    [Fact]
    public void FromResults_HandlesNullResults()
    {
        var summary = FormattingSummary.FromResults(null);
        
        Assert.Equal(0, summary.Total);
        Assert.Equal(0, summary.Changed);
        Assert.Equal(0, summary.Skipped);
        Assert.Equal(0, summary.Errors);
        Assert.Equal(CheckStatus.Pass, summary.Status);
    }

    [Fact]
    public void FromResults_HandlesEmptyResults()
    {
        var results = new List<FormatterResult>();
        var summary = FormattingSummary.FromResults(results);
        
        Assert.Equal(0, summary.Total);
        Assert.Equal(0, summary.Changed);
        Assert.Equal(0, summary.Skipped);
        Assert.Equal(0, summary.Errors);
        Assert.Equal(CheckStatus.Pass, summary.Status);
    }

    [Fact]
    public void FromResults_SkipsNullItems()
    {
        var results = new List<FormatterResult?>
        {
            new FormatterResult("file1.ps1", true, "Formatted"),
            null,
            new FormatterResult("file2.ps1", false, "Unchanged")
        };
        var summary = FormattingSummary.FromResults(results!);
        
        Assert.Equal(2, summary.Total);
        Assert.Equal(1, summary.Changed);
    }

    [Fact]
    public void FromResults_CountsChangedFiles()
    {
        var results = new List<FormatterResult>
        {
            new FormatterResult("file1.ps1", true, "Formatted"),
            new FormatterResult("file2.ps1", false, "Unchanged"),
            new FormatterResult("file3.ps1", true, "Formatted"),
            new FormatterResult("file4.ps1", false, "Unchanged")
        };
        var summary = FormattingSummary.FromResults(results);
        
        Assert.Equal(4, summary.Total);
        Assert.Equal(2, summary.Changed);
        Assert.Equal(0, summary.Skipped);
        Assert.Equal(0, summary.Errors);
    }

    [Fact]
    public void FromResults_CountsSkippedFiles()
    {
        var results = new List<FormatterResult>
        {
            new FormatterResult("file1.ps1", false, "Skipped: Timeout"),
            new FormatterResult("file2.ps1", false, "Unchanged"),
            new FormatterResult("file3.ps1", false, "Skipped: Missing tool")
        };
        var summary = FormattingSummary.FromResults(results);
        
        Assert.Equal(3, summary.Total);
        Assert.Equal(0, summary.Changed);
        Assert.Equal(2, summary.Skipped);
        Assert.Equal(0, summary.Errors);
    }

    [Fact]
    public void FromResults_CountsErrorFiles()
    {
        var results = new List<FormatterResult>
        {
            new FormatterResult("file1.ps1", false, "Error: Failed to process"),
            new FormatterResult("file2.ps1", false, "Unchanged"),
            new FormatterResult("file3.ps1", false, "No result returned")
        };
        var summary = FormattingSummary.FromResults(results);
        
        Assert.Equal(3, summary.Total);
        Assert.Equal(0, summary.Changed);
        Assert.Equal(0, summary.Skipped);
        Assert.Equal(2, summary.Errors);
    }

    [Fact]
    public void FromResults_HandlesMixedResults()
    {
        var results = new List<FormatterResult>
        {
            new FormatterResult("file1.ps1", true, "Formatted"),
            new FormatterResult("file2.ps1", false, "Unchanged"),
            new FormatterResult("file3.ps1", false, "Skipped: Timeout"),
            new FormatterResult("file4.ps1", false, "Error: Failed"),
            new FormatterResult("file5.ps1", true, "Formatted"),
            new FormatterResult("file6.ps1", false, "No result returned")
        };
        var summary = FormattingSummary.FromResults(results);
        
        Assert.Equal(6, summary.Total);
        Assert.Equal(2, summary.Changed);
        Assert.Equal(1, summary.Skipped);
        Assert.Equal(2, summary.Errors);
    }

    [Fact]
    public void FromResults_HandlesNullMessages()
    {
        var results = new List<FormatterResult>
        {
            new FormatterResult("file1.ps1", true, null!),
            new FormatterResult("file2.ps1", false, null!)
        };
        var summary = FormattingSummary.FromResults(results);
        
        Assert.Equal(2, summary.Total);
        Assert.Equal(1, summary.Changed);
        Assert.Equal(0, summary.Skipped);
        Assert.Equal(0, summary.Errors);
    }

    #endregion

    #region Status Derivation Tests

    [Fact]
    public void FromResults_StatusIsPass_WhenNoErrorsOrSkipped()
    {
        var results = new List<FormatterResult>
        {
            new FormatterResult("file1.ps1", true, "Formatted"),
            new FormatterResult("file2.ps1", false, "Unchanged")
        };
        var summary = FormattingSummary.FromResults(results);
        
        Assert.Equal(CheckStatus.Pass, summary.Status);
    }

    [Fact]
    public void FromResults_StatusIsWarning_WhenSkippedButNoErrors()
    {
        var results = new List<FormatterResult>
        {
            new FormatterResult("file1.ps1", true, "Formatted"),
            new FormatterResult("file2.ps1", false, "Skipped: Timeout")
        };
        var summary = FormattingSummary.FromResults(results);
        
        Assert.Equal(CheckStatus.Warning, summary.Status);
    }

    [Fact]
    public void FromResults_StatusIsFail_WhenErrorsPresent()
    {
        var results = new List<FormatterResult>
        {
            new FormatterResult("file1.ps1", true, "Formatted"),
            new FormatterResult("file2.ps1", false, "Error: Failed")
        };
        var summary = FormattingSummary.FromResults(results);
        
        Assert.Equal(CheckStatus.Fail, summary.Status);
    }

    [Fact]
    public void FromResults_StatusIsFail_WhenBothErrorsAndSkipped()
    {
        var results = new List<FormatterResult>
        {
            new FormatterResult("file1.ps1", false, "Skipped: Timeout"),
            new FormatterResult("file2.ps1", false, "Error: Failed")
        };
        var summary = FormattingSummary.FromResults(results);
        
        Assert.Equal(CheckStatus.Fail, summary.Status);
        Assert.Equal(1, summary.Skipped);
        Assert.Equal(1, summary.Errors);
    }

    #endregion

    #region Worst Method Tests

    [Fact]
    public void Worst_ReturnsFail_WhenEitherIsFail()
    {
        Assert.Equal(CheckStatus.Fail, FormattingSummary.Worst(CheckStatus.Fail, CheckStatus.Pass));
        Assert.Equal(CheckStatus.Fail, FormattingSummary.Worst(CheckStatus.Pass, CheckStatus.Fail));
        Assert.Equal(CheckStatus.Fail, FormattingSummary.Worst(CheckStatus.Fail, CheckStatus.Warning));
        Assert.Equal(CheckStatus.Fail, FormattingSummary.Worst(CheckStatus.Warning, CheckStatus.Fail));
        Assert.Equal(CheckStatus.Fail, FormattingSummary.Worst(CheckStatus.Fail, CheckStatus.Fail));
    }

    [Fact]
    public void Worst_ReturnsWarning_WhenEitherIsWarningAndNoneFail()
    {
        Assert.Equal(CheckStatus.Warning, FormattingSummary.Worst(CheckStatus.Warning, CheckStatus.Pass));
        Assert.Equal(CheckStatus.Warning, FormattingSummary.Worst(CheckStatus.Pass, CheckStatus.Warning));
        Assert.Equal(CheckStatus.Warning, FormattingSummary.Worst(CheckStatus.Warning, CheckStatus.Warning));
    }

    [Fact]
    public void Worst_ReturnsPass_WhenBothPass()
    {
        Assert.Equal(CheckStatus.Pass, FormattingSummary.Worst(CheckStatus.Pass, CheckStatus.Pass));
    }

    #endregion

    #region FormatPartPlain Tests

    [Fact]
    public void FormatPartPlain_HandlesNullSummary()
    {
        var result = FormattingSummary.FormatPartPlain("Test", null!);
        Assert.Equal("Test 0/0", result);
    }

    [Fact]
    public void FormatPartPlain_HandlesNullLabel()
    {
        var summary = FormattingSummary.FromResults(new List<FormatterResult>
        {
            new FormatterResult("file1.ps1", true, "Formatted")
        });
        var result = FormattingSummary.FormatPartPlain(null!, summary);
        Assert.Equal(" 1/1", result);
    }

    [Fact]
    public void FormatPartPlain_FormatsBasicSummary()
    {
        var summary = FormattingSummary.FromResults(new List<FormatterResult>
        {
            new FormatterResult("file1.ps1", true, "Formatted"),
            new FormatterResult("file2.ps1", false, "Unchanged"),
            new FormatterResult("file3.ps1", true, "Formatted")
        });
        var result = FormattingSummary.FormatPartPlain("Scripts", summary);
        Assert.Equal("Scripts 2/3", result);
    }

    [Fact]
    public void FormatPartPlain_IncludesSkippedCount()
    {
        var summary = FormattingSummary.FromResults(new List<FormatterResult>
        {
            new FormatterResult("file1.ps1", true, "Formatted"),
            new FormatterResult("file2.ps1", false, "Skipped: Timeout"),
            new FormatterResult("file3.ps1", false, "Skipped: Missing tool")
        });
        var result = FormattingSummary.FormatPartPlain("Scripts", summary);
        Assert.Equal("Scripts 1/3 (skipped 2)", result);
    }

    [Fact]
    public void FormatPartPlain_IncludesErrorCount()
    {
        var summary = FormattingSummary.FromResults(new List<FormatterResult>
        {
            new FormatterResult("file1.ps1", true, "Formatted"),
            new FormatterResult("file2.ps1", false, "Error: Failed"),
            new FormatterResult("file3.ps1", false, "No result returned")
        });
        var result = FormattingSummary.FormatPartPlain("Scripts", summary);
        Assert.Equal("Scripts 1/3 (errors 2)", result);
    }

    [Fact]
    public void FormatPartPlain_IncludesBothSkippedAndErrors()
    {
        var summary = FormattingSummary.FromResults(new List<FormatterResult>
        {
            new FormatterResult("file1.ps1", true, "Formatted"),
            new FormatterResult("file2.ps1", false, "Skipped: Timeout"),
            new FormatterResult("file3.ps1", false, "Error: Failed")
        });
        var result = FormattingSummary.FormatPartPlain("Scripts", summary);
        Assert.Equal("Scripts 1/3 (skipped 1, errors 1)", result);
    }

    #endregion

    #region FormatPartMarkup Tests

    [Fact]
    public void FormatPartMarkup_HandlesNullSummary()
    {
        var result = FormattingSummary.FormatPartMarkup("Test", null!);
        Assert.Equal("Test [grey]0[/]/0", result);
    }

    [Fact]
    public void FormatPartMarkup_HandlesNullLabel()
    {
        var summary = FormattingSummary.FromResults(new List<FormatterResult>
        {
            new FormatterResult("file1.ps1", true, "Formatted")
        });
        var result = FormattingSummary.FormatPartMarkup(null!, summary);
        Assert.Equal(" [green]1[/][grey]/1[/]", result);
    }

    [Fact]
    public void FormatPartMarkup_FormatsBasicSummaryWithChanges()
    {
        var summary = FormattingSummary.FromResults(new List<FormatterResult>
        {
            new FormatterResult("file1.ps1", true, "Formatted"),
            new FormatterResult("file2.ps1", false, "Unchanged"),
            new FormatterResult("file3.ps1", true, "Formatted")
        });
        var result = FormattingSummary.FormatPartMarkup("Scripts", summary);
        Assert.Equal("Scripts [green]2[/][grey]/3[/]", result);
    }

    [Fact]
    public void FormatPartMarkup_FormatsZeroChangesInGrey()
    {
        var summary = FormattingSummary.FromResults(new List<FormatterResult>
        {
            new FormatterResult("file1.ps1", false, "Unchanged"),
            new FormatterResult("file2.ps1", false, "Unchanged")
        });
        var result = FormattingSummary.FormatPartMarkup("Scripts", summary);
        Assert.Equal("Scripts [grey]0[/][grey]/2[/]", result);
    }

    [Fact]
    public void FormatPartMarkup_IncludesSkippedInYellow()
    {
        var summary = FormattingSummary.FromResults(new List<FormatterResult>
        {
            new FormatterResult("file1.ps1", true, "Formatted"),
            new FormatterResult("file2.ps1", false, "Skipped: Timeout"),
            new FormatterResult("file3.ps1", false, "Skipped: Missing tool")
        });
        var result = FormattingSummary.FormatPartMarkup("Scripts", summary);
        Assert.Equal("Scripts [green]1[/][grey]/3[/] [grey](skipped [yellow]2[/])[/]", result);
    }

    [Fact]
    public void FormatPartMarkup_IncludesErrorsInRed()
    {
        var summary = FormattingSummary.FromResults(new List<FormatterResult>
        {
            new FormatterResult("file1.ps1", true, "Formatted"),
            new FormatterResult("file2.ps1", false, "Error: Failed"),
            new FormatterResult("file3.ps1", false, "No result returned")
        });
        var result = FormattingSummary.FormatPartMarkup("Scripts", summary);
        Assert.Equal("Scripts [green]1[/][grey]/3[/] [grey](errors [red]2[/])[/]", result);
    }

    [Fact]
    public void FormatPartMarkup_IncludesBothSkippedAndErrors()
    {
        var summary = FormattingSummary.FromResults(new List<FormatterResult>
        {
            new FormatterResult("file1.ps1", true, "Formatted"),
            new FormatterResult("file2.ps1", false, "Skipped: Timeout"),
            new FormatterResult("file3.ps1", false, "Error: Failed")
        });
        var result = FormattingSummary.FormatPartMarkup("Scripts", summary);
        Assert.Equal("Scripts [green]1[/][grey]/3[/] [grey](skipped [yellow]1[/], errors [red]1[/])[/]", result);
    }

    #endregion
}

