using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>
/// Describes the automatic PowerShell parameters that participate in advanced-command binding.
/// </summary>
internal static class PowerShellCommonParameterPolicy
{
    private static readonly PowerShellCommonParameter[] StandardParameters =
    {
        new("Verbose", "vb", isSwitch: true),
        new("Debug", "db", isSwitch: true),
        new("ErrorAction", "ea"),
        new("WarningAction", "wa"),
        new("InformationAction", "infa"),
        new("ProgressAction", "proga", minimumTargetFramework: "net8.0"),
        new("ErrorVariable", "ev"),
        new("WarningVariable", "wv"),
        new("InformationVariable", "iv"),
        new("OutVariable", "ov"),
        new("OutBuffer", "ob"),
        new("PipelineVariable", "pv")
    };

    private static readonly PowerShellCommonParameter[] ShouldProcessParameters =
    {
        new("WhatIf", "wi", isSwitch: true),
        new("Confirm", "cf", isSwitch: true)
    };

    internal static PowerShellCommonParameter[] GetAvailable(ParamBlockAst? parameterBlock, string? targetFramework)
        => GetAvailable(PowerShellAdvancedFunctionPolicy.GetBinding(parameterBlock), targetFramework);

    internal static PowerShellCommonParameter[] GetAvailable(
        PowerShellCompilationCommandBinding commandBinding,
        string? targetFramework)
    {
        if (!commandBinding.IsAdvancedFunction)
            return Array.Empty<PowerShellCommonParameter>();

        var parameters = StandardParameters
            .Where(parameter => parameter.IsAvailableFor(targetFramework))
            .ToList();
        if (commandBinding.SupportsShouldProcess)
            parameters.AddRange(ShouldProcessParameters);
        return parameters.ToArray();
    }

    internal static PowerShellCommonParameter[] GetStandard(bool isAdvanced, string? targetFramework)
        => isAdvanced
            ? StandardParameters.Where(parameter => parameter.IsAvailableFor(targetFramework)).ToArray()
            : Array.Empty<PowerShellCommonParameter>();
}

internal sealed class PowerShellCommonParameter
{
    internal PowerShellCommonParameter(
        string name,
        string alias,
        bool isSwitch = false,
        string? minimumTargetFramework = null)
    {
        Name = name;
        Alias = alias;
        IsSwitch = isSwitch;
        MinimumTargetFramework = minimumTargetFramework;
    }

    internal string Name { get; }

    internal string Alias { get; }

    internal bool IsSwitch { get; }

    internal string? MinimumTargetFramework { get; }

    internal bool IsAvailableFor(string? targetFramework)
        => MinimumTargetFramework is null ||
           !string.Equals(targetFramework, "net472", StringComparison.OrdinalIgnoreCase);
}
