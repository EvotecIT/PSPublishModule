namespace PowerForge;

/// <summary>
/// Controls how a generated source bootstrapper discovers and loads local development binaries.
/// </summary>
public enum ModuleDevelopmentBinaryMode
{
    /// <summary>Do not emit development-binary loading logic.</summary>
    Off = 0,

    /// <summary>Load development binaries only when the configured environment variable is set to true.</summary>
    Environment = 1,

    /// <summary>Load development binaries automatically when a matching local build output exists.</summary>
    Auto = 2
}
