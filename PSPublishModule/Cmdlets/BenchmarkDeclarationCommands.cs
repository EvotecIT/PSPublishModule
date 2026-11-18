using System;
using System.Linq;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

/// <summary>
/// Validates a benchmark declaration document and, optionally, checks a result against it for drift.
/// </summary>
/// <remarks>
/// <para>
/// The declaration document holds the library policy (id, package, pinned version, licence, restriction class,
/// reason) and the comparisons (area, group, title, subject, libraries, axes, cases, platforms, frameworks).
/// Without a result the command lints the document. With a result it also reports declaration drift: cases,
/// libraries or axis values that ran but are not declared, and declared cases that did not run.
/// </para>
/// <para>
/// The command stops with an error when it finds anything, so a build can run it before measuring.
/// Use <c>-PassThru</c> to receive the findings as objects instead.
/// </para>
/// </remarks>
/// <example>
/// <summary>Lint the declarations in CI</summary>
/// <code>Test-BenchmarkDeclaration -Path .\Benchmarks\benchmarks.json</code>
/// </example>
/// <example>
/// <summary>Check a dry-run result for drift before a long run</summary>
/// <code>Import-BenchmarkResult .\artifacts | Test-BenchmarkDeclaration -Path .\Benchmarks\benchmarks.json -ComparisonId pdf-merge</code>
/// </example>
[Cmdlet(VerbsDiagnostic.Test, "BenchmarkDeclaration")]
[OutputType(typeof(BenchmarkDeclarationIssue))]
public sealed class TestBenchmarkDeclarationCommand : PSCmdlet
{
    /// <summary>Schema 5 declaration document path.</summary>
    [Parameter(Mandatory = true, Position = 0)]
    [ValidateNotNullOrEmpty]
    public string Path { get; set; } = string.Empty;

    /// <summary>Optional normalized result to check for drift.</summary>
    [Parameter(ValueFromPipeline = true)]
    public BenchmarkRunResult? InputObject { get; set; }

    /// <summary>Comparison the result belongs to. Required with a result.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string? ComparisonId { get; set; }

    /// <summary>Return findings as objects instead of stopping with an error.</summary>
    [Parameter]
    public SwitchParameter PassThru { get; set; }

    /// <summary>Validates the declarations.</summary>
    protected override void ProcessRecord()
    {
        var service = new BenchmarkDeclarationService();
        string path = SessionState.Path.GetUnresolvedProviderPathFromPSPath(Path);
        BenchmarkDeclarationDocument document = service.Read(path);
        var issues = service.Validate(document).ToList();
        if (InputObject is not null)
        {
            if (string.IsNullOrWhiteSpace(ComparisonId))
                throw new PSArgumentException("ComparisonId is required when a result is checked for drift.", nameof(ComparisonId));
            BenchmarkComparisonDefinition? comparison = document.Comparisons.FirstOrDefault(value =>
                string.Equals(value.ComparisonId, ComparisonId, StringComparison.OrdinalIgnoreCase));
            if (comparison is null)
            {
                issues.Add(new BenchmarkDeclarationIssue
                {
                    Subject = ComparisonId!,
                    Code = "comparison-not-declared",
                    Message = "The comparison is not declared."
                });
            }
            else
            {
                issues.AddRange(service.CheckDrift(comparison, document.Libraries, InputObject));
            }
        }

        if (PassThru.IsPresent)
        {
            WriteObject(issues, enumerateCollection: true);
            return;
        }
        if (issues.Count > 0)
        {
            ThrowTerminatingError(new ErrorRecord(
                new InvalidOperationException(
                    $"Benchmark declarations in '{path}' have {issues.Count} finding(s): " +
                    string.Join(" ", issues.Select(issue => issue.ToString()))),
                "BenchmarkDeclarationInvalid",
                ErrorCategory.InvalidData,
                path));
        }
    }
}

/// <summary>
/// Selects the libraries a benchmark run may execute from the library policy.
/// </summary>
/// <remarks>
/// Selection is generic: include and exclude lists plus explicit per-library licence acceptance. Excluded
/// libraries never run; acceptance-required libraries run only when you name them in <c>-AcceptLicense</c>.
/// Pass <see cref="BenchmarkLibrarySelection.Accepted"/> to <c>Import-BenchmarkResult -AcceptLicense</c> so the
/// evidence records the acceptance.
/// </remarks>
/// <example>
/// <summary>Select every open library and EPPlus after accepting its licence</summary>
/// <code>$selection = Select-BenchmarkLibrary -Path .\Benchmarks\benchmarks.json -AcceptLicense EPPlus
/// dotnet run -c Release --project .\Benchmarks -- --libraries ($selection.Selected -join ',')</code>
/// </example>
[Cmdlet(VerbsCommon.Select, "BenchmarkLibrary")]
[OutputType(typeof(BenchmarkLibrarySelection))]
public sealed class SelectBenchmarkLibraryCommand : PSCmdlet
{
    /// <summary>Schema 5 declaration document path.</summary>
    [Parameter(Mandatory = true, Position = 0)]
    [ValidateNotNullOrEmpty]
    public string Path { get; set; } = string.Empty;

    /// <summary>Library ids to run; omit to run every selectable library.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string[]? Include { get; set; }

    /// <summary>Library ids not to run.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string[]? Exclude { get; set; }

    /// <summary>Library ids whose licence you accept for this run.</summary>
    [Parameter]
    [ValidateNotNullOrEmpty]
    public string[]? AcceptLicense { get; set; }

    /// <summary>Selects libraries.</summary>
    protected override void ProcessRecord()
    {
        var service = new BenchmarkDeclarationService();
        BenchmarkDeclarationDocument document = service.Read(SessionState.Path.GetUnresolvedProviderPathFromPSPath(Path));
        WriteObject(service.SelectLibraries(document.Libraries, Include, Exclude, AcceptLicense));
    }
}
