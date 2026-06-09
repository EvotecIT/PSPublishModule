using System.Collections.Generic;
using System.Globalization;

namespace PowerForge;

internal sealed class ModuleTestSuiteCiOutputService
{
    private readonly System.Func<string, string?> _getEnvironmentVariable;

    public ModuleTestSuiteCiOutputService(System.Func<string, string?>? getEnvironmentVariable = null)
    {
        _getEnvironmentVariable = getEnvironmentVariable ?? Environment.GetEnvironmentVariable;
    }

    public string[] BuildOutputs(ModuleTestSuiteResult result, bool success, string? errorMessage)
    {
        if (result is null)
            throw new ArgumentNullException(nameof(result));

        var lines = new List<string>();

        if (!string.IsNullOrWhiteSpace(_getEnvironmentVariable("GITHUB_ACTIONS")))
        {
            lines.Add($"::set-output name=test-result::{(success ? "true" : "false")}");
            lines.Add($"::set-output name=total-tests::{result.TotalCount}");
            lines.Add($"::set-output name=failed-tests::{result.FailedCount}");
            if (!success && !string.IsNullOrWhiteSpace(errorMessage))
                lines.Add($"::set-output name=error-message::{errorMessage}");
            if (result.CoveragePercent.HasValue)
                lines.Add($"::set-output name=code-coverage::{result.CoveragePercent.Value.ToString("0.00", CultureInfo.InvariantCulture)}");
        }

        if (!string.IsNullOrWhiteSpace(_getEnvironmentVariable("TF_BUILD")))
        {
            lines.Add($"##vso[task.setvariable variable=TestResult;isOutput=true]{(success ? "true" : "false")}");
            lines.Add($"##vso[task.setvariable variable=TotalTests;isOutput=true]{result.TotalCount}");
            lines.Add($"##vso[task.setvariable variable=FailedTests;isOutput=true]{result.FailedCount}");
            if (!success && !string.IsNullOrWhiteSpace(errorMessage))
                lines.Add($"##vso[task.setvariable variable=ErrorMessage;isOutput=true]{errorMessage}");
        }

        return lines.ToArray();
    }
}
