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
        var hasLib = Directory.Exists(libRoot) && Directory.EnumerateDirectories(libRoot).Any();
        var hasDevelopmentBinaryLoader = developmentBinaries?.Enabled == true;

        // Avoid overwriting "single file" script modules that keep all code in the PSM1 and do not use folder layout.
        // If there is no Lib and no folder-based layout, leave the existing PSM1 intact.
        if (!hasLib && !hasScriptFolders && !hasDevelopmentBinaryLoader && !forceBootstrapperWrite) return;

        var exportAssemblyFileNames = ResolveExportAssemblyFileNames(moduleName, exportAssemblies);
        var primaryAssemblyName = exportAssemblyFileNames.FirstOrDefault() ?? (moduleName + ".dll");
        var primaryLibraryName = Path.GetFileNameWithoutExtension(primaryAssemblyName);
        if (string.IsNullOrWhiteSpace(primaryLibraryName)) primaryLibraryName = moduleName;

        var assemblyLoadContextLoaderIdentity = useAssemblyLoadContext
            ? CreateAssemblyLoadContextLoaderIdentity(moduleName)
            : null;

        if (hasLib && useAssemblyLoadContext && assemblyLoadContextLoaderIdentity is not null)
            BuildAssemblyLoadContextLoader(root, assemblyLoadContextLoaderIdentity, ResolveAssemblyLoadContextTargetFramework(targetFrameworks), log);

        if (hasLib)
        {
            var librariesPath = Path.Combine(root, $"{moduleName}.Libraries.ps1");
            var librariesContent = BuildLibrariesScript(root, moduleName, exportAssemblyFileNames, assemblyLoadContextLoaderIdentity?.AssemblyName, ignoreLibrariesOnLoad);
            WritePowerShellFile(librariesPath, librariesContent);
        }

        var psm1Path = Path.Combine(root, $"{moduleName}.psm1");
        var psm1Content = BuildBootstrapperPsm1(
            moduleName,
            primaryLibraryName,
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
            moduleRoot: root);
        WritePowerShellFile(psm1Path, psm1Content);
    }

    private static bool HasAnyDirectory(string root, params string[] directoryNames)
        => (directoryNames ?? Array.Empty<string>())
            .Where(d => !string.IsNullOrWhiteSpace(d))
            .Any(d => Directory.Exists(Path.Combine(root, d)));

    private static string[] ResolveExportAssemblyFileNames(string moduleName, IReadOnlyList<string>? exportAssemblies)
    {
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var ordered = new List<string>();

        var specified = (exportAssemblies ?? Array.Empty<string>())
            .Where(s => !string.IsNullOrWhiteSpace(s))
            .Select(s => s.Trim().Trim('"'))
            .ToArray();

        var entries = specified.Length > 0 ? specified : new[] { moduleName + ".dll" };
        foreach (var entry in entries)
        {
            if (string.IsNullOrWhiteSpace(entry)) continue;
            var name = entry.EndsWith(".dll", StringComparison.OrdinalIgnoreCase) ? entry : entry + ".dll";
            name = Path.GetFileName(name);
            if (string.IsNullOrWhiteSpace(name)) continue;
            if (seen.Add(name))
                ordered.Add(name);
        }

        return ordered.ToArray();
    }

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
        IReadOnlyList<string>? ignoreLibrariesOnLoad)
    {
        // Generate a deterministic list of DLLs to Add-Type for each Lib/<Folder>.
        var libRoot = Path.Combine(moduleRoot, "Lib");
        var ignored = NormalizeFileNameSet(ignoreLibrariesOnLoad);
        var byFolder = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        byFolder["Core"] = EnumerateDllRelativePaths(libRoot, "Core", exportAssemblyFileNames, assemblyLoadContextLoaderAssemblyName, ignored);
        byFolder["Default"] = EnumerateDllRelativePaths(libRoot, "Default", exportAssemblyFileNames, assemblyLoadContextLoaderAssemblyName, ignored);
        byFolder["Standard"] = EnumerateDllRelativePaths(libRoot, "Standard", exportAssemblyFileNames, assemblyLoadContextLoaderAssemblyName, ignored);
        byFolder[""] = EnumerateDllRelativePaths(libRoot, null, exportAssemblyFileNames, assemblyLoadContextLoaderAssemblyName, ignored);

        var map = BuildLibrariesByFolderMap(byFolder);
        var template = EmbeddedScripts.Load("Scripts/ModuleBootstrapper/Libraries.Template.ps1");
        var tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ModuleName"] = moduleName,
            ["LibrariesByFolderMap"] = map
        };
        return ScriptTemplateRenderer.Render("ModuleBootstrapper.Libraries", template, tokens);
    }

    private static string BuildLibrariesByFolderMap(IReadOnlyDictionary<string, List<string>> byFolder)
    {
        var sb = new StringBuilder(1024);
        var orderedKeys = new[] { "Core", "Default", "Standard", "" };
        var nonEmptyKeys = orderedKeys
            .Where(k => byFolder.TryGetValue(k, out var list) && list is { Count: > 0 })
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
            dllFiles = Directory.EnumerateFiles(dir, "*.dll", SearchOption.TopDirectoryOnly)
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

        var exportLast = new HashSet<string>(exportAssemblyFileNames ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
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
        string? moduleRoot = null)
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
                    ["LibraryName"] = EscapePsSingleQuoted(libraryName),
                    ["ModuleName"] = EscapePsSingleQuoted(moduleName),
                    ["LoaderAssemblyName"] = EscapePsSingleQuoted(loaderIdentity?.AssemblyName ?? string.Empty),
                    ["LoaderTypeName"] = loaderIdentity?.TypeName ?? string.Empty,
                    ["DesktopAssemblyResolverBlock"] = BuildDesktopAssemblyResolverBlock(),
                    ["RuntimeHandlerBlock"] = handleRuntimes ? BuildRuntimeHandlerBlock() : string.Empty,
                    ["TypeAcceleratorBlock"] = BuildTypeAcceleratorBlock(
                        assemblyTypeAcceleratorMode,
                        assemblyTypeAccelerators,
                        assemblyTypeAcceleratorAssemblies),
                    ["DesktopTypeAcceleratorBlock"] = IndentPowerShell(
                        BuildDesktopTypeAcceleratorBlock(
                            assemblyTypeAcceleratorMode,
                            assemblyTypeAccelerators,
                            assemblyTypeAcceleratorAssemblies,
                            "[IO.Path]::Combine($PSScriptRoot, 'Lib', $LibFolder)",
                            ignoreLibrariesOnLoad).TrimEnd(),
                        4)
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

            binaryLoaderBlock = string.IsNullOrWhiteSpace(binaryLoaderBlock)
                ? "$PowerForgeDevelopmentBinaryLoaded = $false\r\n" + developmentBlock.TrimEnd()
                : "$PowerForgeDevelopmentBinaryLoaded = $false\r\n" +
                  developmentBlock.TrimEnd() +
                  "\r\n\r\nif (-not $PowerForgeDevelopmentBinaryLoaded) {\r\n" +
                  IndentPowerShell(binaryLoaderBlock.TrimEnd(), 4) +
                  "\r\n}";
        }

        var scriptLoaderBlock = includeScriptLoader
            ? RenderModuleBootstrapperTemplate(
                "ScriptLoader",
                "Scripts/ModuleBootstrapper/ScriptLoader.Template.ps1",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase))
            : string.Empty;

        var tokens = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["ModuleName"] = moduleName,
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

        var targetDirectories = ResolveAssemblyLoadContextTargetDirectories(libRoot);
        if (targetDirectories.Length == 0)
        {
            log?.Invoke("UseAssemblyLoadContext is set but no compatible Lib directory was found; skipping ALC loader generation.");
            return;
        }

        EnsureDotNetSdkAvailable(moduleRoot);

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

    internal static string[] ResolveAssemblyLoadContextTargetDirectories(string libRoot)
    {
        var byName = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var directory in Directory.EnumerateDirectories(libRoot))
        {
            var name = Path.GetFileName(directory);
            if (!string.IsNullOrWhiteSpace(name))
                byName[name] = directory;
        }

        if (byName.TryGetValue("Standard", out var standard))
            return new[] { standard };

        if (byName.TryGetValue("Core", out var core))
            return new[] { core };

        if (byName.TryGetValue("Default", out var @default))
            return new[] { @default };

        return Array.Empty<string>();
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

        return TryGetNetTfmVersion(normalized, out _) ? normalized : null;
    }

    private static Version GetNetTfmVersion(string framework)
        => TryGetNetTfmVersion(framework, out var version) ? version : new Version(int.MaxValue, 0);

    private static bool TryGetNetTfmVersion(string framework, out Version version)
    {
        version = new Version(0, 0);
        if (string.IsNullOrWhiteSpace(framework) ||
            !framework.StartsWith("net", StringComparison.OrdinalIgnoreCase) ||
            framework.StartsWith("netstandard", StringComparison.OrdinalIgnoreCase) ||
            framework.Length < 5 ||
            !char.IsDigit(framework[3]))
        {
            return false;
        }

        if (!Version.TryParse(framework.Substring(3), out var parsed))
            return false;

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

    private readonly string _assemblyDirectory;
    private readonly string _moduleAssemblyPath;
    private readonly AssemblyDependencyResolver? _resolver;
    private readonly DependencyManifestResolver? _manifestResolver;
    private Assembly? _moduleAssembly;

    private ModuleAssemblyLoadContext(string moduleAssemblyPath, string contextName)
        : base(contextName, isCollectible: false)
    {{
        _moduleAssemblyPath = Path.GetFullPath(moduleAssemblyPath);
        _assemblyDirectory = Path.GetDirectoryName(_moduleAssemblyPath) ?? string.Empty;
        _resolver = TryCreateResolver(_moduleAssemblyPath);
        _manifestResolver = DependencyManifestResolver.TryCreate(_moduleAssemblyPath);
    }}

    public static Assembly LoadModule(string moduleAssemblyPath, string? contextName)
    {{
        if (string.IsNullOrWhiteSpace(moduleAssemblyPath))
            throw new ArgumentException(""Module assembly path is required."", nameof(moduleAssemblyPath));

        var fullPath = Path.GetFullPath(moduleAssemblyPath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException(""Module assembly was not found."", fullPath);

        // The global lock keeps context creation and the first main assembly load single-shot for each module path.
        lock (Sync)
        {{
            if (!Contexts.TryGetValue(fullPath, out var context))
            {{
                context = new ModuleAssemblyLoadContext(fullPath, string.IsNullOrWhiteSpace(contextName) ? Path.GetFileNameWithoutExtension(fullPath) : contextName);
                Contexts[fullPath] = context;
            }}

            return context.LoadMainModule();
        }}
    }}

    protected override Assembly? Load(AssemblyName assemblyName)
    {{
        if (assemblyName is null || string.IsNullOrWhiteSpace(assemblyName.Name))
            return null;

        var loaderAssembly = typeof(ModuleAssemblyLoadContext).Assembly.GetName();
        if (AssemblyName.ReferenceMatchesDefinition(loaderAssembly, assemblyName))
            return typeof(ModuleAssemblyLoadContext).Assembly;

        var resolvedPath = _resolver?.ResolveAssemblyToPath(assemblyName);
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

        resolvedPath = _manifestResolver?.ResolveAssemblyToPath(assemblyName);
        if (!string.IsNullOrWhiteSpace(resolvedPath) && File.Exists(resolvedPath))
            return LoadFromAssemblyPath(resolvedPath);

        var assemblyPath = Path.Combine(_assemblyDirectory, assemblyName.Name + "".dll"");
        var packagedRuntimePath = ResolvePackagedRuntimeAssembly(assemblyName.Name);
        if (!string.IsNullOrWhiteSpace(packagedRuntimePath) && File.Exists(packagedRuntimePath))
            return LoadFromAssemblyPath(packagedRuntimePath);

        return File.Exists(assemblyPath) ? LoadFromAssemblyPath(assemblyPath) : null;
    }}

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {{
        var resolvedPath = _resolver?.ResolveUnmanagedDllToPath(unmanagedDllName);
        if (!string.IsNullOrWhiteSpace(resolvedPath) && File.Exists(resolvedPath))
            return LoadUnmanagedDllFromPath(resolvedPath);

        resolvedPath = _manifestResolver?.ResolveUnmanagedDllToPath(unmanagedDllName);
        if (!string.IsNullOrWhiteSpace(resolvedPath) && File.Exists(resolvedPath))
            return LoadUnmanagedDllFromPath(resolvedPath);

        var packagedLibrary = LoadPackagedNativeLibrary(unmanagedDllName);
        return packagedLibrary != IntPtr.Zero
            ? packagedLibrary
            : IntPtr.Zero;
    }}

    private Assembly LoadMainModule()
    {{
        // Called only while LoadModule holds Sync; keep the one-time main assembly load under that lock.
        _moduleAssembly ??= LoadFromAssemblyPath(_moduleAssemblyPath);
        return _moduleAssembly;
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
            var runtimeLibRoot = Path.Combine(_assemblyDirectory, ""runtimes"", rid, ""lib"");
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

        return null;
    }}

    private bool IsAdjacentAssemblyPath(string resolvedPath, string assemblyName)
    {{
        if (string.IsNullOrWhiteSpace(resolvedPath) || string.IsNullOrWhiteSpace(assemblyName))
            return false;

        var adjacentPath = Path.Combine(_assemblyDirectory, assemblyName + "".dll"");
        return string.Equals(
            Path.GetFullPath(resolvedPath),
            Path.GetFullPath(adjacentPath),
            StringComparison.OrdinalIgnoreCase);
    }}

    private IntPtr LoadPackagedNativeLibrary(string unmanagedDllName)
    {{
        if (string.IsNullOrWhiteSpace(unmanagedDllName))
            return IntPtr.Zero;

        foreach (var rid in GetRuntimeIdentifiers())
        {{
            foreach (var fileName in GetNativeLibraryFileNames(unmanagedDllName))
            {{
                var path = Path.Combine(_assemblyDirectory, ""runtimes"", rid, ""native"", fileName);
                if (File.Exists(path))
                {{
                    var loaded = TryLoadPackagedNativeLibrary(path);
                    if (loaded != IntPtr.Zero)
                        return loaded;
                }}
            }}
        }}

        foreach (var fileName in GetNativeLibraryFileNames(unmanagedDllName))
        {{
            var path = Path.Combine(_assemblyDirectory, fileName);
            if (File.Exists(path))
            {{
                var loaded = TryLoadPackagedNativeLibrary(path);
                if (loaded != IntPtr.Zero)
                    return loaded;
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
        var runtimeIdentifier = RuntimeInformation.RuntimeIdentifier ?? string.Empty;
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
        return string.Join(
                   "\r\n",
                   new[]
                   {
                       "# Ensure native runtime libraries are discoverable on Windows",
                       "$IsWindowsPlatform = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)",
                       "# Skip probing when the current host cannot resolve a Windows-facing Lib folder (for example Desktop + Core-only payloads).",
                       "if ($IsWindowsPlatform -and $LibFolder) {",
                       "    $Arch = [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture",
                       "    # PowerShell switch matches the Architecture enum by its string representation here.",
                       "    $ArchFolder = switch ($Arch) {",
                       "        'X64'   { 'win-x64' }",
                       "        'X86'   { 'win-x86' }",
                       "        'Arm64' { 'win-arm64' }",
                       "        'Arm'   { 'win-arm' }",
                       "        Default {",
                       "            Write-Warning -Message (\"Unknown Windows architecture '{0}'. Falling back to win-x64 native runtime probing.\" -f $Arch)",
                       "            'win-x64'",
                       "        }",
                       "    }",
                       string.Empty,
                       "    $NativePath = Join-Path -Path $PSScriptRoot -ChildPath (\"Lib\\{0}\\runtimes\\{1}\\native\" -f $LibFolder, $ArchFolder)",
                       "    $PathEntries = if ([string]::IsNullOrWhiteSpace($env:PATH)) { @() } else { @($env:PATH -split [IO.Path]::PathSeparator) }",
                       "    if ((Test-Path -LiteralPath $NativePath) -and ($PathEntries -notcontains $NativePath)) {",
                       "        # Prepend the module-native runtime path so the packaged payload wins over unrelated machine-wide copies.",
                       "        if ([string]::IsNullOrWhiteSpace($env:PATH)) {",
                       "            $env:PATH = $NativePath",
                       "        } else {",
                       "            $env:PATH = \"$NativePath$([IO.Path]::PathSeparator)$env:PATH\"",
                       "        }",
                       "    }",
                       "}",
                       string.Empty
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

        return $@"        # Type accelerator registration relies on $ModuleAssembly and $LibFolder from this ALC loader scope.
$RegisterPowerForgeAssemblyTypeAccelerators = {{
    param(
        [Parameter(Mandatory = $true)][System.Reflection.Assembly] $ModuleAssembly,
        [Parameter(Mandatory = $true)][string] $LibFolder
    )

    $Mode = '{mode}'
    $RequestedTypes = {BuildPowerShellArrayLiteral(normalizedTypes)}
    $RequestedAssemblies = {BuildPowerShellArrayLiteral(normalizedAssemblies)}

    if ($null -eq $ModuleAssembly) {{
        Write-Warning -Message 'Module assembly was not available. ALC dependency type exposure is disabled.'
        return
    }}

    if ([string]::IsNullOrWhiteSpace($LibFolder)) {{
        Write-Warning -Message 'Module library folder was not available. ALC dependency type exposure is disabled.'
        return
    }}

    $PowerForgeAlcLibraryDirectory = $null
    if ([IO.Path]::IsPathRooted($LibFolder)) {{
        $PowerForgeAlcLibraryDirectory = [IO.Path]::GetFullPath($LibFolder)
    }} elseif ($LibFolder.Contains('..') -or $LibFolder.IndexOfAny([IO.Path]::GetInvalidFileNameChars()) -ge 0) {{
        Write-Warning -Message ""Module library folder '$LibFolder' must be a simple folder name or a rooted development binary directory. ALC dependency type exposure is disabled.""
        return
    }} else {{
        $PowerForgeAlcLibraryDirectory = [IO.Path]::Combine($PSScriptRoot, 'Lib', $LibFolder)
    }}

    if ($Mode -eq 'AllowList' -and $RequestedTypes.Count -eq 0) {{
        Write-Warning -Message 'AllowList type accelerator mode was configured without type names. No ALC dependency type accelerators will be registered.'
        return
    }}

    if (($Mode -eq 'Assembly' -or $Mode -eq 'Enums') -and $RequestedAssemblies.Count -eq 0) {{
        if ($RequestedTypes.Count -eq 0) {{
            Write-Warning -Message ""$Mode type accelerator mode was configured without assembly names or type names. No ALC dependency type accelerators will be registered.""
            return
        }}

        Write-Warning -Message ""$Mode type accelerator mode was configured without assembly names. Only explicitly configured type names will be registered.""
    }}

    $TypeAccelerators = [psobject].Assembly.GetType('System.Management.Automation.TypeAccelerators')
    if ($null -eq $TypeAccelerators) {{
        Write-Warning -Message 'PowerShell type accelerator APIs are not available. ALC dependency type exposure is disabled.'
        return
    }}

    $AddTypeAccelerator = $TypeAccelerators.GetMethod('Add', [type[]]@([string], [type]))
    $GetTypeAccelerators = $TypeAccelerators.GetProperty('Get', [System.Reflection.BindingFlags] 'Static,Public,NonPublic')
    if ($null -eq $AddTypeAccelerator -or $null -eq $GetTypeAccelerators) {{
        Write-Warning -Message 'PowerShell type accelerator APIs are incomplete. ALC dependency type exposure is disabled.'
        return
    }}

    $ModuleAlc = [System.Runtime.Loader.AssemblyLoadContext]::GetLoadContext($ModuleAssembly)
    if ($null -eq $ModuleAlc) {{
        Write-Warning -Message 'Unable to resolve the module AssemblyLoadContext. ALC dependency type exposure is disabled.'
        return
    }}

    if ($null -eq $script:PowerForgeRegisteredAssemblyTypeAccelerators) {{
        $script:PowerForgeRegisteredAssemblyTypeAccelerators = @{{}}
    }}

    $ImportPowerForgeAlcAssembly = {{
        param([Parameter(Mandatory = $true)][string] $AssemblyName)

        foreach ($Assembly in $ModuleAlc.Assemblies) {{
            if ($Assembly.GetName().Name -eq $AssemblyName) {{
                return $Assembly
            }}
        }}

        try {{
            return $ModuleAlc.LoadFromAssemblyName([System.Reflection.AssemblyName]::new($AssemblyName))
        }} catch {{
            $AssemblyPath = [IO.Path]::Combine($PowerForgeAlcLibraryDirectory, $AssemblyName + '.dll')
            if (Test-Path -LiteralPath $AssemblyPath) {{
                try {{
                    $AssemblyNameObject = [System.Reflection.AssemblyName]::GetAssemblyName($AssemblyPath)
                    return $ModuleAlc.LoadFromAssemblyName($AssemblyNameObject)
                }} catch {{
                    Write-Warning -Message ""Could not load ALC assembly '$AssemblyName' for type accelerator exposure: $($_.Exception.Message)""
                }}
            }}
        }}

        return $null
    }}

    $FindPowerForgeAlcType = {{
        param([Parameter(Mandatory = $true)][string] $TypeName)

        foreach ($Assembly in $ModuleAlc.Assemblies) {{
            $Type = $Assembly.GetType($TypeName, $false, $false)
            if ($null -ne $Type) {{
                return $Type
            }}
        }}

        $LibDirectory = $PowerForgeAlcLibraryDirectory
        if (-not (Test-Path -LiteralPath $LibDirectory)) {{
            return $null
        }}

        foreach ($File in Get-ChildItem -LiteralPath $LibDirectory -Filter '*.dll' -File -ErrorAction SilentlyContinue) {{
            try {{
                $AssemblyName = [System.Reflection.AssemblyName]::GetAssemblyName($File.FullName)
                $Assembly = & $ImportPowerForgeAlcAssembly -AssemblyName $AssemblyName.Name
                if ($null -eq $Assembly) {{
                    continue
                }}

                $Type = $Assembly.GetType($TypeName, $false, $false)
                if ($null -ne $Type) {{
                    return $Type
                }}
            }} catch {{
                continue
            }}
        }}

        return $null
    }}

    $AddPowerForgeTypeAccelerator = {{
        param([Parameter(Mandatory = $true)][type] $Type)

        if ([string]::IsNullOrWhiteSpace($Type.FullName)) {{
            return
        }}

        $Name = $Type.FullName
        $Existing = $GetTypeAccelerators.GetValue($null)
        if ($Existing.ContainsKey($Name)) {{
            $ExistingType = $Existing[$Name]
            if ([object]::ReferenceEquals($ExistingType, $Type)) {{
                return
            }} else {{
                $ExistingAssemblyName = $ExistingType.Assembly.GetName()
                $TypeAssemblyName = $Type.Assembly.GetName()
                $ExistingLoadContext = [System.Runtime.Loader.AssemblyLoadContext]::GetLoadContext($ExistingType.Assembly)
                $TypeLoadContext = [System.Runtime.Loader.AssemblyLoadContext]::GetLoadContext($Type.Assembly)
                if ([object]::ReferenceEquals($ExistingLoadContext, $TypeLoadContext) -and [object]::Equals($ExistingAssemblyName.FullName, $TypeAssemblyName.FullName)) {{
                    Write-Verbose -Message ""Type accelerator '$Name' already exists in the same AssemblyLoadContext from the same assembly identity. Keeping the existing accelerator and skipping the duplicate type from $($TypeAssemblyName.Name).""
                }} else {{
                    Write-Warning -Message ""Type accelerator '$Name' already exists from $($ExistingAssemblyName.FullName). Keeping the existing accelerator and skipping the ALC type from $($TypeAssemblyName.FullName).""
                }}
            }}
            return
        }}

        try {{
            $AddTypeAccelerator.Invoke($null, @($Name, $Type)) | Out-Null
        }} catch {{
            Write-Warning -Message ""Type accelerator '$Name' could not be registered from $($Type.Assembly.GetName().Name): $($_.Exception.Message)""
            return
        }}

        $script:PowerForgeRegisteredAssemblyTypeAccelerators[$Name] = $Type
    }}

    if ($Mode -eq 'Assembly' -or $Mode -eq 'Enums') {{
        foreach ($AssemblyName in $RequestedAssemblies) {{
            $Assembly = & $ImportPowerForgeAlcAssembly -AssemblyName $AssemblyName
            if ($null -eq $Assembly) {{
                Write-Warning -Message ""Assembly '$AssemblyName' was not found in the module AssemblyLoadContext. No type accelerators were registered for it.""
                continue
            }}

            try {{
                $ExportedTypes = @($Assembly.GetExportedTypes())
            }} catch {{
                Write-Warning -Message ""Could not enumerate exported types from assembly '$AssemblyName' for type accelerator exposure: $($_.Exception.Message)""
                continue
            }}

            foreach ($Type in $ExportedTypes) {{
                if ($Mode -eq 'Enums' -and -not $Type.IsEnum) {{
                    continue
                }}

                & $AddPowerForgeTypeAccelerator -Type $Type
            }}
        }}
    }}

    foreach ($TypeName in $RequestedTypes) {{
        $Type = & $FindPowerForgeAlcType -TypeName $TypeName
        if ($null -eq $Type) {{
            Write-Warning -Message ""Type '$TypeName' was not found in the module AssemblyLoadContext. No type accelerator was registered.""
            continue
        }}

        & $AddPowerForgeTypeAccelerator -Type $Type
    }}

    if ($script:PowerForgeAssemblyTypeAcceleratorCleanupRegistered -ne $true) {{
        $script:PowerForgeAssemblyTypeAcceleratorCleanupRegistered = $true
        $RegisteredPowerForgeTypeAccelerators = $script:PowerForgeRegisteredAssemblyTypeAccelerators
        $PreviousPowerForgeOnRemove = $ExecutionContext.SessionState.Module.OnRemove
        $ExecutionContext.SessionState.Module.OnRemove = {{
            try {{
                $TypeAccelerators = [psobject].Assembly.GetType('System.Management.Automation.TypeAccelerators')
                if ($null -eq $TypeAccelerators -or $null -eq $RegisteredPowerForgeTypeAccelerators) {{
                    return
                }}

                $GetTypeAccelerators = $TypeAccelerators.GetProperty('Get', [System.Reflection.BindingFlags] 'Static,Public,NonPublic')
                $RemoveTypeAccelerator = $TypeAccelerators.GetMethod('Remove', [type[]]@([string]))
                if ($null -eq $GetTypeAccelerators -or $null -eq $RemoveTypeAccelerator) {{
                    return
                }}

                $Existing = $GetTypeAccelerators.GetValue($null)
                foreach ($Entry in @($RegisteredPowerForgeTypeAccelerators.GetEnumerator())) {{
                    if ($Existing.ContainsKey($Entry.Key) -and [object]::ReferenceEquals($Existing[$Entry.Key], $Entry.Value)) {{
                        $RemoveTypeAccelerator.Invoke($null, @($Entry.Key)) | Out-Null
                    }}
                }}
            }} finally {{
                if ($null -ne $PreviousPowerForgeOnRemove) {{
                    & $PreviousPowerForgeOnRemove @args
                }}
            }}
        }}.GetNewClosure()
    }}
}}

# Type accelerator exposure is PowerShell Core-only because it depends on AssemblyLoadContext.
try {{
    & $RegisterPowerForgeAssemblyTypeAccelerators -ModuleAssembly $ModuleAssembly -LibFolder $LibFolder
}} catch {{
    Write-Warning -Message ""ALC type accelerator registration failed: $($_.Exception.Message)""
}}
";
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
