using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PowerForge;

internal static partial class ModuleBootstrapperGenerator
{
    // net8.0 is the default modern PowerShell LTS baseline when the module build does not declare a Core TFM.
    private const string DefaultAssemblyLoadContextTargetFramework = "net8.0";
    // PowerShell 7.0 runs on .NET Core 3.1; a helper built for that floor loads in every supported later Core host.
    private const string PowerShell70AssemblyLoadContextTargetFramework = "netcoreapp3.1";
    private static readonly TimeSpan AssemblyLoadContextLoaderBuildTimeout = TimeSpan.FromMinutes(10);

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
        Action<string>? log = null)
    {
        if (string.IsNullOrWhiteSpace(moduleRoot)) throw new ArgumentException("Module root is required.", nameof(moduleRoot));
        if (string.IsNullOrWhiteSpace(moduleName)) throw new ArgumentException("Module name is required.", nameof(moduleName));

        var root = Path.GetFullPath(moduleRoot);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException($"Module root not found: {root}");

        var hasScriptFolders = HasAnyDirectory(root, "Public", "Private", "Classes", "Enums");
        var libRoot = Path.Combine(root, "Lib");
        var exportAssemblyFileNames = ModuleBinaryFileLocator.ResolveAssemblyReferences(moduleName, exportAssemblies);
        var primaryAssemblyName = exportAssemblyFileNames.FirstOrDefault() ?? (moduleName + ".dll");
        var hasLib = ModuleBinaryFileLocator.ContainsAnyFileName(libRoot, exportAssemblyFileNames, SearchOption.AllDirectories);
        var hasDevelopmentBinaryLoader = developmentBinaries?.Enabled == true;

        // Avoid overwriting "single file" script modules that keep all code in the PSM1 and do not use folder layout.
        // If there is no Lib and no folder-based layout, leave the existing PSM1 intact.
        if (!ShouldWriteBootstrapper(hasLib, hasScriptFolders, hasDevelopmentBinaryLoader, forceBootstrapperWrite)) return;

        var primaryLibraryName = Path.GetFileNameWithoutExtension(primaryAssemblyName);
        if (string.IsNullOrWhiteSpace(primaryLibraryName)) primaryLibraryName = moduleName;

        var assemblyLoadContextLoaderIdentity = useAssemblyLoadContext
            ? CreateAssemblyLoadContextLoaderIdentity(moduleName)
            : null;

        if (hasLib && useAssemblyLoadContext && assemblyLoadContextLoaderIdentity is not null)
            BuildAssemblyLoadContextLoader(root, exportAssemblyFileNames, assemblyLoadContextLoaderIdentity, ResolveAssemblyLoadContextTargetFramework(targetFrameworks), log);

        if (hasLib)
        {
            var librariesPath = Path.Combine(root, $"{moduleName}.Libraries.ps1");
            var librariesContent = BuildLibrariesScript(
                root,
                moduleName,
                exportAssemblyFileNames,
                assemblyLoadContextLoaderIdentity?.AssemblyName,
                ignoreLibrariesOnLoad,
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
            targetFrameworks: targetFrameworks);
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
        var hasLib = ModuleBinaryFileLocator.ContainsAnyFileName(libRoot, assemblyReferences, SearchOption.AllDirectories);
        return ShouldWriteBootstrapper(hasLib, hasScriptFolders, hasDevelopmentBinaryLoader: false, forceBootstrapperWrite);
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
        string? assemblyLoadContextLoaderAssemblyName,
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
                folder,
                exportAssemblyFileNames,
                assemblyLoadContextLoaderAssemblyName,
                ignored);
        }
        byFolder[""] = EnumerateDllRelativePaths(libRoot, null, exportAssemblyFileNames, assemblyLoadContextLoaderAssemblyName, ignored);

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
        string? folderName,
        IReadOnlyList<string> exportAssemblyFileNames,
        string? assemblyLoadContextLoaderAssemblyName,
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
                .Distinct(StringComparer.OrdinalIgnoreCase)
                .ToArray()!;
        }
        catch
        {
            return list;
        }

        var excluded = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        if (!string.IsNullOrWhiteSpace(assemblyLoadContextLoaderAssemblyName))
            excluded.Add(assemblyLoadContextLoaderAssemblyName + ".dll");
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
        IReadOnlyList<string>? targetFrameworks = null)
    {
        var loaderIdentity = useAssemblyLoadContext
            ? CreateAssemblyLoadContextLoaderIdentity(moduleName)
            : null;

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
                            ["RuntimePayloadSelectorBlock"] = ModuleBinaryPayloadLayout.BuildPowerShellRuntimeSelector().TrimEnd()
                        }).TrimEnd(),
                    ["ModuleName"] = EscapePsSingleQuoted(moduleName),
                    ["LoaderAssemblyName"] = EscapePsSingleQuoted(loaderIdentity?.AssemblyName ?? string.Empty),
                    ["LoaderTypeName"] = loaderIdentity?.TypeName ?? string.Empty,
                    ["RuntimePayloadSelectorBlock"] = ModuleBinaryPayloadLayout.BuildPowerShellRuntimeSelector(),
                    ["DesktopAssemblyResolverBlock"] = BuildDesktopAssemblyResolverBlock(),
                    ["RuntimeHandlerBlock"] = handleRuntimes ? BuildRuntimeHandlerBlock() : string.Empty,
                    ["TypeAcceleratorBlock"] = BuildTypeAcceleratorBlock(
                        assemblyTypeAcceleratorMode,
                        assemblyTypeAccelerators,
                        assemblyTypeAcceleratorAssemblies),
                    ["ExportBridgeBlock"] = IndentPowerShell(
                        BuildPowerShellModuleExportBridge(
                            "$InnerModule",
                            "$LibraryName",
                            "$ModuleAssemblyPath").TrimEnd(),
                        12),
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
            var developmentLoaderIdentity = useAssemblyLoadContext
                ? CreateDevelopmentAssemblyLoadContextLoaderIdentity(moduleName)
                : null;
            var developmentBlock = BuildDevelopmentBinaryLoaderBlock(
                moduleRoot ?? string.Empty,
                moduleName,
                libraryName,
                useAssemblyLoadContext,
                developmentLoaderIdentity,
                handleRuntimes,
                assemblyTypeAcceleratorMode,
                assemblyTypeAccelerators,
                assemblyTypeAcceleratorAssemblies,
                ignoreLibrariesOnLoad,
                developmentBinaries);

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

    private static void BuildAssemblyLoadContextLoader(
        string moduleRoot,
        IReadOnlyList<string> exportAssemblyFileNames,
        AssemblyLoadContextLoaderIdentity identity,
        string targetFramework,
        Action<string>? log)
    {
        var libRoot = Path.Combine(moduleRoot, "Lib");
        if (!Directory.Exists(libRoot))
        {
            log?.Invoke("UseAssemblyLoadContext is set but no Lib directory was found; skipping ALC loader generation.");
            return;
        }

        var targetDirectories = ResolveAssemblyLoadContextTargetDirectories(libRoot, exportAssemblyFileNames);
        if (targetDirectories.Length == 0)
        {
            log?.Invoke("UseAssemblyLoadContext is set but no compatible Lib directory was found; skipping ALC loader generation.");
            return;
        }

        EnsureDotNetSdkAvailable(moduleRoot);

        targetFramework = ResolveAssemblyLoadContextTargetFrameworkForPayloads(targetFramework, targetDirectories);

        var buildRoot = Path.Combine(Path.GetTempPath(), "PowerForge", "module-load-context", identity.AssemblyName + "_" + Guid.NewGuid().ToString("N"));
        var outputRoot = Path.Combine(buildRoot, "out");

        try
        {
            Directory.CreateDirectory(buildRoot);
            Directory.CreateDirectory(outputRoot);

            var projectPath = Path.Combine(buildRoot, identity.AssemblyName + ".csproj");
            File.WriteAllText(projectPath, BuildAssemblyLoadContextProject(identity, targetFramework), Encoding.UTF8);
            File.WriteAllText(Path.Combine(buildRoot, "ModuleAssemblyLoadContext.cs"), BuildAssemblyLoadContextSource(identity), Encoding.UTF8);

            log?.Invoke($"Building module-scoped AssemblyLoadContext loader '{identity.AssemblyName}' for {targetFramework}.");
            var result = RunProcess(
                "dotnet",
                buildRoot,
                // Disable MSBuild node reuse so short-lived helper builds exit cleanly in CI and tests.
                new[] { "build", projectPath, "-c", "Release", "-o", outputRoot, "-nologo", "-v:minimal", "-nr:false" },
                AssemblyLoadContextLoaderBuildTimeout);
            if (result.ExitCode != 0)
            {
                var message = string.Join(
                    Environment.NewLine,
                    new[]
                    {
                        $"Failed to build module-scoped AssemblyLoadContext loader '{identity.AssemblyName}' (exit {result.ExitCode}).",
                        result.StdOut,
                        result.StdErr
                    }.Where(static line => !string.IsNullOrWhiteSpace(line)));
                throw new InvalidOperationException(message);
            }

            var loaderPath = Path.Combine(outputRoot, identity.AssemblyName + ".dll");
            if (!File.Exists(loaderPath))
                throw new FileNotFoundException("Module-scoped AssemblyLoadContext loader build did not produce the expected DLL.", loaderPath);

            foreach (var directory in targetDirectories)
                File.Copy(loaderPath, Path.Combine(directory, identity.AssemblyName + ".dll"), overwrite: true);
        }
        finally
        {
            try { if (Directory.Exists(buildRoot)) Directory.Delete(buildRoot, recursive: true); }
            catch { /* best effort */ }
        }
    }

    private static void EnsureDotNetSdkAvailable(string workingDirectory)
    {
        ProcessRunResult result;
        try
        {
            result = RunProcess("dotnet", workingDirectory, new[] { "--version" }, TimeSpan.FromSeconds(30));
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException("UseAssemblyLoadContext requires the .NET SDK to be installed and 'dotnet' to be available on PATH.", ex);
        }

        if (result.ExitCode != 0)
        {
            var message = string.Join(
                Environment.NewLine,
                new[]
                {
                    "UseAssemblyLoadContext requires the .NET SDK to be installed and 'dotnet' to be available on PATH.",
                    result.StdOut,
                    result.StdErr
                }.Where(static line => !string.IsNullOrWhiteSpace(line)));
            throw new InvalidOperationException(message);
        }
    }

    internal static string[] ResolveAssemblyLoadContextTargetDirectories(
        string libRoot,
        IReadOnlyList<string>? exportAssemblyFileNames = null)
    {
        var runtimeCandidates = ModuleBinaryPayloadLayout.ResolveAssemblyLoadContextTargetDirectories(libRoot);
        if (exportAssemblyFileNames is { Count: > 0 })
        {
            var allTargetDirectories = new List<string>();
            foreach (var assemblyFileName in exportAssemblyFileNames)
            {
                var qualifiedAssemblyPath = ResolveQualifiedAssemblyPath(libRoot, assemblyFileName);
                if (!string.IsNullOrWhiteSpace(qualifiedAssemblyPath))
                {
                    allTargetDirectories.Add(Path.GetDirectoryName(qualifiedAssemblyPath!)!);
                    continue;
                }

                var configuredFileName = Path.GetFileName(assemblyFileName);
                var configuredTargetDirectories = runtimeCandidates.Where(directory =>
                    Directory.Exists(directory) &&
                    ModuleBinaryFileLocator.Enumerate(directory, SearchOption.TopDirectoryOnly)
                        .Any(path => string.Equals(Path.GetFileName(path), configuredFileName, StringComparison.OrdinalIgnoreCase)))
                    .ToList();

                if (Directory.Exists(libRoot) &&
                    ModuleBinaryFileLocator.Enumerate(libRoot, SearchOption.TopDirectoryOnly)
                        .Any(path => string.Equals(Path.GetFileName(path), configuredFileName, StringComparison.OrdinalIgnoreCase)))
                {
                    configuredTargetDirectories.Add(libRoot);
                }

                var defaultDirectory = Path.Combine(libRoot, "Default");
                if (Directory.Exists(defaultDirectory) &&
                    ModuleBinaryFileLocator.Enumerate(defaultDirectory, SearchOption.TopDirectoryOnly)
                        .Any(path => string.Equals(Path.GetFileName(path), configuredFileName, StringComparison.OrdinalIgnoreCase)))
                {
                    configuredTargetDirectories.Add(defaultDirectory);
                }

                // An unqualified runtime reference can fall through preferred folders to a unique
                // arbitrary nested match when the named Core payload is incompatible with the host.
                // Emit the helper beside every physical candidate so all runtime-selectable fallbacks
                // have the same loader available.
                foreach (var candidate in ModuleBinaryFileLocator.Enumerate(libRoot, SearchOption.AllDirectories)
                             .Where(path => string.Equals(Path.GetFileName(path), configuredFileName, StringComparison.OrdinalIgnoreCase)))
                {
                    configuredTargetDirectories.Add(Path.GetDirectoryName(candidate)!);
                }

                allTargetDirectories.AddRange(configuredTargetDirectories);
            }

            return allTargetDirectories.Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        }

        if (runtimeCandidates.Length > 0)
            return runtimeCandidates;

        return ModuleBinaryFileLocator.HasAny(libRoot, SearchOption.TopDirectoryOnly)
            ? new[] { libRoot }
            : Array.Empty<string>();
    }

    private static string? ResolveQualifiedAssemblyPath(string libRoot, string assemblyReference)
    {
        if (string.IsNullOrWhiteSpace(assemblyReference) ||
            assemblyReference.IndexOf(Path.DirectorySeparatorChar) < 0 &&
            assemblyReference.IndexOf(Path.AltDirectorySeparatorChar) < 0)
        {
            return null;
        }

        if (Path.IsPathRooted(assemblyReference))
            return File.Exists(assemblyReference) ? Path.GetFullPath(assemblyReference) : null;

        var normalizedReference = assemblyReference
            .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar)
            .TrimStart(Path.DirectorySeparatorChar);
        var libPrefix = "Lib" + Path.DirectorySeparatorChar;
        if (normalizedReference.StartsWith(libPrefix, StringComparison.OrdinalIgnoreCase))
            normalizedReference = normalizedReference.Substring(libPrefix.Length);

        var normalizedLibRoot = Path.GetFullPath(libRoot)
            .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        foreach (var candidate in ModuleBinaryFileLocator.Enumerate(normalizedLibRoot, SearchOption.AllDirectories))
        {
            var relativeCandidate = Path.GetFullPath(candidate).Substring(normalizedLibRoot.Length)
                .TrimStart(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar)
                .Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
            if (string.Equals(relativeCandidate, normalizedReference, StringComparison.OrdinalIgnoreCase))
                return candidate;
        }

        return null;
    }

    internal static string ResolveAssemblyLoadContextTargetFrameworkForPayloads(
        string targetFramework,
        IReadOnlyList<string>? targetDirectories)
    {
        var candidates = new[] { targetFramework }
            .Concat((targetDirectories ?? Array.Empty<string>()).Select(ResolvePayloadAssemblyLoadContextTargetFramework))
            .Where(static framework => !string.IsNullOrWhiteSpace(framework))
            .Select(static framework => framework!)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static framework => GetNetTfmVersion(framework), Comparer<Version>.Create(static (left, right) => left.CompareTo(right)))
            .ToArray();
        return candidates.FirstOrDefault() ?? targetFramework;
    }

    private static string? ResolvePayloadAssemblyLoadContextTargetFramework(string directory)
    {
        var folderName = Path.GetFileName(directory) ?? string.Empty;
        var markerPath = Path.Combine(directory, ModuleBinaryPayloadLayout.TargetFrameworkMarkerFileName);
        try
        {
            if (File.Exists(markerPath))
            {
                var markerFramework = NormalizePayloadAssemblyLoadContextTargetFramework(File.ReadAllText(markerPath).Trim());
                if (!string.IsNullOrWhiteSpace(markerFramework))
                    return markerFramework;
            }
        }
        catch
        {
            // Fall back to the deterministic folder contract below.
        }

        foreach (var prefix in new[] { "Core-", "Standard-", "Default-" })
        {
            if (folderName.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                return NormalizePayloadAssemblyLoadContextTargetFramework(folderName.Substring(prefix.Length));
        }

        // Generic payload folder names describe selection behavior, not a target-framework floor.
        // Keep the framework already resolved from the module's declared TFMs unless the payload
        // carries an explicit marker or a framework-qualified folder name.
        return null;
    }

    private static string? NormalizePayloadAssemblyLoadContextTargetFramework(string? framework)
    {
        var normalized = framework?.Trim() ?? string.Empty;
        if (normalized.StartsWith("netcoreapp", StringComparison.OrdinalIgnoreCase) &&
            TryGetNetTfmVersion(normalized, out var coreVersion) &&
            coreVersion < new Version(3, 1))
        {
            return PowerShell70AssemblyLoadContextTargetFramework;
        }

        return normalized.Equals("netstandard2.0", StringComparison.OrdinalIgnoreCase) ||
               normalized.Equals("netstandard2.1", StringComparison.OrdinalIgnoreCase)
            ? PowerShell70AssemblyLoadContextTargetFramework
            : NormalizeAssemblyLoadContextTargetFramework(normalized);
    }

    private static AssemblyLoadContextLoaderIdentity CreateAssemblyLoadContextLoaderIdentity(string moduleName)
    {
        var safeNamespaceRoot = ToCSharpIdentifierPath(moduleName);
        var assemblyName = SanitizeAssemblyName(moduleName) + ".ModuleLoadContext";
        var ns = safeNamespaceRoot + ".ModuleLoadContext";
        return new AssemblyLoadContextLoaderIdentity(assemblyName, ns, ns + ".ModuleAssemblyLoadContext");
    }

    private static AssemblyLoadContextLoaderIdentity CreateDevelopmentAssemblyLoadContextLoaderIdentity(string moduleName)
    {
        var safeNamespaceRoot = ToCSharpIdentifierPath(moduleName);
        var assemblyName = SanitizeAssemblyName(moduleName) + ".DevelopmentModuleLoadContext";
        var ns = safeNamespaceRoot + ".DevelopmentModuleLoadContext";
        return new AssemblyLoadContextLoaderIdentity(assemblyName, ns, ns + ".ModuleAssemblyLoadContext");
    }

    internal static string[] GetAssemblyLoadContextLoaderFileNames(string moduleName)
        => new[]
        {
            CreateAssemblyLoadContextLoaderIdentity(moduleName).AssemblyName + ".dll",
            CreateDevelopmentAssemblyLoadContextLoaderIdentity(moduleName).AssemblyName + ".dll"
        };

    private static string SanitizeAssemblyName(string value)
    {
        var chars = (value ?? string.Empty).Select(ch => char.IsLetterOrDigit(ch) || ch is '.' or '_' or '-' ? ch : '_').ToArray();
        var sanitized = new string(chars).Trim('.', '-', '_');
        return string.IsNullOrWhiteSpace(sanitized) ? "Module" : sanitized;
    }

    private static string ToCSharpIdentifierPath(string value)
    {
        var parts = (value ?? string.Empty)
            .Split(new[] { '.', '-' }, StringSplitOptions.RemoveEmptyEntries)
            .Select(ToCSharpIdentifier)
            .Where(static part => !string.IsNullOrWhiteSpace(part))
            .ToArray();

        return parts.Length == 0 ? "Module" : string.Join(".", parts);
    }

    private static string ToCSharpIdentifier(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return string.Empty;

        var sb = new StringBuilder(value.Length + 1);
        for (var i = 0; i < value.Length; i++)
        {
            var ch = value[i];
            var valid = i == 0
                ? char.IsLetter(ch) || ch == '_'
                : char.IsLetterOrDigit(ch) || ch == '_';
            sb.Append(valid ? ch : '_');
        }

        return sb.ToString();
    }

    internal static string ResolveAssemblyLoadContextTargetFramework(IReadOnlyList<string>? targetFrameworks)
    {
        var candidates = (targetFrameworks ?? Array.Empty<string>())
            .Select(static framework => NormalizeAssemblyLoadContextTargetFramework(framework))
            .Where(static framework => !string.IsNullOrWhiteSpace(framework))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static framework => GetNetTfmVersion(framework!), Comparer<Version>.Create(static (left, right) => left.CompareTo(right)))
            .ToArray();

        return candidates.FirstOrDefault() ?? DefaultAssemblyLoadContextTargetFramework;
    }

    private static string? NormalizeAssemblyLoadContextTargetFramework(string? framework)
    {
        framework ??= string.Empty;
        if (string.IsNullOrWhiteSpace(framework))
            return null;

        var normalized = framework.Trim();
        var platformIndex = normalized.IndexOf('-');
        if (platformIndex >= 0)
            normalized = normalized.Substring(0, platformIndex);

        // PowerShell 7.0 runs on .NET Core 3.1. A netstandard2.1 module payload
        // explicitly promises compatibility with that host, so compile the small
        // module-scoped loader against the matching runtime baseline as well.
        if (normalized.Equals("netstandard2.1", StringComparison.OrdinalIgnoreCase))
            return "netcoreapp3.1";

        if (!TryGetNetTfmVersion(normalized, out var version))
            return null;

        if (normalized.StartsWith("netcoreapp", StringComparison.OrdinalIgnoreCase) &&
            version < new Version(3, 0))
        {
            return null;
        }

        return normalized;
    }

    private static Version GetNetTfmVersion(string framework)
        => TryGetNetTfmVersion(framework, out var version) ? version : new Version(int.MaxValue, 0);

    private static bool TryGetNetTfmVersion(string framework, out Version version)
    {
        version = new Version(0, 0);
        if (string.IsNullOrWhiteSpace(framework))
        {
            return false;
        }

        var versionStart = framework.StartsWith("netcoreapp", StringComparison.OrdinalIgnoreCase)
            ? "netcoreapp".Length
            : framework.StartsWith("net", StringComparison.OrdinalIgnoreCase) &&
              !framework.StartsWith("netstandard", StringComparison.OrdinalIgnoreCase)
                ? "net".Length
                : -1;
        if (versionStart < 0 ||
            framework.Length <= versionStart ||
            !char.IsDigit(framework[versionStart]) ||
            !Version.TryParse(framework.Substring(versionStart), out var parsed))
        {
            return false;
        }

        version = parsed;
        return true;
    }

    private static string BuildAssemblyLoadContextProject(AssemblyLoadContextLoaderIdentity identity, string targetFramework)
        => $@"<Project Sdk=""Microsoft.NET.Sdk"">
  <PropertyGroup>
    <TargetFramework>{EscapeXml(targetFramework)}</TargetFramework>
    <Nullable>enable</Nullable>
    <LangVersion>latest</LangVersion>
    <AssemblyName>{EscapeXml(identity.AssemblyName)}</AssemblyName>
    <RootNamespace>{EscapeXml(identity.Namespace)}</RootNamespace>
    <AssemblyVersion>1.0.0.0</AssemblyVersion>
    <FileVersion>1.0.0.0</FileVersion>
    <InformationalVersion>1.0.0</InformationalVersion>
  </PropertyGroup>
</Project>
";

    internal static string BuildAssemblyLoadContextSource(AssemblyLoadContextLoaderIdentity identity)
        => $@"using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Runtime.Loader;
using System.Text.Json;

namespace {identity.Namespace};

public sealed class ModuleAssemblyLoadContext : AssemblyLoadContext
{{
    private static readonly object Sync = new();
    // Module contexts are intentionally non-collectible. A process restart is required to load a replaced DLL at the same path.
    private static readonly Dictionary<string, ModuleAssemblyLoadContext> Contexts = new(StringComparer.OrdinalIgnoreCase);

    private readonly string[] _assemblyDirectories;
    private readonly AssemblyDependencyResolver[] _resolvers;
    private readonly DependencyManifestResolver[] _manifestResolvers;
    private readonly Dictionary<string, Assembly> _moduleAssemblies = new(StringComparer.OrdinalIgnoreCase);

    private ModuleAssemblyLoadContext(string[] moduleAssemblyPaths, string contextName)
        : base(contextName, isCollectible: false)
    {{
        _assemblyDirectories = moduleAssemblyPaths
            .Select(path => Path.GetDirectoryName(path) ?? string.Empty)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        _resolvers = moduleAssemblyPaths
            .Select(TryCreateResolver)
            .OfType<AssemblyDependencyResolver>()
            .ToArray();
        _manifestResolvers = moduleAssemblyPaths
            .Select(DependencyManifestResolver.TryCreate)
            .OfType<DependencyManifestResolver>()
            .ToArray();
    }}

    public static Assembly LoadModule(string moduleAssemblyPath, string? contextName)
        => LoadModules(new[] {{ moduleAssemblyPath }}, contextName)[0];

    public static Assembly LoadModuleFromGroup(string[] moduleAssemblyPaths, string moduleAssemblyPath, string? contextName)
    {{
        var fullPaths = NormalizeModuleAssemblyPaths(moduleAssemblyPaths);
        var requestedPath = Path.GetFullPath(moduleAssemblyPath ?? throw new ArgumentNullException(nameof(moduleAssemblyPath)));
        if (!fullPaths.Contains(requestedPath, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException(""The requested module assembly must belong to the configured module group."", nameof(moduleAssemblyPath));

        var contextKey = string.Join(""|"", fullPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase));
        lock (Sync)
        {{
            var context = GetOrCreateContext(fullPaths, contextKey, contextName);
            return context.LoadModuleAssembly(requestedPath);
        }}
    }}

    public static Assembly[] LoadModules(string[] moduleAssemblyPaths, string? contextName)
    {{
        var fullPaths = NormalizeModuleAssemblyPaths(moduleAssemblyPaths);
        var contextKey = string.Join(""|"", fullPaths.OrderBy(path => path, StringComparer.OrdinalIgnoreCase));

        // The global lock keeps context creation and all configured export assembly loads single-shot for each module group.
        lock (Sync)
        {{
            var context = GetOrCreateContext(fullPaths, contextKey, contextName);
            return fullPaths.Select(context.LoadModuleAssembly).ToArray();
        }}
    }}

    private static string[] NormalizeModuleAssemblyPaths(string[] moduleAssemblyPaths)
    {{
        if (moduleAssemblyPaths is null || moduleAssemblyPaths.Length == 0)
            throw new ArgumentException(""At least one module assembly path is required."", nameof(moduleAssemblyPaths));

        var fullPaths = moduleAssemblyPaths
            .Where(path => !string.IsNullOrWhiteSpace(path))
            .Select(Path.GetFullPath)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (fullPaths.Length == 0)
            throw new ArgumentException(""At least one module assembly path is required."", nameof(moduleAssemblyPaths));
        foreach (var fullPath in fullPaths)
        {{
            if (!File.Exists(fullPath))
                throw new FileNotFoundException(""Module assembly was not found."", fullPath);
        }}

        return fullPaths;
    }}

    private static ModuleAssemblyLoadContext GetOrCreateContext(string[] fullPaths, string contextKey, string? contextName)
    {{
        if (Contexts.TryGetValue(contextKey, out var context))
            return context;

        context = new ModuleAssemblyLoadContext(fullPaths, string.IsNullOrWhiteSpace(contextName) ? Path.GetFileNameWithoutExtension(fullPaths[0]) : contextName);
        Contexts[contextKey] = context;
        return context;
    }}

    protected override Assembly? Load(AssemblyName assemblyName)
    {{
        if (assemblyName is null || string.IsNullOrWhiteSpace(assemblyName.Name))
            return null;

        var loaderAssembly = typeof(ModuleAssemblyLoadContext).Assembly.GetName();
        if (AssemblyName.ReferenceMatchesDefinition(loaderAssembly, assemblyName))
            return typeof(ModuleAssemblyLoadContext).Assembly;

        foreach (var resolver in _resolvers)
        {{
            var resolvedPath = resolver.ResolveAssemblyToPath(assemblyName);
            if (!string.IsNullOrWhiteSpace(resolvedPath) && File.Exists(resolvedPath))
            {{
                // A package can place a compile-time facade beside the module and the real
                // implementation under runtimes/<rid>/lib. Replace only that adjacent
                // facade; preserve every non-adjacent path selected by the dependency resolver.
                var runtimePath = IsAdjacentAssemblyPath(resolvedPath, assemblyName.Name)
                    ? ResolvePackagedRuntimeAssembly(assemblyName.Name)
                    : null;
                if (!string.IsNullOrWhiteSpace(runtimePath) && File.Exists(runtimePath))
                    return LoadFromAssemblyPath(runtimePath);

                return LoadFromAssemblyPath(resolvedPath);
            }}
        }}

        foreach (var manifestResolver in _manifestResolvers)
        {{
            var resolvedPath = manifestResolver.ResolveAssemblyToPath(assemblyName);
            if (!string.IsNullOrWhiteSpace(resolvedPath) && File.Exists(resolvedPath))
                return LoadFromAssemblyPath(resolvedPath);
        }}

        var packagedRuntimePath = ResolvePackagedRuntimeAssembly(assemblyName.Name);
        if (!string.IsNullOrWhiteSpace(packagedRuntimePath) && File.Exists(packagedRuntimePath))
            return LoadFromAssemblyPath(packagedRuntimePath);

        foreach (var assemblyDirectory in _assemblyDirectories)
        {{
            var assemblyPath = Path.Combine(assemblyDirectory, assemblyName.Name + "".dll"");
            if (File.Exists(assemblyPath))
                return LoadFromAssemblyPath(assemblyPath);
        }}

        return null;
    }}

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {{
        foreach (var resolver in _resolvers)
        {{
            var resolvedPath = resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            if (!string.IsNullOrWhiteSpace(resolvedPath) && File.Exists(resolvedPath))
                return LoadUnmanagedDllFromPath(resolvedPath);
        }}

        foreach (var manifestResolver in _manifestResolvers)
        {{
            var resolvedPath = manifestResolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            if (!string.IsNullOrWhiteSpace(resolvedPath) && File.Exists(resolvedPath))
                return LoadUnmanagedDllFromPath(resolvedPath);
        }}

        var packagedLibrary = LoadPackagedNativeLibrary(unmanagedDllName);
        return packagedLibrary != IntPtr.Zero
            ? packagedLibrary
            : IntPtr.Zero;
    }}

    private Assembly LoadModuleAssembly(string moduleAssemblyPath)
    {{
        // Called only while LoadModules holds Sync; keep every configured export in the same context.
        var fullPath = Path.GetFullPath(moduleAssemblyPath);
        if (_moduleAssemblies.TryGetValue(fullPath, out var loaded))
            return loaded;

        loaded = Assemblies.FirstOrDefault(assembly =>
            !assembly.IsDynamic &&
            !string.IsNullOrWhiteSpace(assembly.Location) &&
            string.Equals(Path.GetFullPath(assembly.Location), fullPath, StringComparison.OrdinalIgnoreCase))
            ?? LoadFromAssemblyPath(fullPath);
        _moduleAssemblies[fullPath] = loaded;
        return loaded;
    }}

    private static AssemblyDependencyResolver? TryCreateResolver(string assemblyPath)
    {{
        try
        {{
            return new AssemblyDependencyResolver(assemblyPath);
        }}
        catch (InvalidOperationException)
        {{
            return null;
        }}
    }}

    private sealed class DependencyManifestResolver
    {{
        private readonly string _assemblyDirectory;
        private readonly JsonElement _target;
        private readonly string[] _runtimeIdentifiers;

        private DependencyManifestResolver(string assemblyPath, JsonElement target)
        {{
            _assemblyDirectory = Path.GetDirectoryName(assemblyPath) ?? string.Empty;
            _target = target;
            _runtimeIdentifiers = BuildRuntimeIdentifiers();
        }}

        public static DependencyManifestResolver? TryCreate(string assemblyPath)
        {{
            var depsPath = Path.ChangeExtension(assemblyPath, "".deps.json"");
            if (string.IsNullOrWhiteSpace(depsPath) || !File.Exists(depsPath))
                return null;

            try
            {{
                var document = JsonDocument.Parse(File.ReadAllText(depsPath));
                if (!document.RootElement.TryGetProperty(""targets"", out var targets) || targets.ValueKind != JsonValueKind.Object)
                {{
                    document.Dispose();
                    return null;
                }}

                JsonElement target;
                if (document.RootElement.TryGetProperty(""runtimeTarget"", out var runtimeTarget) &&
                    runtimeTarget.TryGetProperty(""name"", out var targetName) &&
                    targetName.ValueKind == JsonValueKind.String &&
                    !string.IsNullOrWhiteSpace(targetName.GetString()) &&
                    targets.TryGetProperty(targetName.GetString()!, out target))
                {{
                    var clonedTarget = target.Clone();
                    document.Dispose();
                    return new DependencyManifestResolver(assemblyPath, clonedTarget);
                }}

                foreach (var candidate in targets.EnumerateObject())
                {{
                    var clonedTarget = candidate.Value.Clone();
                    document.Dispose();
                    return new DependencyManifestResolver(assemblyPath, clonedTarget);
                }}

                document.Dispose();
                return null;
            }}
            catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is JsonException || ex is InvalidOperationException)
            {{
                return null;
            }}
        }}

        public string? ResolveAssemblyToPath(AssemblyName assemblyName)
        {{
            if (assemblyName is null || string.IsNullOrWhiteSpace(assemblyName.Name))
                return null;

            var resolved = SearchRuntimeTargets(assemblyName.Name, ""runtime"");
            if (!string.IsNullOrWhiteSpace(resolved))
                return resolved;

            return SearchAssetGroup(assemblyName.Name, ""runtime"");
        }}

        public string? ResolveUnmanagedDllToPath(string unmanagedDllName)
        {{
            if (string.IsNullOrWhiteSpace(unmanagedDllName))
                return null;

            var names = new HashSet<string>(GetNativeLibraryFileNames(unmanagedDllName), StringComparer.OrdinalIgnoreCase);
            var resolved = SearchRuntimeTargets(names, ""native"");
            if (!string.IsNullOrWhiteSpace(resolved))
                return resolved;

            return SearchAssetGroup(names, ""native"");
        }}

        private string? SearchRuntimeTargets(string assemblyName, string assetType)
        {{
            foreach (var library in _target.EnumerateObject())
            {{
                if (!library.Value.TryGetProperty(""runtimeTargets"", out var runtimeTargets) ||
                    runtimeTargets.ValueKind != JsonValueKind.Object)
                    continue;

                foreach (var rid in _runtimeIdentifiers)
                {{
                    foreach (var asset in runtimeTargets.EnumerateObject())
                    {{
                        if (!asset.Value.TryGetProperty(""assetType"", out var declaredAssetType) ||
                            !string.Equals(declaredAssetType.GetString(), assetType, StringComparison.OrdinalIgnoreCase))
                            continue;

                        if (!asset.Value.TryGetProperty(""rid"", out var declaredRid) ||
                            !string.Equals(declaredRid.GetString(), rid, StringComparison.OrdinalIgnoreCase))
                            continue;

                        if (string.Equals(Path.GetFileNameWithoutExtension(asset.Name), assemblyName, StringComparison.OrdinalIgnoreCase))
                        {{
                            var resolved = ResolveAssetPath(asset.Name);
                            if (!string.IsNullOrWhiteSpace(resolved))
                                return resolved;
                        }}
                    }}
                }}
            }}

            return null;
        }}

        private string? SearchRuntimeTargets(HashSet<string> fileNames, string assetType)
        {{
            foreach (var library in _target.EnumerateObject())
            {{
                if (!library.Value.TryGetProperty(""runtimeTargets"", out var runtimeTargets) ||
                    runtimeTargets.ValueKind != JsonValueKind.Object)
                    continue;

                foreach (var rid in _runtimeIdentifiers)
                {{
                    foreach (var asset in runtimeTargets.EnumerateObject())
                    {{
                        if (!asset.Value.TryGetProperty(""assetType"", out var declaredAssetType) ||
                            !string.Equals(declaredAssetType.GetString(), assetType, StringComparison.OrdinalIgnoreCase))
                            continue;

                        if (!asset.Value.TryGetProperty(""rid"", out var declaredRid) ||
                            !string.Equals(declaredRid.GetString(), rid, StringComparison.OrdinalIgnoreCase))
                            continue;

                        if (fileNames.Contains(Path.GetFileName(asset.Name)))
                        {{
                            var resolved = ResolveAssetPath(asset.Name);
                            if (!string.IsNullOrWhiteSpace(resolved))
                                return resolved;
                        }}
                    }}
                }}
            }}

            return null;
        }}

        private string? SearchAssetGroup(string assemblyName, string groupName)
        {{
            foreach (var library in _target.EnumerateObject())
            {{
                if (!library.Value.TryGetProperty(groupName, out var assets) || assets.ValueKind != JsonValueKind.Object)
                    continue;

                foreach (var asset in assets.EnumerateObject())
                {{
                    if (string.Equals(Path.GetFileNameWithoutExtension(asset.Name), assemblyName, StringComparison.OrdinalIgnoreCase))
                    {{
                        var resolved = ResolveAssetPath(asset.Name);
                        if (!string.IsNullOrWhiteSpace(resolved))
                            return resolved;
                    }}
                }}
            }}

            return null;
        }}

        private string? SearchAssetGroup(HashSet<string> fileNames, string groupName)
        {{
            foreach (var library in _target.EnumerateObject())
            {{
                if (!library.Value.TryGetProperty(groupName, out var assets) || assets.ValueKind != JsonValueKind.Object)
                    continue;

                foreach (var asset in assets.EnumerateObject())
                {{
                    if (fileNames.Contains(Path.GetFileName(asset.Name)))
                    {{
                        var resolved = ResolveAssetPath(asset.Name);
                        if (!string.IsNullOrWhiteSpace(resolved))
                            return resolved;
                    }}
                }}
            }}

            return null;
        }}

        private string? ResolveAssetPath(string assetPath)
        {{
            if (string.IsNullOrWhiteSpace(assetPath))
                return null;

            var normalized = assetPath.Replace('/', Path.DirectorySeparatorChar);
            foreach (var candidate in new[]
            {{
                Path.Combine(_assemblyDirectory, normalized),
                Path.Combine(_assemblyDirectory, Path.GetFileName(normalized))
            }})
            {{
                if (File.Exists(candidate))
                    return candidate;
            }}

            return null;
        }}

        private static string[] BuildRuntimeIdentifiers()
        {{
            var values = new List<string>();
            foreach (var rid in GetRuntimeIdentifiers())
            {{
                if (!string.IsNullOrWhiteSpace(rid) && !values.Contains(rid, StringComparer.OrdinalIgnoreCase))
                    values.Add(rid);
            }}

            values.Add(string.Empty);
            return values.ToArray();
        }}
    }}

    private string? ResolvePackagedRuntimeAssembly(string assemblyName)
    {{
        if (string.IsNullOrWhiteSpace(assemblyName))
            return null;

        var fileName = assemblyName + "".dll"";
        foreach (var rid in GetRuntimeIdentifiers())
        {{
            foreach (var assemblyDirectory in _assemblyDirectories)
            {{
                var runtimeLibRoot = Path.Combine(assemblyDirectory, ""runtimes"", rid, ""lib"");
                if (!Directory.Exists(runtimeLibRoot))
                    continue;

                try
                {{
                    foreach (var path in Directory.EnumerateFiles(runtimeLibRoot, fileName, SearchOption.AllDirectories))
                    {{
                        if (File.Exists(path))
                            return path;
                    }}
                }}
                catch (Exception ex) when (ex is IOException || ex is UnauthorizedAccessException || ex is DirectoryNotFoundException)
                {{
                    continue;
                }}
            }}
        }}

        return null;
    }}

    private bool IsAdjacentAssemblyPath(string resolvedPath, string assemblyName)
    {{
        if (string.IsNullOrWhiteSpace(resolvedPath) || string.IsNullOrWhiteSpace(assemblyName))
            return false;

        var fullResolvedPath = Path.GetFullPath(resolvedPath);
        return _assemblyDirectories.Any(assemblyDirectory =>
            string.Equals(
                fullResolvedPath,
                Path.GetFullPath(Path.Combine(assemblyDirectory, assemblyName + "".dll"")),
                StringComparison.OrdinalIgnoreCase));
    }}

    private IntPtr LoadPackagedNativeLibrary(string unmanagedDllName)
    {{
        if (string.IsNullOrWhiteSpace(unmanagedDllName))
            return IntPtr.Zero;

        foreach (var rid in GetRuntimeIdentifiers())
        {{
            foreach (var assemblyDirectory in _assemblyDirectories)
            {{
                foreach (var fileName in GetNativeLibraryFileNames(unmanagedDllName))
                {{
                    var path = Path.Combine(assemblyDirectory, ""runtimes"", rid, ""native"", fileName);
                    if (File.Exists(path))
                    {{
                        var loaded = TryLoadPackagedNativeLibrary(path);
                        if (loaded != IntPtr.Zero)
                            return loaded;
                    }}
                }}
            }}
        }}

        foreach (var assemblyDirectory in _assemblyDirectories)
        {{
            foreach (var fileName in GetNativeLibraryFileNames(unmanagedDllName))
            {{
                var path = Path.Combine(assemblyDirectory, fileName);
                if (File.Exists(path))
                {{
                    var loaded = TryLoadPackagedNativeLibrary(path);
                    if (loaded != IntPtr.Zero)
                        return loaded;
                }}
            }}
        }}

        return IntPtr.Zero;
    }}

    private IntPtr TryLoadPackagedNativeLibrary(string path)
    {{
        try
        {{
            return LoadUnmanagedDllFromPath(path);
        }}
        catch (Exception ex) when (ex is BadImageFormatException || ex is DllNotFoundException || ex is FileLoadException)
        {{
            return IntPtr.Zero;
        }}
    }}

    private static IEnumerable<string> GetRuntimeIdentifiers()
    {{
        var runtimeIdentifier = typeof(RuntimeInformation)
            .GetProperty(""RuntimeIdentifier"", BindingFlags.Public | BindingFlags.Static)?
            .GetValue(null) as string ?? string.Empty;
        if (!string.IsNullOrWhiteSpace(runtimeIdentifier))
            yield return runtimeIdentifier;

        var arch = RuntimeInformation.ProcessArchitecture switch
        {{
            Architecture.X64 => ""x64"",
            Architecture.X86 => ""x86"",
            Architecture.Arm64 => ""arm64"",
            Architecture.Arm => ""arm"",
            _ => null
        }};
        var isMusl = runtimeIdentifier.Contains(""musl"", StringComparison.OrdinalIgnoreCase);

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {{
            if (arch is not null)
                yield return ""win-"" + arch;
            yield return ""win"";
        }}
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {{
            if (arch is not null)
                yield return ""osx-"" + arch;
            yield return ""osx"";
            yield return ""unix"";
        }}
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
        {{
            if (arch is not null)
            {{
                if (isMusl)
                {{
                    yield return ""linux-musl-"" + arch;
                    yield return ""linux-musl"";
                    yield return ""linux-"" + arch;
                }}
                else
                {{
                    yield return ""linux-"" + arch;
                    yield return ""linux-musl-"" + arch;
                    yield return ""linux-musl"";
                }}
            }}
            yield return ""linux"";
            yield return ""unix"";
        }}
    }}

    private static IEnumerable<string> GetNativeLibraryFileNames(string unmanagedDllName)
    {{
        yield return unmanagedDllName;

        if (Path.HasExtension(unmanagedDllName))
            yield break;

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {{
            yield return unmanagedDllName + "".dll"";
        }}
        else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {{
            yield return unmanagedDllName + "".dylib"";
            if (!unmanagedDllName.StartsWith(""lib"", StringComparison.Ordinal))
                yield return ""lib"" + unmanagedDllName + "".dylib"";
        }}
        else
        {{
            // Most non-Windows, non-macOS PowerShell hosts use ELF shared objects, so .so is the safest portable fallback.
            yield return unmanagedDllName + "".so"";
            if (!unmanagedDllName.StartsWith(""lib"", StringComparison.Ordinal))
                yield return ""lib"" + unmanagedDllName + "".so"";
        }}
    }}
}}
";

    private static string EscapeXml(string value)
        => System.Security.SecurityElement.Escape(value) ?? string.Empty;

    private static ProcessRunResult RunProcess(string fileName, string workingDirectory, IReadOnlyList<string> arguments, TimeSpan timeout)
        => Task.Run(() => new ProcessRunner().RunAsync(new ProcessRunRequest(fileName, workingDirectory, arguments, timeout)))
            .GetAwaiter()
            .GetResult();

    internal sealed class AssemblyLoadContextLoaderIdentity
    {
        public AssemblyLoadContextLoaderIdentity(string assemblyName, string ns, string typeName)
        {
            AssemblyName = assemblyName;
            Namespace = ns;
            TypeName = typeName;
        }

        public string AssemblyName { get; }
        public string Namespace { get; }
        public string TypeName { get; }
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

    private static string BuildDesktopAssemblyResolverBlock()
    {
        return RenderModuleBootstrapperTemplate(
            "DesktopAssemblyResolver",
            "Scripts/ModuleBootstrapper/DesktopAssemblyResolver.Template.ps1",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase));
    }

    internal static string BuildTypeAcceleratorBlock(
        AssemblyTypeAcceleratorExportMode mode,
        IReadOnlyList<string>? typeNames,
        IReadOnlyList<string>? assemblyNames)
    {
        var normalizedTypes = NormalizePowerShellStringArray(typeNames);
        var normalizedAssemblies = NormalizePowerShellStringArray(assemblyNames);
        if (mode == AssemblyTypeAcceleratorExportMode.None)
            return string.Empty;

        return RenderModuleBootstrapperTemplate(
            "AssemblyTypeAccelerators",
            "Scripts/ModuleBootstrapper/AssemblyTypeAccelerators.Template.ps1",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["Mode"] = mode.ToString(),
                ["RequestedTypes"] = BuildPowerShellArrayLiteral(normalizedTypes),
                ["RequestedAssemblies"] = BuildPowerShellArrayLiteral(normalizedAssemblies)
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
