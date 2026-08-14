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
        IReadOnlyList<string>? ignoreLibrariesOnLoad,
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
                ["DesktopTypeAcceleratorBlock"] = IndentPowerShell(
                    BuildDesktopTypeAcceleratorBlock(
                        assemblyTypeAcceleratorMode,
                        assemblyTypeAccelerators,
                        assemblyTypeAcceleratorAssemblies,
                        "[IO.Path]::GetDirectoryName($PowerForgeDevelopmentBinaryPath)",
                        ignoreLibrariesOnLoad).TrimEnd(),
                    12),
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

        var typeAcceleratorBlock = BuildTypeAcceleratorBlock(
            assemblyTypeAcceleratorMode,
            assemblyTypeAccelerators,
            assemblyTypeAcceleratorAssemblies);
        var typeAcceleratorSetupBlock = string.IsNullOrWhiteSpace(typeAcceleratorBlock)
            ? string.Empty
            : "                $ModuleAssembly = $PowerForgeDevelopmentModuleAssembly\r\n" +
              "                $LibFolder = [IO.Path]::GetDirectoryName($PowerForgeDevelopmentBinaryPath)\r\n" +
              IndentPowerShell(typeAcceleratorBlock.TrimEnd(), 16);

        return RenderModuleBootstrapperTemplate(
            "DevelopmentAssemblyLoadContextImport",
            "Scripts/ModuleBootstrapper/DevelopmentAssemblyLoadContextImport.Template.ps1",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["LoaderTypeName"] = loaderIdentity.TypeName,
                ["LoaderSource"] = BuildDevelopmentAssemblyLoadContextSource(loaderIdentity).TrimEnd(),
                ["DevelopmentContextName"] = EscapePsSingleQuoted(moduleName + ".Development"),
                ["TypeAcceleratorSetupBlock"] = typeAcceleratorSetupBlock,
                ["ExportBridgeBlock"] = IndentPowerShell(
                    BuildPowerShellModuleExportBridge(
                        "$PowerForgeDevelopmentInnerModule",
                        libraryName,
                        "$PowerForgeDevelopmentBinaryPath").TrimEnd(),
                    20)
            });
    }

    private static string BuildPowerShellModuleExportBridge(
        string innerModuleExpression,
        string libraryName,
        string? fallbackImportPathExpression = null)
    {
        var fallbackImportBlock = string.IsNullOrWhiteSpace(fallbackImportPathExpression)
            ? string.Empty
            : "\r\n    & $ImportModule " + fallbackImportPathExpression + " -ErrorAction Stop";
        var unavailableMessage = string.IsNullOrWhiteSpace(fallbackImportPathExpression)
            ? $"AddExportedCmdlet is not available on this PowerShell version. Cmdlets from {EscapePsSingleQuoted(libraryName)} may not be re-exported to the module scope."
            : $"AddExportedCmdlet is not available on this PowerShell version. Falling back to direct Import-Module; cmdlets from {EscapePsSingleQuoted(libraryName)} will load from the default context.";

        return RenderModuleBootstrapperTemplate(
            "PowerShellModuleExportBridge",
            "Scripts/ModuleBootstrapper/PowerShellModuleExportBridge.Template.ps1",
            new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
            {
                ["InnerModuleExpression"] = innerModuleExpression,
                ["LibraryName"] = EscapePsSingleQuoted(libraryName),
                ["UnavailableMessage"] = unavailableMessage,
                ["FallbackImportBlock"] = fallbackImportBlock
            });
    }

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

        private static AssemblyDependencyResolver TryCreateResolver(string assemblyPath)
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
