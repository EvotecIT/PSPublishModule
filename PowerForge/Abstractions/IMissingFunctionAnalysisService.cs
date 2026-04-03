namespace PowerForge;

/// <summary>
/// Analyzes PowerShell content for missing command references and inlineable helper functions.
/// </summary>
public interface IMissingFunctionAnalysisService
{
    /// <summary>
    /// Analyzes the provided file or code and returns a host-neutral result.
    /// </summary>
    MissingFunctionAnalysisResult Analyze(string? filePath, string? code, MissingFunctionsOptions? options = null);
}
