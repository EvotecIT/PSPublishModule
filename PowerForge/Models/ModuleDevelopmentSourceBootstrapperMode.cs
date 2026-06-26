namespace PowerForge;

/// <summary>
/// Controls how PowerForge maintains the checked-in source module bootstrapper for development binary loading.
/// </summary>
public enum ModuleDevelopmentSourceBootstrapperMode
{
    /// <summary>
    /// Preserve hand-authored single-file source PSM1 files and only maintain generated/source-folder bootstrappers.
    /// </summary>
    PreserveSingleFile = 0,

    /// <summary>
    /// Allow generated development-bootstrapper code to replace a single-file source PSM1.
    /// </summary>
    ReplaceSingleFile = 1
}
