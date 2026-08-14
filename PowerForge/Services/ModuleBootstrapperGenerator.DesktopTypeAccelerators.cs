namespace PowerForge;

internal static partial class ModuleBootstrapperGenerator
{
    internal static string BuildDesktopTypeAcceleratorBlock(
        AssemblyTypeAcceleratorExportMode mode,
        IReadOnlyList<string>? typeNames,
        IReadOnlyList<string>? assemblyNames,
        string libraryDirectoryExpression,
        IReadOnlyList<string>? ignoreLibrariesOnLoad = null)
    {
        var normalizedTypes = NormalizePowerShellStringArray(typeNames);
        var normalizedAssemblies = NormalizePowerShellStringArray(assemblyNames);
        var ignoredLibraryFileNames = NormalizeFileNameSet(ignoreLibrariesOnLoad)
            .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (mode == AssemblyTypeAcceleratorExportMode.None)
            return string.Empty;

        if (string.IsNullOrWhiteSpace(libraryDirectoryExpression))
            throw new ArgumentException("A Desktop library directory expression is required.", nameof(libraryDirectoryExpression));

        return RenderModuleBootstrapperTemplate(
            "DesktopTypeAccelerators",
            "Scripts/ModuleBootstrapper/DesktopTypeAccelerators.Template.ps1",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Mode"] = mode.ToString(),
                ["RequestedTypes"] = BuildPowerShellArrayLiteral(normalizedTypes),
                ["RequestedAssemblies"] = BuildPowerShellArrayLiteral(normalizedAssemblies),
                ["IgnoredLibraryFileNames"] = BuildPowerShellArrayLiteral(ignoredLibraryFileNames),
                ["LibraryDirectoryExpression"] = libraryDirectoryExpression
            });
    }
}
