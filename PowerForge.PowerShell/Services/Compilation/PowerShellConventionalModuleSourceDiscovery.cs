using System.Management.Automation;
using System.Management.Automation.Language;
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

        var discovered = new HashSet<string>(PowerShellCompilationPathSafety.PathComparer);
        var discoveredDirectories = new HashSet<string>(PowerShellCompilationPathSafety.PathComparer);
        var recursiveDirectories = new HashSet<string>(PowerShellCompilationPathSafety.PathComparer);
        var loaders = new Dictionary<int, PowerShellConventionalLoaderIdentity>();
        foreach (var command in ast.FindAll(
                     node => node is CommandAst candidate && IsTopLevel(candidate, ast),
                     searchNestedScriptBlocks: true)
                     .Cast<CommandAst>()
                     .Where(static command => command.GetCommandName()?.Equals("Get-ChildItem", StringComparison.OrdinalIgnoreCase) == true))
        {
            var acceptedLoaders = FindConventionalLoaders(command, ast, rootPath).ToArray();
            if (acceptedLoaders.Length == 0 ||
                !TryReadPathPattern(command, out var relativePattern, out var isLiteralPath) ||
                !Path.GetExtension(relativePattern).Equals(".ps1", StringComparison.OrdinalIgnoreCase))
                continue;
            var recurse = ReadLoaderOptions(command, rootPath);
            foreach (var loader in acceptedLoaders)
                loaders[loader.StartOffset] = loader;

            var normalized = relativePattern.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
            if (isLiteralPath && WildcardPattern.ContainsWildcardCharacters(relativePattern))
                throw new InvalidOperationException($"Conventional module source LiteralPath '{relativePattern}' at {rootPath}:{command.Extent.StartLineNumber} cannot contain wildcard characters.");
            if (Path.IsPathRooted(normalized) || LooksLikeWindowsRootedPath(relativePattern))
                throw new InvalidOperationException($"Conventional module source pattern '{relativePattern}' at {rootPath}:{command.Extent.StartLineNumber} must remain relative to $PSScriptRoot.");
            var directoryPart = Path.GetDirectoryName(normalized) ?? string.Empty;
            var filePattern = Path.GetFileName(normalized);
            if (WildcardPattern.ContainsWildcardCharacters(directoryPart) || string.IsNullOrWhiteSpace(filePattern))
                throw new InvalidOperationException($"Conventional module source pattern '{relativePattern}' at {rootPath}:{command.Extent.StartLineNumber} may use wildcards only in its file name.");

            var searchRoot = Path.GetFullPath(Path.Combine(sourceRoot, directoryPart));
            if (!PowerShellCompilationPathSafety.PathEquals(sourceRoot, searchRoot))
                PowerShellCompilationPathSafety.EnsureContained(sourceRoot, searchRoot, $"Conventional module source pattern '{relativePattern}' escapes the module root.");
            if (!Directory.Exists(searchRoot))
                continue;
            PowerShellCompilationPathSafety.EnsureNoLinks(sourceRoot, searchRoot, $"Conventional module source pattern '{relativePattern}' traverses a symbolic link or junction.");
            discoveredDirectories.Add(searchRoot);
            if (recurse)
                recursiveDirectories.Add(searchRoot);
            var wildcard = new WildcardPattern(filePattern, WildcardOptions.IgnoreCase | WildcardOptions.Compiled);
            foreach (var file in EnumerateAccessibleFiles(searchRoot, recurse)
                         .Where(path => wildcard.IsMatch(Path.GetFileName(path))))
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
            discoveredDirectories
                .OrderBy(path => FrameworkCompatibility.GetRelativePath(sourceRoot, path), StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            recursiveDirectories
                .OrderBy(path => FrameworkCompatibility.GetRelativePath(sourceRoot, path), StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            loaders.Values.OrderBy(static loader => loader.StartOffset).ToArray());
    }

    private static IEnumerable<string> EnumerateAccessibleFiles(string root, bool recurse)
    {
        var pending = new Stack<string>();
        pending.Push(root);
        while (pending.Count > 0)
        {
            var current = pending.Pop();
            string[] files;
            try
            {
                files = Directory.GetFiles(current, "*", SearchOption.TopDirectoryOnly);
            }
            catch (Exception exception) when (IsAccessFailure(exception))
            {
                continue;
            }
            foreach (var file in files)
                yield return file;

            if (!recurse)
                continue;
            string[] directories;
            try
            {
                directories = Directory.GetDirectories(current, "*", SearchOption.TopDirectoryOnly);
            }
            catch (Exception exception) when (IsAccessFailure(exception))
            {
                continue;
            }
            foreach (var directory in directories)
            {
                try
                {
                    if ((File.GetAttributes(directory) & FileAttributes.ReparsePoint) != 0)
                        throw new InvalidOperationException($"Conventional module source directory '{directory}' is a symbolic link or junction.");
                    pending.Push(directory);
                }
                catch (Exception exception) when (IsAccessFailure(exception))
                {
                    // Get-ChildItem access failures are non-terminating for the supported default and SilentlyContinue shapes.
                }
            }
        }
    }

    private static bool IsAccessFailure(Exception exception)
        => exception is UnauthorizedAccessException or IOException or System.Security.SecurityException;

    private static bool TryReadPathPattern(CommandAst command, out string relativePattern, out bool isLiteralPath)
    {
        relativePattern = string.Empty;
        isLiteralPath = false;
        for (var index = 1; index < command.CommandElements.Count; index++)
        {
            if (command.CommandElements[index] is not CommandParameterAst parameter ||
                !parameter.ParameterName.Equals("Path", StringComparison.OrdinalIgnoreCase) &&
                !parameter.ParameterName.Equals("LiteralPath", StringComparison.OrdinalIgnoreCase))
                continue;
            var argument = parameter.Argument;
            if (argument is null && index + 1 < command.CommandElements.Count)
                argument = command.CommandElements[index + 1] as ExpressionAst;
            if (argument is not ExpandableStringExpressionAst expandable ||
                expandable.NestedExpressions.Count != 1 ||
                expandable.NestedExpressions[0] is not VariableExpressionAst variable ||
                !variable.VariablePath.UserPath.Equals("PSScriptRoot", StringComparison.OrdinalIgnoreCase))
                return false;
            var match = ScriptRootPath.Match(expandable.Value);
            if (!match.Success)
                return false;
            relativePattern = match.Groups["suffix"].Value.TrimStart('\\', '/');
            isLiteralPath = parameter.ParameterName.Equals("LiteralPath", StringComparison.OrdinalIgnoreCase);
            return true;
        }
        return false;
    }

    private static bool ReadLoaderOptions(CommandAst command, string sourcePath)
    {
        var recurse = false;
        for (var index = 1; index < command.CommandElements.Count; index++)
        {
            if (command.CommandElements[index] is not CommandParameterAst parameter)
                throw new InvalidOperationException(
                    $"Conventional module source discovery does not support loader argument '{command.CommandElements[index].Extent.Text}' at {sourcePath}:{command.Extent.StartLineNumber}; every value must belong to an explicitly modeled option.");
            var name = parameter.ParameterName;
            if (name.Equals("Path", StringComparison.OrdinalIgnoreCase) ||
                name.Equals("LiteralPath", StringComparison.OrdinalIgnoreCase))
            {
                if (parameter.Argument is null) index++;
                continue;
            }
            if (name.Equals("Recurse", StringComparison.OrdinalIgnoreCase))
            {
                if (parameter.Argument is not null)
                    throw UnsupportedLoaderOption(command, sourcePath, parameter);
                recurse = true;
                continue;
            }
            if (name.Equals("ErrorAction", StringComparison.OrdinalIgnoreCase))
            {
                var argument = parameter.Argument;
                if (argument is null && index + 1 < command.CommandElements.Count)
                    argument = command.CommandElements[++index] as ExpressionAst;
                if (argument is StringConstantExpressionAst action &&
                    action.Value.Equals("SilentlyContinue", StringComparison.OrdinalIgnoreCase))
                    continue;
                throw UnsupportedLoaderOption(command, sourcePath, parameter);
            }
            if (name.Equals("File", StringComparison.OrdinalIgnoreCase) && parameter.Argument is null)
                continue;
            throw UnsupportedLoaderOption(command, sourcePath, parameter);
        }
        return recurse;
    }

    private static InvalidOperationException UnsupportedLoaderOption(
        CommandAst command,
        string sourcePath,
        CommandParameterAst parameter)
        => new(
            $"Conventional module source discovery does not support loader option '{parameter.Extent.Text}' at {sourcePath}:{command.Extent.StartLineNumber}; use -Path/-LiteralPath, bare -Recurse, optional -File, and optional -ErrorAction SilentlyContinue only.");

    private static bool IsTopLevel(Ast node, ScriptBlockAst root)
    {
        for (var parent = node.Parent; parent is not null && !ReferenceEquals(parent, root); parent = parent.Parent)
        {
            if (parent is FunctionDefinitionAst or ScriptBlockExpressionAst or IfStatementAst or WhileStatementAst or ForStatementAst or SwitchStatementAst or TryStatementAst or TrapStatementAst ||
                parent is ScriptBlockAst scriptBlock && !ReferenceEquals(scriptBlock, root))
                return false;
            if (parent is ForEachStatementAst loop && !IsDescendantOf(node, loop.Condition))
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
                              IsUnconditionallyAttemptedLoader(pipeline, loader.Body) &&
                              command.CommandElements.Count == 1 &&
                              command.CommandElements[0] is MemberExpressionAst
                              {
                                  Expression: VariableExpressionAst variable,
                                  Member: StringConstantExpressionAst member
                              } &&
                              variable.VariablePath.UserPath.Equals(loopVariable, StringComparison.OrdinalIgnoreCase) &&
                              member.Value.Equals("FullName", StringComparison.OrdinalIgnoreCase));
    }

    private static bool IsUnconditionallyAttemptedLoader(PipelineAst pipeline, StatementBlockAst loopBody)
    {
        if (ReferenceEquals(pipeline.Parent, loopBody))
            return loopBody.Statements.Count == 1 && ReferenceEquals(loopBody.Statements[0], pipeline);
        return pipeline.Parent is StatementBlockAst tryBody &&
               tryBody.Parent is TryStatementAst tryStatement &&
               ReferenceEquals(tryStatement.Body, tryBody) &&
               ReferenceEquals(tryStatement.Parent, loopBody) &&
               loopBody.Statements.Count == 1 &&
               ReferenceEquals(loopBody.Statements[0], tryStatement) &&
               tryBody.Statements.Count == 1 &&
               ReferenceEquals(tryBody.Statements[0], pipeline);
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

}

internal sealed class PowerShellConventionalModuleSourceDiscoveryResult
{
    internal PowerShellConventionalModuleSourceDiscoveryResult(
        string[] sourcePaths,
        string[] sourceDirectories,
        string[] recursiveSourceDirectories,
        PowerShellConventionalLoaderIdentity[] loaders)
    {
        SourcePaths = sourcePaths;
        SourceDirectories = sourceDirectories;
        RecursiveSourceDirectories = recursiveSourceDirectories;
        Loaders = loaders;
    }

    internal string[] SourcePaths { get; }

    internal string[] SourceDirectories { get; }

    internal string[] RecursiveSourceDirectories { get; }

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
