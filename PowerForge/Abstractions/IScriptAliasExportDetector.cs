using System.Collections.Generic;

namespace PowerForge;

/// <summary>
/// Optionally detects aliases declared by PowerShell script files.
/// </summary>
/// <remarks>
/// This capability is separate from <see cref="IScriptFunctionExportDetector"/> so existing
/// custom function detectors remain source- and binary-compatible.
/// </remarks>
public interface IScriptAliasExportDetector
{
    /// <summary>
    /// Detects aliases declared at module scope by the provided script files.
    /// </summary>
    IReadOnlyList<string> DetectScriptAliases(IEnumerable<string> scriptFiles);
}
