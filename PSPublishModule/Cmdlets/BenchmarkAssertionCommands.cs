using System;
using System.Management.Automation;

namespace PSPublishModule;

/// <summary>
/// Asserts that a benchmark path exists or does not exist.
/// </summary>
[Cmdlet("Assert", "BenchmarkPath")]
[Alias("assertPath")]
[OutputType(typeof(string))]
public sealed class AssertBenchmarkPathCommand : PSCmdlet
{
    /// <summary>Path to validate.</summary>
    [Parameter(Mandatory = true, Position = 0)]
    [ValidateNotNullOrEmpty]
    public string Path { get; set; } = string.Empty;

    /// <summary>Assert that the path does not exist.</summary>
    [Parameter]
    public SwitchParameter Not { get; set; }

    /// <summary>Optional assertion message.</summary>
    [Parameter]
    public string? Message { get; set; }

    /// <summary>Emit the resolved path when the assertion passes.</summary>
    [Parameter]
    public SwitchParameter PassThru { get; set; }

    /// <inheritdoc />
    protected override void ProcessRecord()
    {
        var resolved = SessionState.Path.GetUnresolvedProviderPathFromPSPath(Path);
        var exists = System.IO.File.Exists(resolved) || System.IO.Directory.Exists(resolved);
        if (exists == !Not.IsPresent)
        {
            if (PassThru)
                WriteObject(resolved);
            return;
        }

        var defaultMessage = Not.IsPresent
            ? $"Expected path '{resolved}' not to exist."
            : $"Expected path '{resolved}' to exist.";
        ThrowTerminatingError(new ErrorRecord(
            new InvalidOperationException(string.IsNullOrWhiteSpace(Message) ? defaultMessage : Message),
            "BenchmarkPathAssertionFailed",
            ErrorCategory.InvalidResult,
            resolved));
    }
}

/// <summary>
/// Asserts a benchmark value condition.
/// </summary>
[Cmdlet("Assert", "BenchmarkValue", DefaultParameterSetName = ParameterSetEquals)]
[Alias("assertValue")]
[OutputType(typeof(object))]
public sealed class AssertBenchmarkValueCommand : PSCmdlet
{
    private const string ParameterSetEquals = "Equals";
    private const string ParameterSetNotNull = "NotNull";

    /// <summary>Actual value.</summary>
    [Parameter(Mandatory = true, Position = 0, ParameterSetName = ParameterSetEquals)]
    [Parameter(Mandatory = true, Position = 0, ParameterSetName = ParameterSetNotNull)]
    public object? Actual { get; set; }

    /// <summary>Expected value.</summary>
    [Parameter(Mandatory = true, Position = 1, ParameterSetName = ParameterSetEquals)]
    public object? Expected { get; set; }

    /// <summary>Assert that the value is not null.</summary>
    [Parameter(Mandatory = true, ParameterSetName = ParameterSetNotNull)]
    public SwitchParameter NotNull { get; set; }

    /// <summary>Optional assertion message.</summary>
    [Parameter]
    public string? Message { get; set; }

    /// <summary>Emit the actual value when the assertion passes.</summary>
    [Parameter]
    public SwitchParameter PassThru { get; set; }

    /// <inheritdoc />
    protected override void ProcessRecord()
    {
        var passed = ParameterSetName == ParameterSetNotNull
            ? Actual is not null
            : object.Equals(Actual, Expected);
        if (passed)
        {
            if (PassThru)
                WriteObject(Actual);
            return;
        }

        var defaultMessage = ParameterSetName == ParameterSetNotNull
            ? "Expected benchmark value not to be null."
            : $"Expected benchmark value '{Actual}' to equal '{Expected}'.";
        ThrowTerminatingError(new ErrorRecord(
            new InvalidOperationException(string.IsNullOrWhiteSpace(Message) ? defaultMessage : Message),
            "BenchmarkValueAssertionFailed",
            ErrorCategory.InvalidResult,
            Actual));
    }
}
