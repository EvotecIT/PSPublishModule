using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using System.Management.Automation;

namespace PSPublishModule;

/// <summary>
/// Gets a caller-supplied benchmark input variable as text.
/// </summary>
[Cmdlet(VerbsCommon.Get, "BenchmarkInput")]
[Alias("input")]
[OutputType(typeof(string))]
public sealed class GetBenchmarkInputCommand : PSCmdlet
{
    /// <summary>Benchmark variable name.</summary>
    [Parameter(Mandatory = true, Position = 0)]
    [ValidateNotNullOrEmpty]
    public string Name { get; set; } = string.Empty;

    /// <summary>Default value used when the variable was not supplied.</summary>
    [Parameter(Position = 1)]
    public string? Default { get; set; }

    /// <summary>Fail when the variable was not supplied or is empty.</summary>
    [Parameter]
    public SwitchParameter Required { get; set; }

    /// <inheritdoc />
    protected override void ProcessRecord()
    {
        var value = BenchmarkInputReader.GetText(SessionState, Name, Default, Required);
        WriteObject(value);
    }
}

/// <summary>
/// Gets a caller-supplied benchmark input variable as one or more integers.
/// </summary>
[Cmdlet(VerbsCommon.Get, "BenchmarkIntInput")]
[Alias("inputInt")]
[OutputType(typeof(int))]
public sealed class GetBenchmarkIntInputCommand : PSCmdlet
{
    /// <summary>Benchmark variable name.</summary>
    [Parameter(Mandatory = true, Position = 0)]
    [ValidateNotNullOrEmpty]
    public string Name { get; set; } = string.Empty;

    /// <summary>Default values used when the variable was not supplied.</summary>
    [Parameter(Position = 1)]
    public int[]? Default { get; set; }

    /// <summary>Fail when the variable was not supplied or is empty.</summary>
    [Parameter]
    public SwitchParameter Required { get; set; }

    /// <inheritdoc />
    protected override void ProcessRecord()
    {
        WriteObject(BenchmarkInputReader.GetIntValues(SessionState, Name, Default ?? Array.Empty<int>(), Required), enumerateCollection: true);
    }
}

/// <summary>
/// Gets a caller-supplied benchmark input variable as a boolean.
/// </summary>
[Cmdlet(VerbsCommon.Get, "BenchmarkBoolInput")]
[Alias("inputBool")]
[OutputType(typeof(bool))]
public sealed class GetBenchmarkBoolInputCommand : PSCmdlet
{
    /// <summary>Benchmark variable name.</summary>
    [Parameter(Mandatory = true, Position = 0)]
    [ValidateNotNullOrEmpty]
    public string Name { get; set; } = string.Empty;

    /// <summary>Default value used when the variable was not supplied.</summary>
    [Parameter(Position = 1)]
    public bool Default { get; set; }

    /// <summary>Fail when the variable was not supplied or is empty.</summary>
    [Parameter]
    public SwitchParameter Required { get; set; }

    /// <inheritdoc />
    protected override void ProcessRecord()
    {
        WriteObject(BenchmarkInputReader.GetBool(SessionState, Name, Default, Required));
    }
}

internal static class BenchmarkInputReader
{
    internal static string? GetText(SessionState sessionState, string name, string? defaultValue, bool required)
    {
        var value = GetRawValue(sessionState, name);
        if (!string.IsNullOrWhiteSpace(value)) return value;
        if (required) throw new InvalidOperationException($"Benchmark variable '{name}' is required.");
        return defaultValue;
    }

    internal static int[] GetIntValues(SessionState sessionState, string name, int[] defaultValue, bool required)
    {
        var value = GetRawValue(sessionState, name);
        if (string.IsNullOrWhiteSpace(value))
        {
            if (required) throw new InvalidOperationException($"Benchmark variable '{name}' is required.");
            return defaultValue;
        }

        var values = value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(item => int.Parse(item, CultureInfo.InvariantCulture))
            .ToArray();
        if (values.Length > 0) return values;
        if (required) throw new InvalidOperationException($"Benchmark variable '{name}' did not contain any integer values.");
        return defaultValue;
    }

    internal static bool GetBool(SessionState sessionState, string name, bool defaultValue, bool required)
    {
        var value = GetRawValue(sessionState, name);
        if (string.IsNullOrWhiteSpace(value))
        {
            if (required) throw new InvalidOperationException($"Benchmark variable '{name}' is required.");
            return defaultValue;
        }

        return value.Trim().ToLowerInvariant() switch
        {
            "true" or "1" or "yes" or "on" => true,
            "false" or "0" or "no" or "off" => false,
            _ => throw new InvalidOperationException($"Benchmark variable '{name}' value '{value}' is not a boolean value.")
        };
    }

    private static string? GetRawValue(SessionState sessionState, string name)
    {
        var variable = sessionState.PSVariable.Get("BenchmarkVariables")?.Value
                       ?? sessionState.PSVariable.Get("BenchmarkVariable")?.Value;
        if (variable is IDictionary dictionary)
            return Convert.ToString(dictionary[name], CultureInfo.InvariantCulture);
        return null;
    }
}
