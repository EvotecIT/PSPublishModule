using System;

namespace PowerForge;

internal sealed class PowerShellCompatibilityWorkflowService
{
    private readonly PowerShellCompatibilityAnalyzer _analyzer;

    public PowerShellCompatibilityWorkflowService(PowerShellCompatibilityAnalyzer analyzer)
    {
        _analyzer = analyzer ?? throw new ArgumentNullException(nameof(analyzer));
    }

    public PowerShellCompatibilityWorkflowResult Execute(
        PowerShellCompatibilityWorkflowRequest request,
        Action<PowerShellCompatibilityProgress>? progress = null)
    {
        if (request is null)
            throw new ArgumentNullException(nameof(request));

        var report = _analyzer.Analyze(
            new PowerShellCompatibilitySpec(request.InputPath, request.Recurse, request.ExcludeDirectories),
            progress,
            request.ExportPath);

        return new PowerShellCompatibilityWorkflowResult
        {
            Report = report
        };
    }
}
