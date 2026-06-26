using System.Text;

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
        var mode = options.Mode.ToString();
        var loaderTypeName = loaderIdentity?.TypeName ?? string.Empty;
        var loaderSource = useAssemblyLoadContext && loaderIdentity is not null
            ? BuildDevelopmentAssemblyLoadContextSource(loaderIdentity)
            : string.Empty;
        var typeAcceleratorBlock = useAssemblyLoadContext && loaderIdentity is not null
            ? BuildTypeAcceleratorBlock(
                assemblyTypeAcceleratorMode,
                assemblyTypeAccelerators,
                assemblyTypeAcceleratorAssemblies)
            : string.Empty;

        var sb = new StringBuilder(8192);
        sb.AppendLine("# Source development binary loader");
        sb.AppendLine("$PowerForgeDevelopmentBinaryRoot = " + binaryRootExpression);
        sb.AppendLine("$PowerForgeDevelopmentBinaryMode = '" + EscapePsSingleQuoted(mode) + "'");
        sb.AppendLine("$PowerForgeDevelopmentBinaryEnvironmentVariable = '" + EscapePsSingleQuoted(options.EnvironmentVariable) + "'");
        sb.AppendLine("$PowerForgeDevelopmentConfigurationEnvironmentVariable = '" + EscapePsSingleQuoted(options.ConfigurationEnvironmentVariable) + "'");
        sb.AppendLine("$PowerForgeDevelopmentCoreFrameworks = " + coreFrameworks);
        sb.AppendLine("$PowerForgeDevelopmentDesktopFrameworks = " + desktopFrameworks);
        sb.AppendLine("$PowerForgeDevelopmentUseAssemblyLoadContext = " + useAlcLiteral);
        sb.AppendLine("$PowerForgeDevelopmentEnabled = $false");
        sb.AppendLine("if ($PowerForgeDevelopmentBinaryMode -eq 'Auto') {");
        sb.AppendLine("    $PowerForgeDevelopmentEnabled = $true");
        sb.AppendLine("} elseif ($PowerForgeDevelopmentBinaryMode -eq 'Environment') {");
        sb.AppendLine("    $PowerForgeDevelopmentRequestedValue = [Environment]::GetEnvironmentVariable($PowerForgeDevelopmentBinaryEnvironmentVariable)");
        sb.AppendLine("    $PowerForgeDevelopmentEnabled = [string]::Equals($PowerForgeDevelopmentRequestedValue, 'true', [StringComparison]::OrdinalIgnoreCase)");
        sb.AppendLine("}");
        sb.AppendLine();
        sb.AppendLine("if ($PowerForgeDevelopmentEnabled) {");
        sb.AppendLine("    $PowerForgeDevelopmentConfigurations = @()");
        sb.AppendLine("    $PowerForgeDevelopmentRequestedConfiguration = [Environment]::GetEnvironmentVariable($PowerForgeDevelopmentConfigurationEnvironmentVariable)");
        sb.AppendLine("    if (-not [string]::IsNullOrWhiteSpace($PowerForgeDevelopmentRequestedConfiguration)) {");
        sb.AppendLine("        $PowerForgeDevelopmentConfigurations += $PowerForgeDevelopmentRequestedConfiguration");
        sb.AppendLine("    }");
        sb.AppendLine("    $PowerForgeDevelopmentConfigurations += 'Debug'");
        sb.AppendLine("    $PowerForgeDevelopmentConfigurations += 'Release'");
        sb.AppendLine("    $PowerForgeDevelopmentConfigurations = @($PowerForgeDevelopmentConfigurations | Where-Object { -not [string]::IsNullOrWhiteSpace($_) } | Select-Object -Unique)");
        sb.AppendLine();
        sb.AppendLine("    $PowerForgeDevelopmentFrameworks = if ($PSEdition -eq 'Core') { $PowerForgeDevelopmentCoreFrameworks } else { $PowerForgeDevelopmentDesktopFrameworks }");
        sb.AppendLine("    $PowerForgeDevelopmentBinaryPath = $null");
        sb.AppendLine("    foreach ($PowerForgeDevelopmentConfiguration in $PowerForgeDevelopmentConfigurations) {");
        sb.AppendLine("        foreach ($PowerForgeDevelopmentFramework in $PowerForgeDevelopmentFrameworks) {");
        sb.AppendLine("            $PowerForgeDevelopmentCandidate = [IO.Path]::Combine($PowerForgeDevelopmentBinaryRoot, $PowerForgeDevelopmentConfiguration, $PowerForgeDevelopmentFramework, '" + EscapePsSingleQuoted(libraryName) + ".dll')");
        sb.AppendLine("            if (Test-Path -LiteralPath $PowerForgeDevelopmentCandidate) {");
        sb.AppendLine("                $PowerForgeDevelopmentBinaryPath = $PowerForgeDevelopmentCandidate");
        sb.AppendLine("                break");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine("        if ($PowerForgeDevelopmentBinaryPath) { break }");
        sb.AppendLine("    }");
        sb.AppendLine();
        sb.AppendLine("    if ($PowerForgeDevelopmentBinaryPath) {");
        sb.AppendLine("        try {");
        sb.AppendLine("            $ImportModule = Get-Command -Name Import-Module -Module Microsoft.PowerShell.Core");
        if (handleRuntimes)
        {
            sb.AppendLine(IndentPowerShell(BuildDevelopmentRuntimeHandlerBlock().TrimEnd(), 12));
        }
        sb.AppendLine("            if ($PSEdition -eq 'Core' -and $PowerForgeDevelopmentUseAssemblyLoadContext) {");
        if (useAssemblyLoadContext && loaderIdentity is not null)
        {
            sb.AppendLine("                if (-not ('" + loaderTypeName + "' -as [type])) {");
            sb.AppendLine("                    Add-Type -TypeDefinition @'");
            sb.AppendLine(loaderSource.TrimEnd());
            sb.AppendLine("'@ -Language CSharp -ErrorAction Stop");
            sb.AppendLine("                }");
            sb.AppendLine("                $PowerForgeDevelopmentModuleAssembly = [" + loaderTypeName + "]::LoadModule($PowerForgeDevelopmentBinaryPath, '" + EscapePsSingleQuoted(moduleName) + ".Development')");
            sb.AppendLine("                $PowerForgeDevelopmentInnerModule = & $ImportModule -Assembly $PowerForgeDevelopmentModuleAssembly -Force -PassThru -ErrorAction Stop");
            if (!string.IsNullOrWhiteSpace(typeAcceleratorBlock))
            {
                sb.AppendLine("                $ModuleAssembly = $PowerForgeDevelopmentModuleAssembly");
                sb.AppendLine("                $LibFolder = [IO.Path]::GetDirectoryName($PowerForgeDevelopmentBinaryPath)");
                sb.AppendLine(IndentPowerShell(typeAcceleratorBlock.TrimEnd(), 16));
            }
            sb.AppendLine("                if ($PowerForgeDevelopmentInnerModule) {");
            sb.AppendLine(IndentPowerShell(BuildPowerShellModuleExportBridge("$PowerForgeDevelopmentInnerModule", libraryName, "$PowerForgeDevelopmentBinaryPath").TrimEnd(), 20));
            sb.AppendLine("                }");
        }
        else
        {
            sb.AppendLine("                & $ImportModule $PowerForgeDevelopmentBinaryPath -ErrorAction Stop");
        }
        sb.AppendLine("            } else {");
        sb.AppendLine("                & $ImportModule $PowerForgeDevelopmentBinaryPath -ErrorAction Stop");
        sb.AppendLine("            }");
        sb.AppendLine("            $PowerForgeDevelopmentBinaryLoaded = $true");
        sb.AppendLine("        } catch {");
        sb.AppendLine("            if ($ErrorActionPreference -eq 'Stop') {");
        sb.AppendLine("                throw");
        sb.AppendLine("            } else {");
        sb.AppendLine("                Write-Warning -Message \"Importing development binary $PowerForgeDevelopmentBinaryPath failed. Falling back to packaged loader when available. Error: $($_.Exception.Message)\"");
        sb.AppendLine("            }");
        sb.AppendLine("        }");
        sb.AppendLine("    }");
        sb.AppendLine("}");

        return sb.ToString();
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
        private readonly AssemblyDependencyResolver _resolver;
        private Assembly _moduleAssembly;

        private ModuleAssemblyLoadContext(string moduleAssemblyPath, string contextName)
            : base(contextName, isCollectible: false)
        {{
            _moduleAssemblyPath = Path.GetFullPath(moduleAssemblyPath);
            _assemblyDirectory = Path.GetDirectoryName(_moduleAssemblyPath) ?? string.Empty;
            _resolver = new AssemblyDependencyResolver(_moduleAssemblyPath);
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

            var resolvedPath = _resolver.ResolveAssemblyToPath(assemblyName);
            if (!string.IsNullOrWhiteSpace(resolvedPath) && File.Exists(resolvedPath))
                return LoadFromAssemblyPath(resolvedPath);

            var assemblyPath = Path.Combine(_assemblyDirectory, assemblyName.Name + "".dll"");
            return File.Exists(assemblyPath) ? LoadFromAssemblyPath(assemblyPath) : null;
        }}

        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {{
            var resolvedPath = _resolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            if (!string.IsNullOrWhiteSpace(resolvedPath) && File.Exists(resolvedPath))
                return LoadUnmanagedDllFromPath(resolvedPath);

            return IntPtr.Zero;
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
