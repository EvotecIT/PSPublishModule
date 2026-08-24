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

    internal static PowerShellConventionalModuleSourceDiscoveryResult Analyze(string sourcePath)
    {
        var rootPath = Path.GetFullPath(sourcePath);
        var sourceRoot = Path.GetDirectoryName(rootPath) ?? Directory.GetCurrentDirectory();
        var ast = Parser.ParseFile(rootPath, out _, out var errors);
        if (errors.Length > 0)
            throw new InvalidOperationException($"Conventional module source discovery could not parse '{rootPath}'.");

        var discovered = new HashSet<string>(GetPathComparer());
        var loaders = new Dictionary<int, PowerShellConventionalLoaderIdentity>();
        foreach (var command in ast.FindAll(
                     node => node is CommandAst candidate && IsTopLevel(candidate, ast),
                     searchNestedScriptBlocks: true)
                     .Cast<CommandAst>()
                     .Where(static command => command.GetCommandName()?.Equals("Get-ChildItem", StringComparison.OrdinalIgnoreCase) == true))
        {
            var acceptedLoaders = FindConventionalLoaders(command, ast, rootPath).ToArray();
            if (acceptedLoaders.Length == 0 ||
                !TryReadPathPattern(command, out var relativePattern) ||
                !Path.GetExtension(relativePattern).Equals(".ps1", StringComparison.OrdinalIgnoreCase))
                continue;
            foreach (var loader in acceptedLoaders)
                loaders[loader.StartOffset] = loader;

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

        return new PowerShellConventionalModuleSourceDiscoveryResult(
            discovered
                .OrderBy(path => FrameworkCompatibility.GetRelativePath(sourceRoot, path), StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            loaders.Values.OrderBy(static loader => loader.StartOffset).ToArray());
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

    private static IEnumerable<PowerShellConventionalLoaderIdentity> FindConventionalLoaders(
        CommandAst command,
        ScriptBlockAst root,
        string sourcePath)
    {
        var assignment = FindAncestor<AssignmentStatementAst>(command, root);
        var assignedVariable = assignment is null ? null : FindAssignedVariable(assignment.Left);
        foreach (var loader in root.FindAll(
                     node => node is ForEachStatementAst candidate && IsTopLevel(candidate, root),
                     searchNestedScriptBlocks: true).Cast<ForEachStatementAst>())
        {
            var dotSources = GetDirectDotSourceLoaders(loader).ToArray();
            if (dotSources.Length == 0)
                continue;
            if (IsDescendantOf(command, loader.Condition))
            {
                foreach (var dotSource in dotSources)
                    yield return new PowerShellConventionalLoaderIdentity(sourcePath, dotSource.Extent.StartOffset);
                continue;
            }
            if (assignedVariable is not null &&
                assignment!.Extent.StartOffset < loader.Extent.StartOffset &&
                !HasInterveningAssignment(root, assignedVariable, assignment.Extent.StartOffset, loader.Condition.Extent.EndOffset) &&
                loader.Condition.FindAll(
                    node => node is VariableExpressionAst variable &&
                            variable.VariablePath.UserPath.Equals(assignedVariable, StringComparison.OrdinalIgnoreCase) &&
                            !PowerShellAssignmentTargetPolicy.IsDirectAssignmentTarget(variable),
                    searchNestedScriptBlocks: false).Any())
            {
                foreach (var dotSource in dotSources)
                    yield return new PowerShellConventionalLoaderIdentity(sourcePath, dotSource.Extent.StartOffset);
            }
        }
    }

    private static IEnumerable<CommandAst> GetDirectDotSourceLoaders(ForEachStatementAst loader)
    {
        var loopVariable = loader.Variable.VariablePath.UserPath;
        return loader.Body.FindAll(
                node => node is CommandAst command && command.InvocationOperator == TokenKind.Dot,
                searchNestedScriptBlocks: false)
            .Cast<CommandAst>()
            .Where(command => command.Parent is PipelineAst pipeline &&
                              ReferenceEquals(pipeline.Parent, loader.Body) &&
                              command.CommandElements.Count == 1 &&
                              command.CommandElements[0] is MemberExpressionAst
                              {
                                  Expression: VariableExpressionAst variable,
                                  Member: StringConstantExpressionAst member
                              } &&
                              variable.VariablePath.UserPath.Equals(loopVariable, StringComparison.OrdinalIgnoreCase) &&
                              member.Value.Equals("FullName", StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasInterveningAssignment(ScriptBlockAst root, string variableName, int producerOffset, int loaderOffset)
        => root.FindAll(
                node => node is AssignmentStatementAst assignment &&
                        assignment.Extent.StartOffset > producerOffset &&
                        assignment.Extent.StartOffset < loaderOffset,
                searchNestedScriptBlocks: false)
            .Cast<AssignmentStatementAst>()
            .Select(static assignment => FindAssignedVariable(assignment.Left))
            .Any(name => name?.Equals(variableName, StringComparison.OrdinalIgnoreCase) == true);

    private static string? FindAssignedVariable(ExpressionAst expression)
    {
        while (expression is ConvertExpressionAst conversion)
            expression = conversion.Child;
        return expression is VariableExpressionAst variable ? variable.VariablePath.UserPath : null;
    }

    private static TAst? FindAncestor<TAst>(Ast node, ScriptBlockAst root) where TAst : Ast
    {
        for (var parent = node.Parent; parent is not null && !ReferenceEquals(parent, root); parent = parent.Parent)
        {
            if (parent is TAst match)
                return match;
        }
        return null;
    }

    private static bool IsDescendantOf(Ast node, Ast ancestor)
    {
        for (var parent = node; parent is not null; parent = parent.Parent)
        {
            if (ReferenceEquals(parent, ancestor))
                return true;
        }
        return false;
    }

    private static bool LooksLikeWindowsRootedPath(string path)
        => path.StartsWith("\\\\", StringComparison.Ordinal) ||
           path.StartsWith("//", StringComparison.Ordinal) ||
           path.Length >= 2 && char.IsLetter(path[0]) && path[1] == ':';

    private static StringComparer GetPathComparer()
        => RuntimeInformation.IsOSPlatform(OSPlatform.Windows) ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;
}

internal sealed class PowerShellConventionalModuleSourceDiscoveryResult
{
    internal PowerShellConventionalModuleSourceDiscoveryResult(
        string[] sourcePaths,
        PowerShellConventionalLoaderIdentity[] loaders)
    {
        SourcePaths = sourcePaths;
        Loaders = loaders;
    }

    internal string[] SourcePaths { get; }

    internal PowerShellConventionalLoaderIdentity[] Loaders { get; }
}

internal sealed class PowerShellConventionalLoaderIdentity
{
    internal PowerShellConventionalLoaderIdentity(string sourcePath, int startOffset)
    {
        SourcePath = Path.GetFullPath(sourcePath);
        StartOffset = startOffset;
    }

    internal string SourcePath { get; }

    internal int StartOffset { get; }
}
