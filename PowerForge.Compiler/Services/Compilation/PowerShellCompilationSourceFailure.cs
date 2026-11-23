using System.Management.Automation.Language;

namespace PowerForge;

internal static class PowerShellCompilationSourceFailure
{
    private const string SourcePathKey = "PowerForge.Compilation.SourcePath";
    private const string LineKey = "PowerForge.Compilation.SourceLine";
    private const string ColumnKey = "PowerForge.Compilation.SourceColumn";

    internal static InvalidOperationException Create(string message, IScriptExtent extent)
        => string.IsNullOrWhiteSpace(extent.File)
            ? new InvalidOperationException(message)
            : Create(message, extent.File, extent);

    /// <summary>Preserves the authored location of a rejection raised before a plan can report it.</summary>
    internal static InvalidOperationException Create(string message, string sourcePath, IScriptExtent extent)
    {
        var failure = new InvalidOperationException(message);
        failure.Data[SourcePathKey] = sourcePath;
        failure.Data[LineKey] = extent.StartLineNumber;
        failure.Data[ColumnKey] = extent.StartColumnNumber;
        return failure;
    }

    internal static PowerShellCompilationDiagnostic? GetDiagnostic(Exception exception)
        => exception.Data[SourcePathKey] is string path &&
           exception.Data[LineKey] is int line &&
           exception.Data[ColumnKey] is int column
            ? new PowerShellCompilationDiagnostic(PowerShellCompilationDiagnosticCode.InputError,
                exception.Message, path, line, column)
            : null;
}
