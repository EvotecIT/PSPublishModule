namespace PowerForge;

internal static partial class ModuleBootstrapperGenerator
{
    private static string BuildDevelopmentBinaryLoaderBlock(
        string moduleRoot,
        string moduleName,
        string libraryName,
        bool useAssemblyLoadContext,
        bool handleRuntimes,
        AssemblyTypeAcceleratorExportMode assemblyTypeAcceleratorMode,
        IReadOnlyList<string>? assemblyTypeAccelerators,
        IReadOnlyList<string>? assemblyTypeAcceleratorAssemblies,
        IReadOnlyList<string>? ignoreLibrariesOnLoad,
        ModuleDevelopmentBinaryBootstrapperOptions options,
        IReadOnlyList<string>? sharedAssemblies = null,
        IReadOnlyList<string>? preloadSharedAssemblies = null)
    {
        var binaryRootExpression = BuildPowerShellPathExpression(moduleRoot, options.BinaryRootPath);
        var coreFrameworks = BuildPowerShellArrayLiteral(NormalizePowerShellStringArray(options.CoreFrameworkCandidates));
        var desktopFrameworks = BuildPowerShellArrayLiteral(NormalizePowerShellStringArray(options.DesktopFrameworkCandidates));
        return RenderModuleBootstrapperTemplate(
            "DevelopmentBinaryLoader",
            "Scripts/ModuleBootstrapper/DevelopmentBinaryLoader.Template.ps1",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ModuleLoaderImportBlock"] = IndentPowerShell(BuildModuleLoaderImportBlock(
                    "$PowerForgeDevelopmentBinaryRoot, '" + ModuleLoaderAssets.DevelopmentFolderName + "'").TrimEnd(), 12),
                ["SharedAssembliesBlock"] = BuildSharedAssembliesBlock(sharedAssemblies, preloadSharedAssemblies,
                    "@([IO.Path]::GetDirectoryName($PowerForgeDevelopmentBinaryPath))"),
                ["BinaryRootExpression"] = binaryRootExpression,
                ["DevelopmentBinaryMode"] = EscapePsSingleQuoted(options.Mode.ToString()),
                ["DevelopmentBinaryEnvironmentVariable"] = EscapePsSingleQuoted(options.EnvironmentVariable),
                ["DevelopmentConfigurationEnvironmentVariable"] = EscapePsSingleQuoted(options.ConfigurationEnvironmentVariable),
                ["DevelopmentCoreFrameworks"] = coreFrameworks,
                ["DevelopmentDesktopFrameworks"] = desktopFrameworks,
                ["LibraryFileName"] = EscapePsSingleQuoted(libraryName + ".dll"),
                ["RuntimeHandlerBlock"] = handleRuntimes
                    ? IndentPowerShell(BuildDevelopmentRuntimeHandlerBlock().TrimEnd(), 12)
                    : string.Empty,
                ["DesktopTypeAcceleratorBlock"] = IndentPowerShell(
                    BuildDesktopTypeAcceleratorBlock(
                        assemblyTypeAcceleratorMode,
                        assemblyTypeAccelerators,
                        assemblyTypeAcceleratorAssemblies,
                        "[IO.Path]::GetDirectoryName($PowerForgeDevelopmentBinaryPath)",
                        ignoreLibrariesOnLoad).TrimEnd(),
                    12),
                ["ImportBlock"] = IndentPowerShell(BuildDevelopmentImportBlock(
                    moduleName,
                    libraryName,
                    useAssemblyLoadContext,
                    assemblyTypeAcceleratorMode,
                    assemblyTypeAccelerators,
                    assemblyTypeAcceleratorAssemblies, sharedAssemblies), 12)
            });
    }

    private static string BuildDevelopmentImportBlock(
        string moduleName,
        string libraryName,
        bool useAssemblyLoadContext,
        AssemblyTypeAcceleratorExportMode assemblyTypeAcceleratorMode,
        IReadOnlyList<string>? assemblyTypeAccelerators,
        IReadOnlyList<string>? assemblyTypeAcceleratorAssemblies, IReadOnlyList<string>? sharedAssemblies = null)
    {
        var plainImportBlock = RenderModuleBootstrapperTemplate(
            "DevelopmentBinaryImport",
            "Scripts/ModuleBootstrapper/DevelopmentBinaryImport.Template.ps1",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["LibraryTypeName"] = EscapePsSingleQuoted(libraryName + ".Initialize"),
                ["ImportedAliasesBlock"] = BuildImportedAliasesBlock()
            }).TrimEnd();
        if (!useAssemblyLoadContext)
            return plainImportBlock;

        return "if ($PSEdition -eq 'Core') {" + Environment.NewLine +
               IndentPowerShell(BuildAssemblyLoadContextImportBlock(
                   "@($PowerForgeDevelopmentBinaryPath)",
                   "$PowerForgeDevelopmentBinaryPath",
                   moduleName + ".Development",
                   "[IO.Path]::GetDirectoryName($PowerForgeDevelopmentBinaryPath)",
                   assemblyTypeAcceleratorMode,
                   assemblyTypeAccelerators,
                   assemblyTypeAcceleratorAssemblies,
                   sharedAssemblies), 4) + Environment.NewLine +
               "} else {" + Environment.NewLine +
               IndentPowerShell(plainImportBlock, 4) + Environment.NewLine +
               "}";
    }

    private static string BuildPowerShellModuleExportBridge(string innerModuleExpression)
        => RenderModuleBootstrapperTemplate(
            "PowerShellModuleExportBridge",
            "Scripts/ModuleBootstrapper/PowerShellModuleExportBridge.Template.ps1",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["InnerModuleExpression"] = innerModuleExpression
            });

    private static string BuildDevelopmentRuntimeHandlerBlock()
    {
        return RenderModuleBootstrapperTemplate(
            "DevelopmentRuntimeHandler",
            "Scripts/ModuleBootstrapper/DevelopmentRuntimeHandler.Template.ps1",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ArchitectureResolverBlock"] = IndentPowerShell(
                    RenderWindowsRuntimeArchitectureResolver("$PowerForgeDevelopmentArch", "$PowerForgeDevelopmentArchFolder").TrimEnd(),
                    4)
            });
    }

    private static string BuildPowerShellPathExpression(string moduleRoot, string targetPath)
    {
        var fullTarget = Path.GetFullPath(targetPath);
        var fullRoot = string.IsNullOrWhiteSpace(moduleRoot)
            ? string.Empty
            : Path.GetFullPath(moduleRoot);

        if (!string.IsNullOrWhiteSpace(fullRoot) &&
            TryBuildRelativePowerShellPathExpression(fullRoot, fullTarget, out var relativeExpression))
        {
            return relativeExpression;
        }

        return "'" + EscapePsSingleQuoted(fullTarget) + "'";
    }

    private static bool TryBuildRelativePowerShellPathExpression(
        string fullRoot,
        string fullTarget,
        out string expression)
    {
        expression = string.Empty;

        try
        {
            var relative = FrameworkCompatibility.GetRelativePath(fullRoot, fullTarget);
            if (string.IsNullOrWhiteSpace(relative) || Path.IsPathRooted(relative) || relative.IndexOf(':') >= 0)
                return false;

            var parts = relative
                .Split(new[] { Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar }, StringSplitOptions.RemoveEmptyEntries)
                .Where(static part => part != ".")
                .ToArray();

            if (parts.Length == 0)
            {
                expression = "$PSScriptRoot";
                return true;
            }

            var args = string.Join(", ", new[] { "$PSScriptRoot" }.Concat(parts.Select(part => "'" + EscapePsSingleQuoted(part) + "'")));
            expression = "[IO.Path]::GetFullPath([IO.Path]::Combine(" + args + "))";
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string IndentPowerShell(string content, int spaces)
    {
        if (string.IsNullOrEmpty(content)) return string.Empty;

        var prefix = new string(' ', spaces);
        var lines = content.Replace("\r\n", "\n").Split('\n');
        return string.Join(
            "\r\n",
            lines.Select(line => line.Length == 0 ? string.Empty :
                // PowerShell here-string terminators must remain at column zero.
                line.StartsWith("'@", StringComparison.Ordinal) || line.StartsWith("\"@", StringComparison.Ordinal)
                    ? line : prefix + line));
    }
}
