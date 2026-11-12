using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PowerForge;

internal static partial class ModuleBootstrapperGenerator
{
    internal static void Generate(
        string moduleRoot,
        string moduleName,
        ExportSet exports,
        IReadOnlyList<string>? exportAssemblies,
        bool handleRuntimes,
        bool useAssemblyLoadContext = false,
        AssemblyTypeAcceleratorExportMode assemblyTypeAcceleratorMode = AssemblyTypeAcceleratorExportMode.None,
        IReadOnlyList<string>? assemblyTypeAccelerators = null,
        IReadOnlyList<string>? assemblyTypeAcceleratorAssemblies = null,
        IReadOnlyList<string>? ignoreLibrariesOnLoad = null,
        IReadOnlyDictionary<string, string[]>? conditionalFunctionDependencies = null,
        ModuleDevelopmentBinaryBootstrapperOptions? developmentBinaries = null,
        IReadOnlyList<string>? targetFrameworks = null,
        bool forceBootstrapperWrite = false,
        Action<string>? log = null,
        IReadOnlyList<string>? sharedAssemblies = null,
        IReadOnlyList<string>? preloadSharedAssemblies = null)
    {
        if (string.IsNullOrWhiteSpace(moduleRoot)) throw new ArgumentException("Module root is required.", nameof(moduleRoot));
        if (string.IsNullOrWhiteSpace(moduleName)) throw new ArgumentException("Module name is required.", nameof(moduleName));

        SharedAssemblyOptions.Validate(sharedAssemblies, preloadSharedAssemblies, exportAssemblies);
        var root = Path.GetFullPath(moduleRoot);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException($"Module root not found: {root}");

        var hasScriptFolders = HasAnyDirectory(root, "Public", "Private", "Classes", "Enums");
        var libRoot = Path.Combine(root, "Lib");
        var exportAssemblyFileNames = ModuleBinaryFileLocator.ResolveAssemblyReferences(moduleName, exportAssemblies);
        SharedAssemblyOptions.Validate(sharedAssemblies, preloadSharedAssemblies, exportAssemblyFileNames);
        var primaryAssemblyName = exportAssemblyFileNames.FirstOrDefault() ?? (moduleName + ".dll");
        var hasLib = ContainsConfiguredOrSelectablePayload(libRoot, exportAssemblies, exportAssemblyFileNames);
        var hasDevelopmentBinaryLoader = developmentBinaries?.Enabled == true;

        // Avoid overwriting "single file" script modules that keep all code in the PSM1 and do not use folder layout.
        // If there is no Lib and no folder-based layout, leave the existing PSM1 intact.
        if (!ShouldWriteBootstrapper(hasLib, hasScriptFolders, hasDevelopmentBinaryLoader, forceBootstrapperWrite)) return;

        var primaryLibraryName = Path.GetFileNameWithoutExtension(primaryAssemblyName);
        if (string.IsNullOrWhiteSpace(primaryLibraryName)) primaryLibraryName = moduleName;

        if (hasLib)
            ModuleLoaderAssets.Write(Path.Combine(root, ModuleLoaderAssets.PackagedFolderName));
        if (hasDevelopmentBinaryLoader)
            ModuleLoaderAssets.Write(Path.Combine(Path.GetFullPath(developmentBinaries!.BinaryRootPath), ModuleLoaderAssets.DevelopmentFolderName));

        if (hasLib)
        {
            var librariesPath = Path.Combine(root, $"{moduleName}.Libraries.ps1");
            var librariesContent = BuildLibrariesScript(
                root,
                moduleName,
                exportAssemblyFileNames,
                (ignoreLibrariesOnLoad ?? Array.Empty<string>()).Concat((sharedAssemblies ?? Array.Empty<string>()).Select(name => name + ".dll")).ToArray(),
                targetFrameworks);
            WritePowerShellFile(librariesPath, librariesContent);
        }

        var psm1Path = Path.Combine(root, $"{moduleName}.psm1");
        var psm1Content = BuildBootstrapperPsm1(
            moduleName,
            primaryLibraryName,
            exportAssemblyFileNames,
            exports,
            includeBinaryLoader: hasLib,
            includeScriptLoader: hasScriptFolders,
            handleRuntimes: handleRuntimes,
            useAssemblyLoadContext: useAssemblyLoadContext,
            assemblyTypeAcceleratorMode: assemblyTypeAcceleratorMode,
            assemblyTypeAccelerators: assemblyTypeAccelerators,
            assemblyTypeAcceleratorAssemblies: assemblyTypeAcceleratorAssemblies,
            ignoreLibrariesOnLoad: ignoreLibrariesOnLoad,
            conditionalFunctionDependencies: conditionalFunctionDependencies,
            developmentBinaries: developmentBinaries,
            moduleRoot: root,
            targetFrameworks: targetFrameworks,
            sharedAssemblies: sharedAssemblies,
            preloadSharedAssemblies: preloadSharedAssemblies);
        WritePowerShellFile(psm1Path, psm1Content);
    }

    internal static bool ShouldWriteBootstrapper(
        string moduleRoot,
        string moduleName,
        IReadOnlyList<string>? exportAssemblies,
        bool forceBootstrapperWrite = false)
    {
        var root = Path.GetFullPath(moduleRoot);
        var hasScriptFolders = HasAnyDirectory(root, "Public", "Private", "Classes", "Enums");
        var libRoot = Path.Combine(root, "Lib");
        var assemblyReferences = ModuleBinaryFileLocator.ResolveAssemblyReferences(moduleName, exportAssemblies);
        var hasLib = ContainsConfiguredOrSelectablePayload(libRoot, exportAssemblies, assemblyReferences);
        return ShouldWriteBootstrapper(hasLib, hasScriptFolders, hasDevelopmentBinaryLoader: false, forceBootstrapperWrite);
    }

    private static bool ContainsConfiguredOrSelectablePayload(
        string libRoot,
        IReadOnlyList<string>? configuredAssemblies,
        IReadOnlyList<string> resolvedAssemblyReferences)
    {
        var hasExplicitAssembly = configuredAssemblies?.Any(static entry => !string.IsNullOrWhiteSpace(entry)) == true;
        if (!hasExplicitAssembly)
            return ModuleBinaryFileLocator.ContainsAnySelectablePayloadFileName(libRoot, resolvedAssemblyReferences);
        if (!Directory.Exists(libRoot)) return false;

        // Explicit assemblies may live in custom folders, but archive-like runtime folders
        // must not turn a script module into a binary bootstrapper.
        return ModuleBinaryFileLocator.ContainsAnyFileName(libRoot, resolvedAssemblyReferences, SearchOption.TopDirectoryOnly) ||
               Directory.EnumerateDirectories(libRoot).Where(folder =>
                   !ModuleBinaryPayloadLayout.IsPayloadLikeFolderName(Path.GetFileName(folder)) ||
                   ModuleBinaryPayloadLayout.IsSelectablePayloadFolderName(Path.GetFileName(folder)))
                   .Any(folder => ModuleBinaryFileLocator.ContainsAnyFileName(folder, resolvedAssemblyReferences, SearchOption.AllDirectories));
    }

    private static bool ShouldWriteBootstrapper(
        bool hasLib,
        bool hasScriptFolders,
        bool hasDevelopmentBinaryLoader,
        bool forceBootstrapperWrite)
        => hasLib || hasScriptFolders || hasDevelopmentBinaryLoader || forceBootstrapperWrite;

    private static bool HasAnyDirectory(string root, params string[] directoryNames)
        => (directoryNames ?? Array.Empty<string>())
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .Any(d => Directory.Exists(Path.Combine(root, d)));

    private static void WritePowerShellFile(string path, string content)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        GeneratedTextNormalizer.WriteUtf8Bom(path, content);
    }

    private static string BuildLibrariesScript(
        string moduleRoot,
        string moduleName,
        IReadOnlyList<string> exportAssemblyFileNames,
        IReadOnlyList<string>? ignoreLibrariesOnLoad,
        IReadOnlyList<string>? targetFrameworks)
    {
        // Generate a deterministic list of DLLs to Add-Type for each Lib/<Folder>.
        var libRoot = Path.Combine(moduleRoot, "Lib");
        var ignored = NormalizeFileNameSet(ignoreLibrariesOnLoad);
        var byFolder = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in Directory.EnumerateDirectories(libRoot, "*", SearchOption.AllDirectories)
                     .Select(path => path.Substring(libRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                     .Where(static folder => !string.IsNullOrWhiteSpace(folder))
                     .Select(static folder => folder.Replace('\\', '/'))
                     .OrderBy(GetPayloadFolderSortOrder)
                     .ThenBy(static folder => folder, StringComparer.OrdinalIgnoreCase))
        {
            byFolder[folder] = EnumerateDllRelativePaths(
                libRoot,
                moduleName,
                folder,
                exportAssemblyFileNames,
                ignored);
        }
        byFolder[""] = EnumerateDllRelativePaths(libRoot, moduleName, null, exportAssemblyFileNames, ignored);

        var map = BuildLibrariesByFolderMap(byFolder);
        var template = EmbeddedScripts.Load("Scripts/ModuleBootstrapper/Libraries.Template.ps1");
        var tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ModuleName"] = moduleName,
            ["LibrariesByFolderMap"] = map,
            ["RuntimePayloadSelectorBlock"] = ModuleBinaryPayloadLayout.BuildPowerShellRuntimeSelector()
        };
        return ScriptTemplateRenderer.Render("ModuleBootstrapper.Libraries", template, tokens);
    }

    private static string BuildLibrariesByFolderMap(IReadOnlyDictionary<string, List<string>> byFolder)
    {
        var sb = new StringBuilder(1024);
        var nonEmptyKeys = byFolder.Keys
            .Where(key => byFolder.TryGetValue(key, out var list) && list is { Count: > 0 })
            .OrderBy(GetPayloadFolderSortOrder)
            .ThenBy(static key => key, StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (nonEmptyKeys.Length == 0)
        {
            sb.AppendLine("$LibrariesByFolder = @{}");
        }
        else
        {
            sb.AppendLine("$LibrariesByFolder = @{");

            foreach (var key in nonEmptyKeys)
            {
                byFolder.TryGetValue(key, out var list);
                list ??= new List<string>();

                sb.Append("    ");
                sb.Append('\'').Append(EscapePsSingleQuoted(key)).Append('\'');
                sb.Append(" = @(").AppendLine();

                foreach (var rel in list)
                    sb.Append("        '").Append(EscapePsSingleQuoted(rel)).AppendLine("'");

                sb.AppendLine("    )");
            }

            sb.AppendLine("}");
        }
        return sb.ToString();
    }

    private static int GetPayloadFolderSortOrder(string? folder)
    {
        if (folder is null || folder.Length == 0) return 40;
        if (folder.Equals("Core", StringComparison.OrdinalIgnoreCase)) return 0;
        if (folder.StartsWith("Core-", StringComparison.OrdinalIgnoreCase)) return 1;
        if (folder.Equals("Default", StringComparison.OrdinalIgnoreCase)) return 10;
        if (folder.StartsWith("Default-", StringComparison.OrdinalIgnoreCase)) return 11;
        if (folder.Equals("Standard", StringComparison.OrdinalIgnoreCase)) return 20;
        if (folder.StartsWith("Standard-", StringComparison.OrdinalIgnoreCase)) return 21;
        return 30;
    }

    private static List<string> EnumerateDllRelativePaths(
        string libRoot,
        string moduleName,
        string? folderName,
        IReadOnlyList<string> exportAssemblyFileNames,
        ISet<string> ignoredLibraryFileNames)
    {
        var list = new List<string>();

        var dir = string.IsNullOrWhiteSpace(folderName) ? libRoot : Path.Combine(libRoot, folderName);
        if (!Directory.Exists(dir)) return list;

        string[] dllFiles;
        try
        {
            dllFiles = ModuleBinaryFileLocator.Enumerate(dir, SearchOption.TopDirectoryOnly)
                .Select(Path.GetFileName)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Where(n => !IsAssemblyLoadContextLoaderFileName(moduleName, n!))
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()!;
        }
        catch
        {
            return list;
        }

        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var ignored in ignoredLibraryFileNames)
            excluded.Add(ignored);

        var exportLast = new HashSet<string>(
            (exportAssemblyFileNames ?? Array.Empty<string>())
                .Select(Path.GetFileName)
                .OfType<string>()
                .Where(static fileName => !string.IsNullOrWhiteSpace(fileName)),
            StringComparer.OrdinalIgnoreCase);
        foreach (var name in OrderManagedLibrariesForDesktopPreload(dir, dllFiles, excluded, exportLast))
            list.Add(RelativeLibPath(folderName, name));

        return list;

        static string RelativeLibPath(string? folder, string fileName)
        {
            var parts = new List<string> { "Lib" };
            if (!string.IsNullOrWhiteSpace(folder)) parts.Add(folder!);
            parts.Add(fileName);
            return string.Join("\\", parts);
        }
    }

    private static ISet<string> NormalizeFileNameSet(IReadOnlyList<string>? values)
    {
        var set = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var value in values ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(value)) continue;
            var fileName = Path.GetFileName(value.Trim().Trim('"'));
            if (string.IsNullOrWhiteSpace(fileName)) continue;
            set.Add(fileName);
        }

        return set;
    }

    private static string EscapePsSingleQuoted(string value)
        => (value ?? string.Empty).Replace("'", "''");

    private static string BuildBootstrapperPsm1(
        string moduleName,
        string libraryName,
        IReadOnlyList<string> libraryFileNames,
        ExportSet exports,
        bool includeBinaryLoader,
        bool includeScriptLoader,
        bool handleRuntimes,
        bool useAssemblyLoadContext,
        AssemblyTypeAcceleratorExportMode assemblyTypeAcceleratorMode,
        IReadOnlyList<string>? assemblyTypeAccelerators,
        IReadOnlyList<string>? assemblyTypeAcceleratorAssemblies,
        IReadOnlyList<string>? ignoreLibrariesOnLoad,
        IReadOnlyDictionary<string, string[]>? conditionalFunctionDependencies,
        ModuleDevelopmentBinaryBootstrapperOptions? developmentBinaries = null,
        string? moduleRoot = null,
        IReadOnlyList<string>? targetFrameworks = null,
        IReadOnlyList<string>? sharedAssemblies = null,
        IReadOnlyList<string>? preloadSharedAssemblies = null)
    {
        var binaryLoaderBlock = includeBinaryLoader
            ? RenderModuleBootstrapperTemplate(
                useAssemblyLoadContext ? "AssemblyLoadContextBinaryLoader" : "BinaryLoader",
                useAssemblyLoadContext
                    ? "Scripts/ModuleBootstrapper/AssemblyLoadContextBinaryLoader.Template.ps1"
                    : "Scripts/ModuleBootstrapper/BinaryLoader.Template.ps1",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["LibraryFileNames"] = BuildPowerShellArrayLiteral(libraryFileNames),
                    ["BinaryAssemblyResolverBlock"] = RenderModuleBootstrapperTemplate(
                        "BinaryAssemblyResolver",
                        "Scripts/ModuleBootstrapper/BinaryAssemblyResolver.Template.ps1",
                        new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                        {
                            ["LibraryFileNames"] = BuildPowerShellArrayLiteral(libraryFileNames),
                            ["RuntimePayloadSelectorBlock"] = ModuleBinaryPayloadLayout.BuildPowerShellRuntimeSelector().TrimEnd(),
                            ["DesktopScriptHelperBlock"] = includeScriptLoader && exports.Functions.Length > 0 &&
                                exports.Cmdlets.Length == 0 && assemblyTypeAcceleratorMode == AssemblyTypeAcceleratorExportMode.None &&
                                libraryFileNames.All(name => name.IndexOfAny(new[] { '/', '\\' }) < 0 && !Path.IsPathRooted(name))
                                ? RenderModuleBootstrapperTemplate("DesktopScriptHelper",
                                    "Scripts/ModuleBootstrapper/DesktopScriptHelper.Template.ps1",
                                    new Dictionary<string, string>()).TrimEnd()
                                : string.Empty
                        }).TrimEnd(),
                    ["ModuleName"] = EscapePsSingleQuoted(moduleName),
                    ["ModuleLoaderImportBlock"] = BuildModuleLoaderImportBlock("$PowerForgeModuleRoot, '" + ModuleLoaderAssets.PackagedFolderName + "'"),
                    ["SharedAssemblyNames"] = BuildPowerShellArrayLiteral(NormalizePowerShellStringArray(sharedAssemblies)),
                    ["RuntimePayloadSelectorBlock"] = ModuleBinaryPayloadLayout.BuildPowerShellRuntimeSelector(),
                    ["SharedAssembliesBlock"] = BuildSharedAssembliesBlock(sharedAssemblies, preloadSharedAssemblies,
                        "@($LibraryFileNames | ForEach-Object { (& $ResolvePowerForgeModuleAssembly -LibraryFileName $_).Directory } | Select-Object -Unique)"),
                    ["DesktopAssemblyResolverBlock"] = BuildDesktopAssemblyResolverBlock(sharedAssemblies, ignoreLibrariesOnLoad),
                    ["RuntimeHandlerBlock"] = handleRuntimes ? BuildRuntimeHandlerBlock() : string.Empty,
                    ["TypeAcceleratorBlock"] = IndentPowerShell(BuildTypeAcceleratorBlock(
                        assemblyTypeAcceleratorMode,
                        assemblyTypeAccelerators,
                        assemblyTypeAcceleratorAssemblies,
                        "$LibraryDirectory").TrimEnd(), 16),
                    ["ExportBridgeBlock"] = IndentPowerShell(
                        BuildPowerShellModuleExportBridge("$InnerModule").TrimEnd(),
                        20),
                    ["DesktopTypeAcceleratorBlock"] = IndentPowerShell(
                        BuildDesktopTypeAcceleratorBlock(
                            assemblyTypeAcceleratorMode,
                            assemblyTypeAccelerators,
                            assemblyTypeAcceleratorAssemblies,
                            "$PowerForgeDesktopBinaryDirectory",
                            ignoreLibrariesOnLoad).TrimEnd(),
                        8)
                })
            : string.Empty;

        if (developmentBinaries?.Enabled == true)
        {
            var developmentBlock = BuildDevelopmentBinaryLoaderBlock(
                moduleRoot ?? string.Empty,
                moduleName,
                libraryName,
                useAssemblyLoadContext,
                handleRuntimes,
                assemblyTypeAcceleratorMode,
                assemblyTypeAccelerators,
                assemblyTypeAcceleratorAssemblies,
                ignoreLibrariesOnLoad,
                developmentBinaries, sharedAssemblies, preloadSharedAssemblies);

            var hasPackagedBinaryLoader = !string.IsNullOrWhiteSpace(binaryLoaderBlock);
            binaryLoaderBlock = RenderModuleBootstrapperTemplate(
                hasPackagedBinaryLoader ? "DevelopmentBinarySelection" : "DevelopmentOnlyBinarySelection",
                hasPackagedBinaryLoader
                    ? "Scripts/ModuleBootstrapper/DevelopmentBinarySelection.Template.ps1"
                    : "Scripts/ModuleBootstrapper/DevelopmentOnlyBinarySelection.Template.ps1",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["DevelopmentBinaryLoaderBlock"] = developmentBlock.TrimEnd(),
                    ["PackagedBinaryLoaderBlock"] = hasPackagedBinaryLoader
                        ? IndentPowerShell(binaryLoaderBlock.TrimEnd(), 4)
                        : string.Empty
                });
        }

        var scriptLoaderBlock = includeScriptLoader
            ? RenderModuleBootstrapperTemplate(
                "ScriptLoader",
                "Scripts/ModuleBootstrapper/ScriptLoader.Template.ps1",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["ModuleRootExpression"] = includeBinaryLoader
                        ? "$PowerForgeModuleRoot"
                        : "$PSScriptRoot"
                })
            : string.Empty;

        var tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ModuleName"] = moduleName,
            ["ScriptPreambleBlock"] = string.Empty,
            ["ModuleRootCaptureBlock"] = includeBinaryLoader
                ? "$PowerForgeModuleRoot = $PSScriptRoot" + Environment.NewLine +
                  "$PowerForgeModulePath = $PSCommandPath"
                : string.Empty,
            ["BinaryLoaderBlock"] = binaryLoaderBlock,
            ["ScriptLoaderBlock"] = scriptLoaderBlock,
            ["ExportBlock"] = ModuleConditionalExportBlockBuilder.BuildExportBlock(
                exports,
                conditionalFunctionDependencies,
                moduleName).TrimEnd()
        };

        var template = EmbeddedScripts.Load("Scripts/ModuleBootstrapper/Bootstrapper.Template.ps1");
        return ScriptTemplateRenderer.Render("ModuleBootstrapper.Bootstrapper", template, tokens);
    }

    private static string BuildRuntimeHandlerBlock()
    {
        return RenderModuleBootstrapperTemplate(
            "RuntimeHandler",
            "Scripts/ModuleBootstrapper/RuntimeHandler.Template.ps1",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ArchitectureResolverBlock"] = IndentPowerShell(
                    RenderWindowsRuntimeArchitectureResolver("$Arch", "$ArchFolder").TrimEnd(),
                    4)
            });
    }

    private static string BuildDesktopAssemblyResolverBlock(IReadOnlyList<string>? sharedAssemblies, IReadOnlyList<string>? ignoreLibrariesOnLoad)
    {
        return RenderModuleBootstrapperTemplate(
            "DesktopAssemblyResolver",
            "Scripts/ModuleBootstrapper/DesktopAssemblyResolver.Template.ps1",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["SharedAssemblyNames"] = BuildPowerShellArrayLiteral(NormalizePowerShellStringArray(sharedAssemblies)),
                ["IgnoredLibraryFileNames"] = BuildPowerShellArrayLiteral(NormalizeFileNameSet(ignoreLibrariesOnLoad)
                    .OrderBy(static value => value, StringComparer.OrdinalIgnoreCase).ToArray())
            });
    }

    /// <summary>Loads the precompiled loader for the running edition; <paramref name="loaderRootParts"/> are Path.Combine arguments.</summary>
    internal static string BuildModuleLoaderImportBlock(string loaderRootParts)
        => RenderModuleBootstrapperTemplate(
            "ModuleLoaderImport",
            "Scripts/ModuleBootstrapper/ModuleLoaderImport.Template.ps1",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["LoaderRootParts"] = loaderRootParts,
                ["LoaderFileName"] = ModuleLoaderAssets.FileName,
                ["LoaderProbeType"] = ModuleLoaderAssets.ProbeTypeName
            });

    internal static string BuildTypeAcceleratorBlock(
        AssemblyTypeAcceleratorExportMode mode,
        IReadOnlyList<string>? typeNames,
        IReadOnlyList<string>? assemblyNames,
        string libraryDirectoryExpression)
    {
        if (mode == AssemblyTypeAcceleratorExportMode.None)
            return string.Empty;

        return RenderModuleBootstrapperTemplate(
            "AssemblyTypeAccelerators",
            "Scripts/ModuleBootstrapper/AssemblyTypeAccelerators.Template.ps1",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Mode"] = mode.ToString(),
                ["RequestedTypes"] = BuildPowerShellArrayLiteral(NormalizePowerShellStringArray(typeNames)),
                ["RequestedAssemblies"] = BuildPowerShellArrayLiteral(NormalizePowerShellStringArray(assemblyNames)),
                ["LibraryDirectoryExpression"] = libraryDirectoryExpression,
                ["RegistrationTrackingBlock"] = BuildTypeAcceleratorTrackingBlock(4)
            });
    }

    private static string[] NormalizePowerShellStringArray(IReadOnlyList<string>? values)
        => values is { Count: > 0 }
            ? values
                .Where(static value => !string.IsNullOrWhiteSpace(value))
                .Select(static value => value.Trim())
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()
            : Array.Empty<string>();

    private static string BuildTypeAcceleratorTrackingBlock(int indent)
        => IndentPowerShell(RenderModuleBootstrapperTemplate(
            "TypeAcceleratorTracking", "Scripts/ModuleBootstrapper/TypeAcceleratorTracking.Template.ps1",
            new Dictionary<string, string>()), indent).TrimEnd();

    private static string BuildPowerShellArrayLiteral(IReadOnlyList<string> values)
    {
        if (values.Count == 0)
            return "@()";

        return "@(" + string.Join(", ", values.Select(static value => "'" + EscapePsSingleQuoted(value) + "'")) + ")";
    }

    private static string RenderModuleBootstrapperTemplate(
        string templateName,
        string embeddedPath,
        IReadOnlyDictionary<string, string> tokens)
    {
        var template = EmbeddedScripts.Load(embeddedPath);
        return ScriptTemplateRenderer.Render("ModuleBootstrapper." + templateName, template, tokens);
    }

}
