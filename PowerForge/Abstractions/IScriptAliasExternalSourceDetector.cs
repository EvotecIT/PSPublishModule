namespace PowerForge;

/// <summary>
/// Optionally reports script imports that can contribute aliases outside the analyzed file set.
/// </summary>
public interface IScriptAliasExternalSourceDetector
{
    /// <summary>
    /// Determines whether any supplied script dot-sources another script at module scope.
    /// </summary>
    /// <param name="scriptFiles">Script files executed during module import.</param>
    /// <returns><see langword="true"/> when a module-scope dot-source can contribute additional aliases.</returns>
    bool HasModuleScopeDotSources(IEnumerable<string> scriptFiles);
}
