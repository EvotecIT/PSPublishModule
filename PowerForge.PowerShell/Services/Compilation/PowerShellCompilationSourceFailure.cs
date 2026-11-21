using System.Management.Automation.Language;

namespace PowerForge;

internal static class PowerShellCompilationSourceFailure
{
    /// <summary>Preserves the authored location of a rejection raised before a plan can report it.</summary>
    internal static InvalidOperationException Create(string message, string sourcePath, IScriptExtent extent)
    {
        var failure = new InvalidOperationException(message);
        failure.Data[typeof(PowerShellCompilationDiagnostic)] = new PowerShellCompilationDiagnostic(
            PowerShellCompilationDiagnosticCode.InputError,
            message,
            sourcePath,
            extent.StartLineNumber,
            extent.StartColumnNumber);
        return failure;
    }

    internal static PowerShellCompilationDiagnostic? GetDiagnostic(Exception exception)
        => exception.Data[typeof(PowerShellCompilationDiagnostic)] as PowerShellCompilationDiagnostic;
}
