namespace PowerForge;

/// <summary>
/// Analyzes PowerShell code for unresolved command usage and inlineable helper functions without exposing the
/// underlying host-specific analysis mechanism.
/// </summary>
public interface IMissingFunctionAnalysisService
{
    /// <summary>
    /// Analyzes a script file or in-memory code block.
    /// </summary>
    MissingFunctionAnalysisResult Analyze(string? filePath, string? code, MissingFunctionsOptions options);
}
