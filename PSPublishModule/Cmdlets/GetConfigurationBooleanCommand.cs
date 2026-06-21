using System;
using System.Management.Automation;

namespace PSPublishModule;

/// <summary>
/// Resolves a boolean configuration value from an environment variable with a script-defined default.
/// </summary>
/// <remarks>
/// This helper is intended for build DSL scripts that need environment overrides without repeating
/// <c>[bool]::Parse</c> boilerplate in every repository.
/// </remarks>
/// <example>
/// <summary>Default to manifest-only builds unless RefreshPSD1Only is set in the environment</summary>
/// <code>New-ConfigurationBuild -RefreshPSD1Only:(Get-ConfigurationBoolean RefreshPSD1Only -Default $true)</code>
/// </example>
[Cmdlet(VerbsCommon.Get, "ConfigurationBoolean")]
[OutputType(typeof(bool))]
public sealed class GetConfigurationBooleanCommand : PSCmdlet
{
    /// <summary>Environment variable name to read.</summary>
    [Parameter(Mandatory = true, Position = 0)]
    [Alias("VariableName")]
    [ValidateNotNullOrEmpty]
    public string Name { get; set; } = string.Empty;

    /// <summary>Value returned when the environment variable is missing or blank.</summary>
    [Parameter]
    public bool Default { get; set; }

    /// <summary>Reads and writes the resolved boolean value.</summary>
    protected override void ProcessRecord()
    {
        var value = Environment.GetEnvironmentVariable(Name);
        if (string.IsNullOrWhiteSpace(value))
        {
            WriteObject(Default);
            return;
        }

        if (bool.TryParse(value.Trim(), out var parsed))
        {
            WriteObject(parsed);
            return;
        }

        throw new PSArgumentException($"Environment variable '{Name}' must be a Boolean value, but was '{value}'.");
    }
}
