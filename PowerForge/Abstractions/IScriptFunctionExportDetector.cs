using System.Collections.Generic;

namespace PowerForge;

/// <summary>
/// Detects exported script functions from PowerShell script files.
/// </summary>
public interface IScriptFunctionExportDetector
{
    /// <summary>
    /// Detects top-level function names defined in the provided script files.
    /// </summary>
    IReadOnlyList<string> DetectScriptFunctions(IEnumerable<string> scriptFiles);
}
