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

        BinaryPayloadPlan? payloadPlan = null;
        if (hasLib)
        {
            var librariesByFolder = BuildLibrariesByFolder(
                root,
                moduleName,
                exportAssemblyFileNames,
                (ignoreLibrariesOnLoad ?? Array.Empty<string>()).Concat((sharedAssemblies ?? Array.Empty<string>()).Select(name => name + ".dll")).ToArray());
            var desktopMayDropCoreOnlyHelper = hasScriptFolders && exports.Functions.Length > 0 &&
                                               exports.Cmdlets.Length == 0 && assemblyTypeAcceleratorMode == AssemblyTypeAcceleratorExportMode.None &&
                                               exportAssemblyFileNames.All(name => name.IndexOfAny(new[] { '/', '\\' }) < 0 && !Path.IsPathRooted(name));
            payloadPlan = ResolveBinaryPayloadPlan(root, exportAssemblyFileNames, librariesByFolder, desktopMayDropCoreOnlyHelper, targetFrameworks);

            var librariesPath = Path.Combine(root, $"{moduleName}.Libraries.ps1");
            WritePowerShellFile(librariesPath, BuildLibrariesScript(moduleName, payloadPlan));
        }

        var psm1Path = Path.Combine(root, $"{moduleName}.psm1");
        var psm1Content = BuildBootstrapperPsm1(
            moduleName,
            primaryLibraryName,
            payloadPlan,
            exports,
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

    private static Dictionary<string, List<string>> BuildLibrariesByFolder(
        string moduleRoot,
        string moduleName,
        IReadOnlyList<string> exportAssemblyFileNames,
        IReadOnlyList<string>? ignoreLibrariesOnLoad)
    {
        // A deterministic list of DLLs to Add-Type for each Lib/<Folder>, dependencies before the assemblies that use them.
        var libRoot = Path.Combine(moduleRoot, "Lib");
        var ignored = NormalizeFileNameSet(ignoreLibrariesOnLoad);
        var byFolder = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var folder in Directory.EnumerateDirectories(libRoot, "*", SearchOption.AllDirectories)
                     .Select(path => path.Substring(libRoot.Length).TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar))
                     .Where(static folder => !string.IsNullOrWhiteSpace(folder))
                     .Select(static folder => folder.Replace('\\', '/')))
        {
            byFolder[folder] = EnumerateDllRelativePaths(libRoot, moduleName, folder, exportAssemblyFileNames, ignored);
        }
        byFolder[""] = EnumerateDllRelativePaths(libRoot, moduleName, null, exportAssemblyFileNames, ignored);
        return byFolder;
    }

    private static string BuildLibrariesScript(string moduleName, BinaryPayloadPlan plan)
    {
        var template = EmbeddedScripts.Load("Scripts/ModuleBootstrapper/Libraries.Template.ps1");
        var tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ModuleName"] = moduleName,
            ["LibrariesToLoad"] = RenderPerHostValue(plan, static scenario => BuildMultilineArrayLiteral(scenario.Libraries))
        };
        return ScriptTemplateRenderer.Render("ModuleBootstrapper.Libraries", template, tokens);
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
        BinaryPayloadPlan? payloadPlan,
        ExportSet exports,
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
        IReadOnlyList<string>? sharedAssemblies = null,
        IReadOnlyList<string>? preloadSharedAssemblies = null)
    {
        var includeBinaryLoader = payloadPlan is not null;
        var binaryLoaderBlock = payloadPlan is null
            ? string.Empty
            : BuildPackagedBinaryLoaderBlock(
                moduleName,
                payloadPlan,
                moduleRoot ?? string.Empty,
                handleRuntimes,
                useAssemblyLoadContext,
                assemblyTypeAcceleratorMode,
                assemblyTypeAccelerators,
                assemblyTypeAcceleratorAssemblies,
                ignoreLibrariesOnLoad,
                sharedAssemblies,
                preloadSharedAssemblies);

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
            ["BinaryLoaderBlock"] = string.IsNullOrWhiteSpace(binaryLoaderBlock)
                ? string.Empty
                : "    # The loader runs in its own scope, so its working variables do not stay in the module." + Environment.NewLine +
                  "    & {" + Environment.NewLine +
                  IndentPowerShell(binaryLoaderBlock.TrimEnd(), 8) + Environment.NewLine +
                  "    }",
            ["FailedImportCleanupBlock"] = assemblyTypeAcceleratorMode == AssemblyTypeAcceleratorExportMode.None || string.IsNullOrWhiteSpace(binaryLoaderBlock)
                ? string.Empty
                : RenderModuleBootstrapperTemplate("TypeAcceleratorFailedImportCleanup",
                    "Scripts/ModuleBootstrapper/TypeAcceleratorFailedImportCleanup.Template.ps1", new Dictionary<string, string>()).TrimEnd(),
            ["ScriptLoaderBlock"] = scriptLoaderBlock,
            ["ExportBlock"] = ModuleConditionalExportBlockBuilder.BuildExportBlock(
                exports,
                conditionalFunctionDependencies,
                moduleName).TrimEnd()
        };

        var template = EmbeddedScripts.Load("Scripts/ModuleBootstrapper/Bootstrapper.Template.ps1");
        return ScriptTemplateRenderer.Render("ModuleBootstrapper.Bootstrapper", template, tokens);
    }

    private static string BuildPackagedBinaryLoaderBlock(
        string moduleName,
        BinaryPayloadPlan payloadPlan,
        string moduleRoot,
        bool handleRuntimes,
        bool useAssemblyLoadContext,
        AssemblyTypeAcceleratorExportMode assemblyTypeAcceleratorMode,
        IReadOnlyList<string>? assemblyTypeAccelerators,
        IReadOnlyList<string>? assemblyTypeAcceleratorAssemblies,
        IReadOnlyList<string>? ignoreLibrariesOnLoad,
        IReadOnlyList<string>? sharedAssemblies,
        IReadOnlyList<string>? preloadSharedAssemblies)
    {
        var plainImportBlock = RenderModuleBootstrapperTemplate(
            "BinaryModuleImport",
            "Scripts/ModuleBootstrapper/BinaryModuleImport.Template.ps1",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ImportedAliasesBlock"] = BuildImportedAliasesBlock()
            }).TrimEnd();
        var importBlock = useAssemblyLoadContext
            ? "if ($PSEdition -eq 'Core') {" + Environment.NewLine +
              IndentPowerShell(BuildAssemblyLoadContextImportBlock(
                  "$BinaryModulePaths",
                  "$ModuleAssemblyPath",
                  moduleName,
                  "$LibraryDirectory",
                  assemblyTypeAcceleratorMode,
                  assemblyTypeAccelerators,
                  assemblyTypeAcceleratorAssemblies,
                  sharedAssemblies), 4) + Environment.NewLine +
              "} else {" + Environment.NewLine +
              IndentPowerShell(plainImportBlock, 4) + Environment.NewLine +
              "}"
            : plainImportBlock;
        var desktopTypeAcceleratorBlock = BuildDesktopTypeAcceleratorBlock(
            assemblyTypeAcceleratorMode,
            assemblyTypeAccelerators,
            assemblyTypeAcceleratorAssemblies,
            "$LoadedDirectory",
            ignoreLibrariesOnLoad).TrimEnd();

        return RenderModuleBootstrapperTemplate(
            "BinaryLoader",
            "Scripts/ModuleBootstrapper/BinaryLoader.Template.ps1",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ModuleName"] = EscapePsSingleQuoted(moduleName),
                ["ModuleLoaderImportBlock"] = BuildModuleLoaderImportBlock("$PowerForgeModuleRoot, '" + ModuleLoaderAssets.PackagedFolderName + "'").TrimEnd(),
                ["BinaryModules"] = RenderPerHostValue(payloadPlan, static scenario => BuildBinaryModulesLiteral(scenario.Modules)),
                ["SharedAssembliesBlock"] = BuildSharedAssembliesBlock(sharedAssemblies, preloadSharedAssemblies, "$BinaryModuleDirectories"),
                ["DesktopAssemblyResolverBlock"] = BuildDesktopAssemblyResolverBlock(sharedAssemblies, ignoreLibrariesOnLoad),
                ["RuntimeHandlerBlock"] = handleRuntimes ? BuildRuntimeHandlerBlock(moduleRoot) : string.Empty,
                ["ImportBlock"] = IndentPowerShell(importBlock, 8),
                // Without an AssemblyLoadContext, PowerShell 7 loads the remaining dependencies after the module.
                ["LateLibrariesBlock"] = useAssemblyLoadContext
                    ? string.Empty
                    : "if ($PSEdition -eq 'Core' -and (Test-Path -LiteralPath $LibrariesScript)) {" + Environment.NewLine +
                      "    . $LibrariesScript" + Environment.NewLine +
                      "}" + Environment.NewLine,
                ["DesktopTypeAcceleratorBlock"] = string.IsNullOrWhiteSpace(desktopTypeAcceleratorBlock)
                    ? string.Empty
                    : "foreach ($LoadedDirectory in $LoadedDirectories) {" + Environment.NewLine +
                      IndentPowerShell(desktopTypeAcceleratorBlock, 4) + Environment.NewLine +
                      "}" + Environment.NewLine
            });
    }

    /// <summary>Imports one module assembly into its AssemblyLoadContext on PowerShell 7 and publishes its commands.</summary>
    private static string BuildAssemblyLoadContextImportBlock(
        string moduleAssemblyPathsExpression,
        string moduleAssemblyPathExpression,
        string contextName,
        string libraryDirectoryExpression,
        AssemblyTypeAcceleratorExportMode assemblyTypeAcceleratorMode,
        IReadOnlyList<string>? assemblyTypeAccelerators,
        IReadOnlyList<string>? assemblyTypeAcceleratorAssemblies,
        IReadOnlyList<string>? sharedAssemblies)
        => RenderModuleBootstrapperTemplate(
            "AssemblyLoadContextImport",
            "Scripts/ModuleBootstrapper/AssemblyLoadContextImport.Template.ps1",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ModuleAssemblyPaths"] = moduleAssemblyPathsExpression,
                ["ModuleAssemblyPath"] = moduleAssemblyPathExpression,
                ["ContextName"] = EscapePsSingleQuoted(contextName),
                ["SharedAssemblyNames"] = BuildPowerShellArrayLiteral(NormalizePowerShellStringArray(sharedAssemblies)),
                ["TypeAcceleratorBlock"] = BuildTypeAcceleratorBlock(
                    assemblyTypeAcceleratorMode,
                    assemblyTypeAccelerators,
                    assemblyTypeAcceleratorAssemblies,
                    libraryDirectoryExpression).TrimEnd(),
                ["ImportedAliasesBlock"] = BuildImportedAliasesBlock(),
                ["ExportBridgeBlock"] = IndentPowerShell(BuildPowerShellModuleExportBridge("$InnerModule").TrimEnd(), 4)
            }).TrimEnd();

    private static string BuildImportedAliasesBlock()
        => IndentPowerShell(RenderModuleBootstrapperTemplate(
            "ImportedAliases",
            "Scripts/ModuleBootstrapper/ImportedAliases.Template.ps1",
            new Dictionary<string, string>()).TrimEnd(), 4);

    private static string BuildRuntimeHandlerBlock(string moduleRoot)
    {
        return RenderModuleBootstrapperTemplate(
            "RuntimeHandler",
            "Scripts/ModuleBootstrapper/RuntimeHandler.Template.ps1",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["ArchitectureResolverBlock"] = IndentPowerShell(
                    RenderWindowsRuntimeArchitectureResolver("$Arch", "$ArchFolder").TrimEnd(),
                    4),
                ["NativeRuntimeRoots"] = BuildPowerShellArrayLiteral(ResolveNativeRuntimeRoots(moduleRoot))
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
