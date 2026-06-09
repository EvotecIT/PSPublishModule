using System;
using System.Collections.Generic;

namespace PowerForge;

/// <summary>
/// Options for emitting a WiX SDK project around generated WiX source.
/// </summary>
public sealed class PowerForgeWixInstallerProjectOptions
{
    /// <summary>
    /// WiX SDK version used by the generated project.
    /// </summary>
    public string SdkVersion { get; set; } = "4.0.6";

    /// <summary>
    /// WiX platform.
    /// </summary>
    public string Platform { get; set; } = "x64";

    /// <summary>
    /// Generated WiX source file included by the project.
    /// </summary>
    public string SourceFile { get; set; } = "Product.wxs";

    /// <summary>
    /// Additional WiX source files included by the generated project.
    /// </summary>
    public List<string> AdditionalSourceFiles { get; } = new();

    /// <summary>
    /// MSBuild define constants passed to WiX.
    /// </summary>
    public Dictionary<string, string> DefineConstants { get; } = new(StringComparer.OrdinalIgnoreCase);
}
