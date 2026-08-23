using System.Management.Automation;
using System.Management.Automation.Language;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace PowerForge;

/// <summary>
/// Resolves contained PowerShell source globs from conventional module loaders without executing module code.
/// </summary>
internal static class PowerShellConventionalModuleSourceDiscovery
{
    private static readonly Regex ScriptRootPath = new(
        @"^\$(?:\{)?PSScriptRoot(?:\})?(?<suffix>[\\/].+)$",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    internal static string[] Discover(string sourcePath)
    {
        var rootPath = Path.GetFullPath(sourcePath);
        var sourceRoot = Path.GetDirectoryName(rootPath) ?? Directory.GetCurrentDirectory();
        var ast = Parser.ParseFile(rootPath, out _, out var errors);
        if (errors.Length > 0)
            throw new InvalidOperationException($"Conventional module source discovery could not parse '{rootPath}'.");

        var discovered = new HashSet<string>(GetPathComparer());
        foreach (var command in ast.FindAll(
                     node => node is CommandAst candidate && IsTopLevel(candidate, ast),
                     searchNestedScriptBlocks: true)
                     .Cast<CommandAst>()
                     .Where(static command => command.GetCommandName()?.Equals("Get-ChildItem", StringComparison.OrdinalIgnoreCase) == true))
        {
            if (!TryReadPathPattern(command, out var relativePattern) ||
                !Path.GetExtension(relativePattern).Equals(".ps1", StringComparison.OrdinalIgnoreCase))
                continue;

            var normalized = relativePattern.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
            if (Path.IsPathRooted(normalized) || LooksLikeWindowsRootedPath(relativePattern))
                throw new InvalidOperationException($"Conventional module source pattern '{relativePattern}' at {rootPath}:{command.Extent.StartLineNumber} must remain relative to $PSScriptRoot.");
            var directoryPart = Path.GetDirectoryName(normalized) ?? string.Empty;
            var filePattern = Path.GetFileName(normalized);
            if (WildcardPattern.ContainsWildcardCharacters(directoryPart) || string.IsNullOrWhiteSpace(filePattern))
                throw new InvalidOperationException($"Conventional module source pattern '{relativePattern}' at {rootPath}:{command.Extent.StartLineNumber} may use wildcards only in its file name.");

            var searchRoot = Path.GetFullPath(Path.Combine(sourceRoot, directoryPart));
            PowerShellCompilationPathSafety.EnsureContained(sourceRoot, searchRoot, $"Conventional module source pattern '{relativePattern}' escapes the module root.");
            if (!Directory.Exists(searchRoot))
                continue;
            PowerShellCompilationPathSafety.EnsureNoLinks(sourceRoot, searchRoot, $"Conventional module source pattern '{relativePattern}' traverses a symbolic link or junction.");
            var searchOption = command.CommandElements
                .OfType<CommandParameterAst>()
                .Any(static parameter => parameter.ParameterName.Equals("Recurse", StringComparison.OrdinalIgnoreCase))
                ? SearchOption.AllDirectories
                : SearchOption.TopDirectoryOnly;
            foreach (var file in Directory.EnumerateFiles(searchRoot, filePattern, searchOption))
            {
                var fullPath = Path.GetFullPath(file);
                PowerShellCompilationPathSafety.EnsureNoLinks(sourceRoot, fullPath, $"Conventional module source '{fullPath}' traverses a symbolic link or junction.");
                discovered.Add(fullPath);
            }
        }

        return discovered
            .OrderBy(path => FrameworkCompatibility.GetRelativePath(sourceRoot, path), StringComparer.OrdinalIgnoreCase)
            .ToArray();
    }

    private static bool TryReadPathPattern(CommandAst command, out string relativePattern)
    {
        relativePattern = string.Empty;
        for (var index = 1; index < command.CommandElements.Count - 1; index++)
        {
            if (command.CommandElements[index] is not CommandParameterAst parameter ||
                !parameter.ParameterName.Equals("Path", StringComparison.OrdinalIgnoreCase) &&
                !parameter.ParameterName.Equals("LiteralPath", StringComparison.OrdinalIgnoreCase))
                continue;
            if (command.CommandElements[index + 1] is not ExpandableStringExpressionAst expandable ||
                expandable.NestedExpressions.Count != 1 ||
                expandable.NestedExpressions[0] is not VariableExpressionAst variable ||
                !variable.VariablePath.UserPath.Equals("PSScriptRoot", StringComparison.OrdinalIgnoreCase))
                return false;
            var match = ScriptRootPath.Match(expandable.Value);
            if (!match.Success)
                return false;
            relativePattern = match.Groups["suffix"].Value.TrimStart('\\', '/');
            return true;
        }
        return false;
    }

    private static bool IsTopLevel(Ast node, ScriptBlockAst root)
    {
        for (var parent = node.Parent; parent is not null && !ReferenceEquals(parent, root); parent = parent.Parent)
        {
            if (parent is FunctionDefinitionAst or ScriptBlockExpressionAst ||
                parent is ScriptBlockAst scriptBlock && !ReferenceEquals(scriptBlock, root))
                return false;
        }
        return true;
    }

    private static bool LooksLikeWindowsRootedPath(string path)
        => path.StartsWith("\\\\", StringComparison.Ordinal) ||
           path.StartsWith("//", StringComparison.Ordinal) ||
           path.Length >= 2 && char.IsLetter(path[0]) && path[1] == ':';

    private static StringComparer GetPathComparer()
        => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}
