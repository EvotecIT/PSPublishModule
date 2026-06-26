namespace PowerForge;

internal static partial class ModuleBootstrapperGenerator
{
    private static string BuildDevelopmentBinaryLoaderBlock(
        string moduleRoot,
        string moduleName,
        string libraryName,
        bool useAssemblyLoadContext,
        AssemblyLoadContextLoaderIdentity? loaderIdentity,
        bool handleRuntimes,
        AssemblyTypeAcceleratorExportMode assemblyTypeAcceleratorMode,
        IReadOnlyList<string>? assemblyTypeAccelerators,
        IReadOnlyList<string>? assemblyTypeAcceleratorAssemblies,
        ModuleDevelopmentBinaryBootstrapperOptions options)
    {
        var binaryRootExpression = BuildPowerShellPathExpression(moduleRoot, options.BinaryRootPath);
        var coreFrameworks = BuildPowerShellArrayLiteral(NormalizePowerShellStringArray(options.CoreFrameworkCandidates));
        var desktopFrameworks = BuildPowerShellArrayLiteral(NormalizePowerShellStringArray(options.DesktopFrameworkCandidates));
        var useAlcLiteral = useAssemblyLoadContext ? "$true" : "$false";
        return RenderModuleBootstrapperTemplate(
            "DevelopmentBinaryLoader",
            "Scripts/ModuleBootstrapper/DevelopmentBinaryLoader.Template.ps1",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["BinaryRootExpression"] = binaryRootExpression,
                ["DevelopmentBinaryMode"] = EscapePsSingleQuoted(options.Mode.ToString()),
                ["DevelopmentBinaryEnvironmentVariable"] = EscapePsSingleQuoted(options.EnvironmentVariable),
                ["DevelopmentConfigurationEnvironmentVariable"] = EscapePsSingleQuoted(options.ConfigurationEnvironmentVariable),
                ["DevelopmentCoreFrameworks"] = coreFrameworks,
                ["DevelopmentDesktopFrameworks"] = desktopFrameworks,
                ["UseAssemblyLoadContext"] = useAlcLiteral,
                ["LibraryFileName"] = EscapePsSingleQuoted(libraryName + ".dll"),
                ["LibraryTypeName"] = EscapePsSingleQuoted(libraryName + ".Initialize"),
                ["RuntimeHandlerBlock"] = handleRuntimes
                    ? IndentPowerShell(BuildDevelopmentRuntimeHandlerBlock().TrimEnd(), 12)
                    : string.Empty,
                ["AssemblyLoadContextImportBlock"] = BuildDevelopmentAssemblyLoadContextImportBlock(
                    moduleName,
                    libraryName,
                    useAssemblyLoadContext,
                    loaderIdentity,
                    assemblyTypeAcceleratorMode,
                    assemblyTypeAccelerators,
                    assemblyTypeAcceleratorAssemblies)
            });
    }

    private static string BuildDevelopmentAssemblyLoadContextImportBlock(
        string moduleName,
        string libraryName,
        bool useAssemblyLoadContext,
        AssemblyLoadContextLoaderIdentity? loaderIdentity,
        AssemblyTypeAcceleratorExportMode assemblyTypeAcceleratorMode,
        IReadOnlyList<string>? assemblyTypeAccelerators,
        IReadOnlyList<string>? assemblyTypeAcceleratorAssemblies)
    {
        if (!useAssemblyLoadContext || loaderIdentity is null)
            return "                & $ImportModule $PowerForgeDevelopmentBinaryPath -ErrorAction Stop";

        var loaderTypeName = loaderIdentity.TypeName;
        var loaderSource = BuildDevelopmentAssemblyLoadContextSource(loaderIdentity);
        var lines = new List<string>
        {
            "                if (-not ('" + loaderTypeName + "' -as [type])) {",
            "                    Add-Type -TypeDefinition @'",
            loaderSource.TrimEnd(),
            "'@ -Language CSharp -ErrorAction Stop",
            "                }",
            "                $PowerForgeDevelopmentModuleAssembly = [" + loaderTypeName + "]::LoadModule($PowerForgeDevelopmentBinaryPath, '" + EscapePsSingleQuoted(moduleName) + ".Development')",
            "                $PowerForgeDevelopmentInnerModule = & $ImportModule -Assembly $PowerForgeDevelopmentModuleAssembly -Force -PassThru -ErrorAction Stop"
        };

        var typeAcceleratorBlock = BuildTypeAcceleratorBlock(
            assemblyTypeAcceleratorMode,
            assemblyTypeAccelerators,
            assemblyTypeAcceleratorAssemblies);
        if (!string.IsNullOrWhiteSpace(typeAcceleratorBlock))
        {
            lines.Add("                $ModuleAssembly = $PowerForgeDevelopmentModuleAssembly");
            lines.Add("                $LibFolder = [IO.Path]::GetDirectoryName($PowerForgeDevelopmentBinaryPath)");
            lines.Add(IndentPowerShell(typeAcceleratorBlock.TrimEnd(), 16));
        }

        lines.Add("                if ($PowerForgeDevelopmentInnerModule) {");
        lines.Add(IndentPowerShell(BuildPowerShellModuleExportBridge("$PowerForgeDevelopmentInnerModule", libraryName, "$PowerForgeDevelopmentBinaryPath").TrimEnd(), 20));
        lines.Add("                }");

        return string.Join("\r\n", lines);
    }

    private static string BuildPowerShellModuleExportBridge(string innerModuleExpression, string libraryName, string? fallbackImportPathExpression = null)
    {
        var fallbackImport = string.IsNullOrWhiteSpace(fallbackImportPathExpression)
            ? string.Empty
            : "\r\n    & $ImportModule " + fallbackImportPathExpression + " -ErrorAction Stop";

        var unavailableMessage = string.IsNullOrWhiteSpace(fallbackImportPathExpression)
            ? $"AddExportedCmdlet is not available on this PowerShell version. Cmdlets from {EscapePsSingleQuoted(libraryName)} may not be re-exported to the module scope."
            : $"AddExportedCmdlet is not available on this PowerShell version. Falling back to direct Import-Module; cmdlets from {EscapePsSingleQuoted(libraryName)} will load from the default context.";

        return $@"# Import-Module -Assembly loads the inner binary module into its own module object. PowerShell has no
# public API to copy those exported cmdlets back to the script-module wrapper, so this uses the same
# private PSModuleInfo hook used by community ALC loaders.
$AddExportedCmdlet = [System.Management.Automation.PSModuleInfo].GetMethod(
    'AddExportedCmdlet',
    [System.Reflection.BindingFlags]'Instance, NonPublic'
)
if ($null -ne $AddExportedCmdlet) {{
    foreach ($Cmd in {innerModuleExpression}.ExportedCmdlets.Values) {{
        $AddExportedCmdlet.Invoke($ExecutionContext.SessionState.Module, @(, $Cmd)) | Out-Null
    }}
    $AddExportedAlias = [System.Management.Automation.PSModuleInfo].GetMethod(
        'AddExportedAlias',
        [System.Reflection.BindingFlags]'Instance, NonPublic'
    )
    if ($null -ne $AddExportedAlias) {{
        foreach ($Alias in {innerModuleExpression}.ExportedAliases.Values) {{
            $AliasTarget = if ([string]::IsNullOrWhiteSpace($Alias.Definition)) {{ $Alias.ResolvedCommandName }} else {{ $Alias.Definition }}
            try {{
                Set-Alias -Name $Alias.Name -Value $AliasTarget -Scope Local -Force -ErrorAction Stop
                $ExportedAlias = $ExecutionContext.SessionState.InvokeCommand.GetCommand($Alias.Name, [System.Management.Automation.CommandTypes]::Alias)
                if ($null -ne $ExportedAlias) {{
                    $AddExportedAlias.Invoke($ExecutionContext.SessionState.Module, @(, $ExportedAlias)) | Out-Null
                }} else {{
                    Write-Warning -Message ""Alias '$($Alias.Name)' from {EscapePsSingleQuoted(libraryName)} was created but could not be resolved for export.""
                }}
            }} catch {{
                Write-Warning -Message ""Alias '$($Alias.Name)' from {EscapePsSingleQuoted(libraryName)} could not be re-exported: $($_.Exception.Message)""
            }}
        }}
    }} else {{
        Write-Warning -Message ""AddExportedAlias is not available on this PowerShell version. Aliases from {EscapePsSingleQuoted(libraryName)} will not be re-exported to the module scope.""
    }}
}} else {{
    Write-Warning -Message ""{unavailableMessage}""{fallbackImport}
}}";
    }

    private static string BuildDevelopmentRuntimeHandlerBlock()
        => string.Join(
            "\r\n",
            new[]
            {
                "# Ensure native runtime libraries are discoverable for the selected development binary.",
                "$PowerForgeDevelopmentIsWindowsPlatform = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)",
                "if ($PowerForgeDevelopmentIsWindowsPlatform) {",
                "    $PowerForgeDevelopmentArch = [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture",
                "    $PowerForgeDevelopmentArchFolder = switch ($PowerForgeDevelopmentArch) {",
                "        'X64'   { 'win-x64' }",
                "        'X86'   { 'win-x86' }",
                "        'Arm64' { 'win-arm64' }",
                "        'Arm'   { 'win-arm' }",
                "        Default {",
                "            Write-Warning -Message (\"Unknown Windows architecture '{0}'. Falling back to win-x64 native runtime probing.\" -f $PowerForgeDevelopmentArch)",
                "            'win-x64'",
                "        }",
                "    }",
                "    $PowerForgeDevelopmentLibFolder = [IO.Path]::GetDirectoryName($PowerForgeDevelopmentBinaryPath)",
                "    if ($PowerForgeDevelopmentLibFolder) {",
                "        $PowerForgeDevelopmentNativePath = Join-Path -Path $PowerForgeDevelopmentLibFolder -ChildPath (\"runtimes\\{0}\\native\" -f $PowerForgeDevelopmentArchFolder)",
                "        $PowerForgeDevelopmentPathEntries = if ([string]::IsNullOrWhiteSpace($env:PATH)) { @() } else { @($env:PATH -split [IO.Path]::PathSeparator) }",
                "        if ((Test-Path -LiteralPath $PowerForgeDevelopmentNativePath) -and ($PowerForgeDevelopmentPathEntries -notcontains $PowerForgeDevelopmentNativePath)) {",
                "            if ([string]::IsNullOrWhiteSpace($env:PATH)) {",
                "                $env:PATH = $PowerForgeDevelopmentNativePath",
                "            } else {",
                "                $env:PATH = \"$PowerForgeDevelopmentNativePath$([IO.Path]::PathSeparator)$env:PATH\"",
                "            }",
                "        }",
                "    }",
                "}",
                string.Empty
            });

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
            lines.Select(line => line.Length == 0 ? string.Empty : prefix + line));
    }

    private static string BuildDevelopmentAssemblyLoadContextSource(AssemblyLoadContextLoaderIdentity identity)
        => $@"using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace {identity.Namespace}
{{
    public sealed class ModuleAssemblyLoadContext : AssemblyLoadContext
    {{
        private static readonly object Sync = new object();
        private static readonly Dictionary<string, ModuleAssemblyLoadContext> Contexts = new Dictionary<string, ModuleAssemblyLoadContext>(StringComparer.OrdinalIgnoreCase);

        private readonly string _assemblyDirectory;
        private readonly string _moduleAssemblyPath;
        private readonly AssemblyDependencyResolver? _resolver;
        private Assembly _moduleAssembly;

        private ModuleAssemblyLoadContext(string moduleAssemblyPath, string contextName)
            : base(contextName, isCollectible: false)
        {{
            _moduleAssemblyPath = Path.GetFullPath(moduleAssemblyPath);
            _assemblyDirectory = Path.GetDirectoryName(_moduleAssemblyPath) ?? string.Empty;
            _resolver = TryCreateResolver(_moduleAssemblyPath);
        }}

        public static Assembly LoadModule(string moduleAssemblyPath, string contextName)
        {{
            if (string.IsNullOrWhiteSpace(moduleAssemblyPath))
                throw new ArgumentException(""Module assembly path is required."", nameof(moduleAssemblyPath));

            var fullPath = Path.GetFullPath(moduleAssemblyPath);
            if (!File.Exists(fullPath))
                throw new FileNotFoundException(""Module assembly was not found."", fullPath);

            lock (Sync)
            {{
                ModuleAssemblyLoadContext context;
                if (!Contexts.TryGetValue(fullPath, out context))
                {{
                    context = new ModuleAssemblyLoadContext(fullPath, string.IsNullOrWhiteSpace(contextName) ? Path.GetFileNameWithoutExtension(fullPath) : contextName);
                    Contexts[fullPath] = context;
                }}

                return context.LoadMainModule();
            }}
        }}

        protected override Assembly Load(AssemblyName assemblyName)
        {{
            if (assemblyName == null || string.IsNullOrWhiteSpace(assemblyName.Name))
                return null;

            var loaderAssembly = typeof(ModuleAssemblyLoadContext).Assembly.GetName();
            if (AssemblyName.ReferenceMatchesDefinition(loaderAssembly, assemblyName))
                return typeof(ModuleAssemblyLoadContext).Assembly;

            var resolvedPath = _resolver?.ResolveAssemblyToPath(assemblyName);
            if (!string.IsNullOrWhiteSpace(resolvedPath) && File.Exists(resolvedPath))
                return LoadFromAssemblyPath(resolvedPath);

            var assemblyPath = Path.Combine(_assemblyDirectory, assemblyName.Name + "".dll"");
            return File.Exists(assemblyPath) ? LoadFromAssemblyPath(assemblyPath) : null;
        }}

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {{
            var resolvedPath = _resolver?.ResolveUnmanagedDllToPath(unmanagedDllName);
            if (!string.IsNullOrWhiteSpace(resolvedPath) && File.Exists(resolvedPath))
                return LoadUnmanagedDllFromPath(resolvedPath);

            return IntPtr.Zero;
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

        private Assembly LoadMainModule()
        {{
            if (_moduleAssembly == null)
                _moduleAssembly = LoadFromAssemblyPath(_moduleAssemblyPath);

            return _moduleAssembly;
        }}
    }}
}}";
}
