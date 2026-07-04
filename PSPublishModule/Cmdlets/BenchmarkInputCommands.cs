using System;
using System.Collections;
using System.Globalization;
using System.Linq;
using System.Management.Automation;

namespace PSPublishModule;

/// <summary>
/// Gets a caller-supplied benchmark input variable.
/// </summary>
[Cmdlet(VerbsCommon.Get, "BenchmarkInput", DefaultParameterSetName = TextParameterSet)]
[Alias("input", "inputInt", "inputBool")]
[OutputType(typeof(string), typeof(int), typeof(bool))]
public sealed class GetBenchmarkInputCommand : PSCmdlet
{
    private const string TextParameterSet = "Text";
    private const string IntParameterSet = "Int";
    private const string BoolParameterSet = "Bool";

    /// <summary>Benchmark variable name.</summary>
    [Parameter(Mandatory = true, Position = 0, ParameterSetName = TextParameterSet)]
    [Parameter(Mandatory = true, Position = 0, ParameterSetName = IntParameterSet)]
    [Parameter(Mandatory = true, Position = 0, ParameterSetName = BoolParameterSet)]
    [ValidateNotNullOrEmpty]
    public string Name { get; set; } = string.Empty;

    /// <summary>Default value used when the variable was not supplied.</summary>
    [Parameter(Position = 1, ParameterSetName = TextParameterSet)]
    [Parameter(Position = 1, ParameterSetName = IntParameterSet)]
    [Parameter(Position = 1, ParameterSetName = BoolParameterSet)]
    public object? Default { get; set; }

    /// <summary>Fail when the variable was not supplied or is empty.</summary>
    [Parameter(ParameterSetName = TextParameterSet)]
    [Parameter(ParameterSetName = IntParameterSet)]
    [Parameter(ParameterSetName = BoolParameterSet)]
    public SwitchParameter Required { get; set; }

    /// <summary>Return the benchmark variable as one or more integers.</summary>
    [Parameter(Mandatory = true, ParameterSetName = IntParameterSet)]
    public SwitchParameter Int { get; set; }

    /// <summary>Return the benchmark variable as a boolean.</summary>
    [Parameter(Mandatory = true, ParameterSetName = BoolParameterSet)]
    public SwitchParameter Bool { get; set; }

    /// <inheritdoc />
    protected override void ProcessRecord()
    {
        var kind = GetInputKind();
        switch (kind)
        {
            case BenchmarkInputKind.Int:
                WriteObject(BenchmarkInputReader.GetIntValues(SessionState, Name, ConvertDefaultToIntValues(Default), Required), enumerateCollection: true);
                return;
            case BenchmarkInputKind.Bool:
                WriteObject(BenchmarkInputReader.GetBool(SessionState, Name, ConvertDefaultToBool(Default), Required));
                return;
            default:
                var value = BenchmarkInputReader.GetText(SessionState, Name, Convert.ToString(Default, CultureInfo.InvariantCulture), Required);
                WriteObject(value);
                return;
        }
    }

    private BenchmarkInputKind GetInputKind()
    {
        var invokedAs = MyInvocation.InvocationName;
        if (string.Equals(invokedAs, "inputInt", StringComparison.OrdinalIgnoreCase)) return BenchmarkInputKind.Int;
        if (string.Equals(invokedAs, "inputBool", StringComparison.OrdinalIgnoreCase)) return BenchmarkInputKind.Bool;
        if (Int.IsPresent) return BenchmarkInputKind.Int;
        if (Bool.IsPresent) return BenchmarkInputKind.Bool;
        return BenchmarkInputKind.Text;
    }

    private static int[] ConvertDefaultToIntValues(object? value)
    {
        if (value is null) return Array.Empty<int>();
        if (value is int[] integers) return integers;
        if (value is IEnumerable values and not string)
        {
            return values
                .Cast<object?>()
                .Where(item => item is not null)
                .Select(item => Convert.ToInt32(item, CultureInfo.InvariantCulture))
                .ToArray();
        }

        var text = Convert.ToString(value, CultureInfo.InvariantCulture);
        if (string.IsNullOrWhiteSpace(text)) return Array.Empty<int>();
        return text.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item.Trim())
            .Where(item => item.Length > 0)
            .Select(item => int.Parse(item, CultureInfo.InvariantCulture))
            .ToArray();
    }

    private static bool ConvertDefaultToBool(object? value)
    {
        if (value is null) return false;
        if (value is bool boolean) return boolean;
        return BenchmarkInputReader.ParseBool(Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty, "Default");
    }
}

internal enum BenchmarkInputKind
{
    Text,
    Int,
    Bool
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

        var values = value!.Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(item => item.Trim())
            .Where(item => item.Length > 0)
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

        return ParseBool(value!, name);
    }

    internal static bool ParseBool(string value, string name)
    {
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
