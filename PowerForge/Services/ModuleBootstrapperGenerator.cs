using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PowerForge;

internal static class ModuleBootstrapperGenerator
{
    private static readonly UTF8Encoding Utf8Bom = new(encoderShouldEmitUTF8Identifier: true);
    private static readonly TimeSpan AssemblyLoadContextLoaderBuildTimeout = TimeSpan.FromMinutes(5);

    internal static void Generate(
        string moduleRoot,
        string moduleName,
        ExportSet exports,
        IReadOnlyList<string>? exportAssemblies,
        bool handleRuntimes,
        bool useAssemblyLoadContext = false,
        IReadOnlyDictionary<string, string[]>? conditionalFunctionDependencies = null,
        IReadOnlyList<string>? targetFrameworks = null,
        Action<string>? log = null)
    {
        if (string.IsNullOrWhiteSpace(moduleRoot)) throw new ArgumentException("Module root is required.", nameof(moduleRoot));
        if (string.IsNullOrWhiteSpace(moduleName)) throw new ArgumentException("Module name is required.", nameof(moduleName));

        var root = Path.GetFullPath(moduleRoot);
        if (!Directory.Exists(root)) throw new DirectoryNotFoundException($"Module root not found: {root}");

        var hasScriptFolders = HasAnyDirectory(root, "Public", "Private", "Classes", "Enums");
        var libRoot = Path.Combine(root, "Lib");
        var hasLib = Directory.Exists(libRoot) && Directory.EnumerateDirectories(libRoot).Any();

        // Avoid overwriting "single file" script modules that keep all code in the PSM1 and do not use folder layout.
        // If there is no Lib and no folder-based layout, leave the existing PSM1 intact.
        if (!hasLib && !hasScriptFolders) return;

        var exportAssemblyFileNames = ResolveExportAssemblyFileNames(moduleName, exportAssemblies);
        var primaryAssemblyName = exportAssemblyFileNames.FirstOrDefault() ?? (moduleName + ".dll");
        var primaryLibraryName = Path.GetFileNameWithoutExtension(primaryAssemblyName);
        if (string.IsNullOrWhiteSpace(primaryLibraryName)) primaryLibraryName = moduleName;

        if (hasLib && useAssemblyLoadContext)
            BuildAssemblyLoadContextLoader(root, moduleName, ResolveAssemblyLoadContextTargetFramework(targetFrameworks), log);

        if (hasLib)
        {
            var librariesPath = Path.Combine(root, $"{moduleName}.Libraries.ps1");
            var librariesContent = BuildLibrariesScript(root, moduleName, exportAssemblyFileNames);
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
            conditionalFunctionDependencies: conditionalFunctionDependencies);
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
        File.WriteAllText(path, NormalizeCrLf(content), Utf8Bom);
    }

    private static string NormalizeCrLf(string text)
    {
        if (string.IsNullOrEmpty(text)) return string.Empty;
        // Ensure deterministic CRLF output for Windows PowerShell.
        return text.Replace("\r\n", "\n").Replace("\r", "\n").Replace("\n", "\r\n");
    }

    private static string BuildLibrariesScript(string moduleRoot, string moduleName, IReadOnlyList<string> exportAssemblyFileNames)
    {
        // Generate a deterministic list of DLLs to Add-Type for each Lib/<Folder>.
        var libRoot = Path.Combine(moduleRoot, "Lib");
        var byFolder = new Dictionary<string, List<string>>(StringComparer.OrdinalIgnoreCase);
        byFolder["Core"] = EnumerateDllRelativePaths(libRoot, "Core", exportAssemblyFileNames);
        byFolder["Default"] = EnumerateDllRelativePaths(libRoot, "Default", exportAssemblyFileNames);
        byFolder["Standard"] = EnumerateDllRelativePaths(libRoot, "Standard", exportAssemblyFileNames);
        byFolder[""] = EnumerateDllRelativePaths(libRoot, null, exportAssemblyFileNames);

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

    private static List<string> EnumerateDllRelativePaths(string libRoot, string? folderName, IReadOnlyList<string> exportAssemblyFileNames)
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

        var exportFirst = new HashSet<string>(exportAssemblyFileNames ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
        foreach (var name in exportAssemblyFileNames ?? Array.Empty<string>())
        {
            if (string.IsNullOrWhiteSpace(name)) continue;
            if (!dllFiles.Contains(name, StringComparer.OrdinalIgnoreCase)) continue;
            list.Add(RelativeLibPath(folderName, name));
        }

        foreach (var name in dllFiles.OrderBy(n => n, StringComparer.OrdinalIgnoreCase))
        {
            if (exportFirst.Contains(name)) continue;
            list.Add(RelativeLibPath(folderName, name));
        }

        return list;

        static string RelativeLibPath(string? folder, string fileName)
        {
            var parts = new List<string> { "Lib" };
            if (!string.IsNullOrWhiteSpace(folder)) parts.Add(folder!);
            parts.Add(fileName);
            return string.Join("\\", parts);
        }
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
        IReadOnlyDictionary<string, string[]>? conditionalFunctionDependencies)
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
                    ["RuntimeHandlerBlock"] = handleRuntimes ? BuildRuntimeHandlerBlock() : string.Empty
                })
            : string.Empty;

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

    private static void BuildAssemblyLoadContextLoader(string moduleRoot, string moduleName, string targetFramework, Action<string>? log)
    {
        var libRoot = Path.Combine(moduleRoot, "Lib");
        if (!Directory.Exists(libRoot)) return;

        var targetDirectories = Directory.EnumerateDirectories(libRoot)
            .Where(directory => !string.Equals(Path.GetFileName(directory), "Default", StringComparison.OrdinalIgnoreCase))
            .ToArray();
        if (targetDirectories.Length == 0) return;

        var identity = CreateAssemblyLoadContextLoaderIdentity(moduleName);
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
                new[] { "build", projectPath, "-c", "Release", "-o", outputRoot, "-nologo", "-v:minimal" },
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

    private static AssemblyLoadContextLoaderIdentity CreateAssemblyLoadContextLoaderIdentity(string moduleName)
    {
        var safeNamespaceRoot = ToCSharpIdentifierPath(moduleName);
        var assemblyName = SanitizeAssemblyName(moduleName) + ".ModuleLoadContext";
        var ns = safeNamespaceRoot + ".ModuleLoadContext";
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

        return candidates.FirstOrDefault() ?? "net8.0";
    }

    private static string? NormalizeAssemblyLoadContextTargetFramework(string? framework)
    {
        if (string.IsNullOrWhiteSpace(framework))
            return null;

        var normalized = framework.Trim();
        var platformIndex = normalized.IndexOf('-', StringComparison.Ordinal);
        if (platformIndex >= 0)
            normalized = normalized[..platformIndex];

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

        if (!Version.TryParse(framework[3..], out var parsed))
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

    private static string BuildAssemblyLoadContextSource(AssemblyLoadContextLoaderIdentity identity)
        => $@"using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace {identity.Namespace};

public sealed class ModuleAssemblyLoadContext : AssemblyLoadContext
{{
    private static readonly object Sync = new();
    private static readonly Dictionary<string, ModuleAssemblyLoadContext> Contexts = new(StringComparer.OrdinalIgnoreCase);

    private readonly string _assemblyDirectory;
    private readonly string _moduleAssemblyPath;
    private readonly AssemblyDependencyResolver _resolver;
    private Assembly? _moduleAssembly;

    private ModuleAssemblyLoadContext(string moduleAssemblyPath, string contextName)
        : base(contextName, isCollectible: false)
    {{
        _moduleAssemblyPath = Path.GetFullPath(moduleAssemblyPath);
        _assemblyDirectory = Path.GetDirectoryName(_moduleAssemblyPath) ?? string.Empty;
        _resolver = new AssemblyDependencyResolver(_moduleAssemblyPath);
    }}

    public static Assembly LoadModule(string moduleAssemblyPath, string? contextName)
    {{
        if (string.IsNullOrWhiteSpace(moduleAssemblyPath))
            throw new ArgumentException(""Module assembly path is required."", nameof(moduleAssemblyPath));

        var fullPath = Path.GetFullPath(moduleAssemblyPath);
        if (!File.Exists(fullPath))
            throw new FileNotFoundException(""Module assembly was not found."", fullPath);

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

        var resolvedPath = _resolver.ResolveAssemblyToPath(assemblyName);
        if (!string.IsNullOrWhiteSpace(resolvedPath) && File.Exists(resolvedPath))
            return LoadFromAssemblyPath(resolvedPath);

        var assemblyPath = Path.Combine(_assemblyDirectory, assemblyName.Name + "".dll"");
        return File.Exists(assemblyPath) ? LoadFromAssemblyPath(assemblyPath) : null;
    }}

    protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
    {{
        var resolvedPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
        return !string.IsNullOrWhiteSpace(resolvedPath) && File.Exists(resolvedPath)
            ? LoadUnmanagedDllFromPath(resolvedPath)
            : IntPtr.Zero;
    }}

    private Assembly LoadMainModule()
    {{
        _moduleAssembly ??= LoadFromAssemblyPath(_moduleAssemblyPath);
        return _moduleAssembly;
    }}
}}
";

    private static string EscapeXml(string value)
        => System.Security.SecurityElement.Escape(value) ?? string.Empty;

    private static ProcessRunResult RunProcess(string fileName, string workingDirectory, IReadOnlyList<string> arguments, TimeSpan timeout)
        => new ProcessRunner()
            .RunAsync(new ProcessRunRequest(fileName, workingDirectory, arguments, timeout))
            .GetAwaiter()
            .GetResult();

    private sealed class AssemblyLoadContextLoaderIdentity
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

    private static string RenderModuleBootstrapperTemplate(
        string templateName,
        string embeddedPath,
        IReadOnlyDictionary<string, string> tokens)
    {
        var template = EmbeddedScripts.Load(embeddedPath);
        return ScriptTemplateRenderer.Render("ModuleBootstrapper." + templateName, template, tokens);
    }

}
