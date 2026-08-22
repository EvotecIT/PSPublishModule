using System.Collections.Generic;

namespace PowerForge;

/// <summary>
/// Optionally analyzes script alias declarations and reports whether every module-scope alias name was resolved.
/// </summary>
/// <remarks>
/// Builders use this richer capability to remove stale binary aliases only when the script alias set is complete.
/// Implementations that expose only <see cref="IScriptAliasExportDetector"/> remain supported and are treated conservatively.
/// </remarks>
public interface IScriptAliasExportAnalysisDetector
{
    /// <summary>
    /// Analyzes aliases declared at module scope by the provided script files.
    /// </summary>
    ScriptAliasExportAnalysis AnalyzeScriptAliases(IEnumerable<string> scriptFiles);
}
