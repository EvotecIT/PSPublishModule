using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace PowerForge;

internal static class ModuleBootstrapperGenerator
{
    private static readonly UTF8Encoding Utf8Bom = new(encoderShouldEmitUTF8Identifier: true);

    internal static void Generate(
        string moduleRoot,
        string moduleName,
        ExportSet exports,
        IReadOnlyList<string>? exportAssemblies,
        bool handleRuntimes)
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
            handleRuntimes: handleRuntimes);
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
        bool handleRuntimes)
    {
        var binaryLoaderBlock = includeBinaryLoader
            ? RenderModuleBootstrapperTemplate(
                "BinaryLoader",
                "Scripts/ModuleBootstrapper/BinaryLoader.Template.ps1",
                new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
                {
                    ["LibraryName"] = EscapePsSingleQuoted(libraryName),
                    ["ModuleName"] = EscapePsSingleQuoted(moduleName),
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
            ["FunctionsToExport"] = FormatPsStringList(exports.Functions),
            ["CmdletsToExport"] = FormatPsStringList(exports.Cmdlets),
            ["AliasesToExport"] = FormatPsStringList(exports.Aliases)
        };

        var template = EmbeddedScripts.Load("Scripts/ModuleBootstrapper/Bootstrapper.Template.ps1");
        return ScriptTemplateRenderer.Render("ModuleBootstrapper.Bootstrapper", template, tokens);
    }

    private static string BuildRuntimeHandlerBlock()
    {
        return string.Join(
                   "\r\n",
                   new[]
                   {
                       "# Ensure native runtime libraries are discoverable on Windows",
                       "$IsWindowsPlatform = [System.Runtime.InteropServices.RuntimeInformation]::IsOSPlatform([System.Runtime.InteropServices.OSPlatform]::Windows)",
                       "if ($IsWindowsPlatform -and $LibFolder) {",
                       "    $Arch = [System.Runtime.InteropServices.RuntimeInformation]::ProcessArchitecture",
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

    private static string FormatPsStringList(IReadOnlyList<string>? values)
    {
        var list = (values ?? Array.Empty<string>())
            .Where(v => !string.IsNullOrWhiteSpace(v))
            .Select(v => v.Trim())
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();

        if (list.Length == 0) return "@()";

        var sb = new StringBuilder();
        sb.Append("@(");
        for (var i = 0; i < list.Length; i++)
        {
            if (i > 0) sb.Append(", ");
            sb.Append('\'').Append(EscapePsSingleQuoted(list[i])).Append('\'');
        }
        sb.Append(')');
        return sb.ToString();
    }
}
