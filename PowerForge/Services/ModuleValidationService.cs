using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>
/// Executes module validation checks (structure, documentation, tests, binaries, csproj).
/// </summary>
public sealed partial class ModuleValidationService
{
    private readonly ILogger _logger;
    private readonly IPowerShellRunner _runner;

    /// <summary>Creates a module validation service.</summary>
    /// <param name="logger">Logger for status output.</param>
    /// <param name="runner">Optional PowerShell runner override.</param>
    public ModuleValidationService(ILogger logger, IPowerShellRunner? runner = null)
    {
        _logger = logger ?? new NullLogger();
        _runner = runner ?? new PowerShellRunner();
    }

    /// <summary>Runs configured validation checks for the module.</summary>
    /// <param name="spec">Module validation inputs and settings.</param>
    /// <returns>Validation report with per-check results.</returns>
    public ModuleValidationReport Run(ModuleValidationSpec spec)
    {
        if (spec is null) throw new ArgumentNullException(nameof(spec));
        var settings = spec.Settings ?? new ModuleValidationSettings();

        if (!settings.Enable)
            return new ModuleValidationReport(Array.Empty<ModuleValidationCheckResult>());

        var checks = new List<ModuleValidationCheckResult>(5);

        AddCheck(checks, ValidateStructure(spec, settings.Structure));
        AddCheck(checks, ValidateDocumentation(spec, settings.Documentation));
        AddCheck(checks, ValidateScriptAnalyzer(spec, settings.ScriptAnalyzer));
        AddCheck(checks, ValidateFileIntegrity(spec, settings.FileIntegrity));
        AddCheck(checks, ValidateTests(spec, settings.Tests));
        AddCheck(checks, ValidateBinary(spec, settings.Binary));
        AddCheck(checks, ValidateCsproj(spec, settings.Csproj));

        return new ModuleValidationReport(checks.ToArray());
    }

    private void AddCheck(List<ModuleValidationCheckResult> checks, ModuleValidationCheckResult? result)
    {
        if (result is null) return;
        checks.Add(result);
        WriteCheckSummary(result);
    }

    private void WriteCheckSummary(ModuleValidationCheckResult result)
    {
        var summary = string.IsNullOrWhiteSpace(result.Summary) ? string.Empty : $" ({result.Summary})";
        var message = $"Validation: {result.Name} => {result.Status}{summary}";
        switch (result.Status)
        {
            case CheckStatus.Fail:
                _logger.Error(message);
                break;
            case CheckStatus.Warning:
                _logger.Warn(message);
                break;
            default:
                _logger.Info(message);
                break;
        }

        if (_logger.IsVerbose && result.Issues is { Length: > 0 })
        {
            foreach (var issue in result.Issues)
                _logger.Verbose($"  - {issue}");
        }
    }

}
