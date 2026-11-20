using System.Management.Automation;
using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>
/// Resolves contained PowerShell source globs from conventional module loaders without executing module code.
/// </summary>
internal static class PowerShellConventionalModuleSourceDiscovery
{
    private static readonly HashSet<string> VariableWritingCommands = new(StringComparer.OrdinalIgnoreCase)
    {
        "Set-Variable", "New-Variable", "Remove-Variable", "Clear-Variable", "set", "sv", "nv", "rv", "clv"
    };

    internal static PowerShellConventionalModuleSourceDiscoveryResult Analyze(string sourcePath, bool rejectChangingFileLists = false)
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
        var enumerations = FindFileEnumerationAssignments(ast).ToArray();
        foreach (var command in ast.FindAll(
                     node => node is CommandAst candidate && IsTopLevel(candidate, ast),
                     searchNestedScriptBlocks: true)
                     .Cast<CommandAst>()
                     .Where(static command => GetUnqualifiedCommandName(command)?.Equals("Get-ChildItem", StringComparison.OrdinalIgnoreCase) == true))
        {
            if (!TryReadPathPattern(command, out var relativePattern, out var isLiteralPath, out var pathElementIndex) ||
                !Path.GetExtension(relativePattern).Equals(".ps1", StringComparison.OrdinalIgnoreCase))
                continue;
            var associations = FindConventionalLoaders(command, ast, rootPath, enumerations).ToArray();
            if (associations.Length == 0)
                continue;
            if (rejectChangingFileLists && associations.Any(static association => !association.Stable))
                throw new InvalidOperationException(
                    $"Conventional module loader at {rootPath}:{command.Extent.StartLineNumber} can change its file list before or during loading.");
            var (recurse, excludes) = ReadLoaderOptions(command, rootPath, pathElementIndex);

            var normalized = relativePattern.Replace('\\', Path.DirectorySeparatorChar).Replace('/', Path.DirectorySeparatorChar);
            if (isLiteralPath && WildcardPattern.ContainsWildcardCharacters(relativePattern))
                throw new InvalidOperationException($"Conventional module source LiteralPath '{relativePattern}' at {rootPath}:{command.Extent.StartLineNumber} cannot contain wildcard characters.");
            if (Path.IsPathRooted(normalized) || LooksLikeWindowsRootedPath(relativePattern))
                throw new InvalidOperationException($"Conventional module source pattern '{relativePattern}' at {rootPath}:{command.Extent.StartLineNumber} must remain relative to $PSScriptRoot.");
            var directoryPart = Path.GetDirectoryName(normalized) ?? string.Empty;
            var filePattern = Path.GetFileName(normalized);
            if (WildcardPattern.ContainsWildcardCharacters(directoryPart) || string.IsNullOrWhiteSpace(filePattern))
                throw new InvalidOperationException($"Conventional module source pattern '{relativePattern}' at {rootPath}:{command.Extent.StartLineNumber} may use wildcards only in its file name.");
            if (excludes.Length > 0 && !WildcardPattern.ContainsWildcardCharacters(filePattern))
                throw new InvalidOperationException($"Conventional module source -Exclude at {rootPath}:{command.Extent.StartLineNumber} requires a wildcard file pattern.");

            var searchRoot = Path.GetFullPath(Path.Combine(sourceRoot, directoryPart));
            if (!PowerShellCompilationPathSafety.PathEquals(sourceRoot, searchRoot))
                PowerShellCompilationPathSafety.EnsureContained(sourceRoot, searchRoot, $"Conventional module source pattern '{relativePattern}' escapes the module root.");
            if (recurse)
                recursiveDirectories.Add(searchRoot);
            var accepted = associations.Where(static association => association.Stable).ToArray();
            foreach (var association in accepted)
                loaders[association.Loader.StartOffset] = association.Loader;
            if (accepted.Length == 0 || !Directory.Exists(searchRoot))
                continue;
            PowerShellCompilationPathSafety.EnsureNoLinks(sourceRoot, searchRoot, $"Conventional module source pattern '{relativePattern}' traverses a symbolic link or junction.");
            discoveredDirectories.Add(searchRoot);
            var wildcard = new WildcardPattern(filePattern, WildcardOptions.IgnoreCase | WildcardOptions.Compiled);
            var excluded = excludes.Select(pattern => new WildcardPattern(pattern, WildcardOptions.IgnoreCase | WildcardOptions.Compiled)).ToArray();
            foreach (var file in EnumerateAccessibleFiles(searchRoot, recurse)
                         .Where(path => wildcard.IsMatch(Path.GetFileName(path)) &&
                                        !excluded.Any(pattern => pattern.IsMatch(Path.GetFileName(path)))))
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

    internal static bool IsFileNameExport(ScriptBlockAst root, CommandElementAst argument)
    {
        if (argument is not MemberExpressionAst
            {
                Static: false,
                Expression: VariableExpressionAst { VariablePath.IsUnqualified: true } receiver,
                Member: StringConstantExpressionAst member
            } ||
            argument is InvokeMemberExpressionAst ||
            !member.Value.Equals("BaseName", StringComparison.OrdinalIgnoreCase) ||
            string.IsNullOrEmpty(root.Extent.File))
            return false;

        var exportOffset = argument.Extent.StartOffset;
        var enumerations = FindFileEnumerationAssignments(root).ToArray();
        var loaders = new List<(ForEachStatementAst Loop, List<VariableExpressionAst> Inputs)>();
        foreach (var loader in root.FindAll(
                     node => node is ForEachStatementAst candidate && IsTopLevel(candidate, root),
                     searchNestedScriptBlocks: false).Cast<ForEachStatementAst>())
        {
            if (!GetDirectDotSourceLoaders(loader).Any())
                continue;

            var inputs = new List<VariableExpressionAst>();
            if (!TryReadFileEnumerationInputs(loader.Condition, inputs))
                return false;
            loaders.Add((loader, inputs));
        }

        var allowedUses = new HashSet<VariableExpressionAst>(loaders.SelectMany(static loader => loader.Inputs)) { receiver };
        var loadsExportedFiles = false;
        foreach (var (loader, inputs) in loaders)
        {
            foreach (var input in inputs)
            {
                var name = input.VariablePath.UserPath;
                var producer = FindEffectiveEnumeration(enumerations, name, loader.Extent.StartOffset);
                if (producer is null ||
                    HasInterveningAssignment(root, name, producer.Extent.EndOffset, loader.Extent.EndOffset) ||
                    HasUnmodeledFileListUse(root, name, producer.Extent.EndOffset, loader.Extent.EndOffset, allowedUses))
                    return false;

                if (loader.Extent.EndOffset < exportOffset &&
                    name.Equals(receiver.VariablePath.UserPath, StringComparison.OrdinalIgnoreCase) &&
                    !HasUnmodeledFileListUse(root, name, producer.Extent.EndOffset, exportOffset, allowedUses))
                    loadsExportedFiles = true;
            }
        }
        return loadsExportedFiles;
    }

    private static IEnumerable<(AssignmentStatementAst Assignment, string Name)> FindFileEnumerationAssignments(ScriptBlockAst root)
    {
        foreach (var producer in root.FindAll(static node => node is CommandAst, searchNestedScriptBlocks: false).Cast<CommandAst>())
        {
            if (!IsTopLevel(producer, root) ||
                producer.Parent is not PipelineAst { PipelineElements.Count: 1 } pipeline ||
                producer.Redirections.Count != 0 ||
                !string.Equals(GetUnqualifiedCommandName(producer), "Get-ChildItem", StringComparison.OrdinalIgnoreCase) ||
                !TryReadPathPattern(producer, out var pattern, out _, out _) ||
                !Path.GetExtension(pattern).Equals(".ps1", StringComparison.OrdinalIgnoreCase) ||
                FindAncestor<AssignmentStatementAst>(producer, root) is not
                {
                    Operator: TokenKind.Equals,
                    Left: VariableExpressionAst { VariablePath.IsUnqualified: true } declared
                } assignment ||
                !IsDirectFileEnumeration(assignment.Right, pipeline))
                continue;

            yield return (assignment, declared.VariablePath.UserPath);
        }
    }

    private static bool TryReadFileEnumerationInputs(Ast node, List<VariableExpressionAst> inputs)
    {
        switch (node)
        {
            case PipelineAst { PipelineElements.Count: 1 } pipeline:
                return TryReadFileEnumerationInputs(pipeline.PipelineElements[0], inputs);
            case CommandExpressionAst { Redirections.Count: 0 } command:
                return TryReadFileEnumerationInputs(command.Expression, inputs);
            case ParenExpressionAst parenthesized:
                return TryReadFileEnumerationInputs(parenthesized.Pipeline, inputs);
            case ArrayExpressionAst { SubExpression.Statements.Count: 1 } array:
                return TryReadFileEnumerationInputs(array.SubExpression.Statements[0], inputs);
            case BinaryExpressionAst { Operator: TokenKind.Plus } binary:
                return TryReadFileEnumerationInputs(binary.Left, inputs) && TryReadFileEnumerationInputs(binary.Right, inputs);
            case VariableExpressionAst { VariablePath.IsUnqualified: true, Splatted: false } variable:
                inputs.Add(variable);
                return true;
            default:
                return false;
        }
    }

    private static bool HasUnmodeledFileListUse(
        ScriptBlockAst root, string name, int startOffset, int endOffset, ISet<VariableExpressionAst>? allowedUses = null)
        => root.Find(node =>
                node is VariableExpressionAst variable &&
                variable.Extent.StartOffset >= startOffset && variable.Extent.StartOffset < endOffset &&
                MatchesVariable(variable, name) && (allowedUses is null || !allowedUses.Contains(variable)),
            searchNestedScriptBlocks: true) is not null;

    private static bool IsDirectFileEnumeration(StatementAst initializer, PipelineAst pipeline)
        => ReferenceEquals(initializer, pipeline) ||
           initializer is CommandExpressionAst
           {
               Redirections.Count: 0,
               Expression: ArrayExpressionAst { SubExpression.Statements.Count: 1 } array
           } && ReferenceEquals(array.SubExpression.Statements[0], pipeline);

    private static bool TryReadPathPattern(
        CommandAst command,
        out string relativePattern,
        out bool isLiteralPath,
        out int pathElementIndex)
    {
        relativePattern = string.Empty;
        isLiteralPath = false;
        pathElementIndex = -1;
        for (var index = 1; index < command.CommandElements.Count; index++)
        {
            if (command.CommandElements[index] is not CommandParameterAst parameter ||
                !parameter.ParameterName.Equals("Path", StringComparison.OrdinalIgnoreCase) &&
                !parameter.ParameterName.Equals("LiteralPath", StringComparison.OrdinalIgnoreCase))
                continue;
            var argument = parameter.Argument;
            if (argument is null && index + 1 < command.CommandElements.Count)
                argument = command.CommandElements[index + 1] as ExpressionAst;
            if (!TryReadScriptRootPattern(argument, out relativePattern))
                return false;
            isLiteralPath = parameter.ParameterName.Equals("LiteralPath", StringComparison.OrdinalIgnoreCase);
            pathElementIndex = index;
            return true;
        }

        if (command.CommandElements.Count > 1 &&
            command.CommandElements[1] is ExpressionAst positional &&
            TryReadScriptRootPattern(positional, out relativePattern))
        {
            pathElementIndex = 1;
            return true;
        }
        return false;
    }

    private static bool TryReadScriptRootPattern(ExpressionAst? argument, out string relativePattern)
        => PowerShellCompilationSourcePathPlanner.TryReadScriptRootLiteral(argument, out relativePattern);

    private static (bool Recurse, string[] Excludes) ReadLoaderOptions(CommandAst command, string sourcePath, int pathElementIndex)
    {
        var recurse = false;
        var excludes = new List<string>();
        for (var index = 1; index < command.CommandElements.Count; index++)
        {
            if (index == pathElementIndex && command.CommandElements[index] is ExpressionAst)
                continue;
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
            if (name.Equals("Exclude", StringComparison.OrdinalIgnoreCase))
            {
                var argument = parameter.Argument;
                if (argument is null && index + 1 < command.CommandElements.Count)
                    argument = command.CommandElements[++index] as ExpressionAst;
                IReadOnlyList<ExpressionAst>? values = argument is ArrayLiteralAst array
                    ? array.Elements
                    : argument is null ? null : new[] { argument };
                if (values is null || values.Count == 0 ||
                    values.Any(static value => value is not StringConstantExpressionAst text ||
                        string.IsNullOrWhiteSpace(text.Value) ||
                        text.Value.IndexOfAny(new[] { '/', '\\' }) >= 0))
                    throw UnsupportedLoaderOption(command, sourcePath, parameter);
                excludes.AddRange(values.Cast<StringConstantExpressionAst>().Select(static value => value.Value));
                continue;
            }
            throw UnsupportedLoaderOption(command, sourcePath, parameter);
        }
        return (recurse, excludes.ToArray());
    }

    private static InvalidOperationException UnsupportedLoaderOption(
        CommandAst command,
        string sourcePath,
        CommandParameterAst parameter)
        => new(
            $"Conventional module source discovery does not support loader option '{parameter.Extent.Text}' at {sourcePath}:{command.Extent.StartLineNumber}; use -Path/-LiteralPath, bare -Recurse, literal -Exclude names, optional -File, and optional -ErrorAction SilentlyContinue only.");

    private static bool IsTopLevel(Ast node, ScriptBlockAst root)
    {
        for (var parent = node.Parent; parent is not null && !ReferenceEquals(parent, root); parent = parent.Parent)
        {
            if (parent is FunctionDefinitionAst or ScriptBlockExpressionAst or IfStatementAst or WhileStatementAst or DoWhileStatementAst or DoUntilStatementAst or ForStatementAst or SwitchStatementAst or TryStatementAst or TrapStatementAst ||
                parent is ScriptBlockAst scriptBlock && !ReferenceEquals(scriptBlock, root))
                return false;
            if (parent is ForEachStatementAst loop && !IsDescendantOf(node, loop.Condition))
                return false;
        }
        return true;
    }

    private static IEnumerable<(PowerShellConventionalLoaderIdentity Loader, bool Stable)> FindConventionalLoaders(
        CommandAst command,
        ScriptBlockAst root,
        string sourcePath,
        (AssignmentStatementAst Assignment, string Name)[] enumerations)
    {
        foreach (var dotSource in GetPipelineDotSourceLoaders(command, root))
            yield return (new PowerShellConventionalLoaderIdentity(sourcePath, dotSource.Extent.StartOffset), Stable: true);

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
                var direct = IsDirectLoopEnumeration(loader, command);
                foreach (var dotSource in dotSources)
                    yield return (new PowerShellConventionalLoaderIdentity(sourcePath, dotSource.Extent.StartOffset), direct);
                continue;
            }
            if (assignedVariable is null ||
                assignment!.Extent.EndOffset > loader.Extent.StartOffset ||
                !loader.Condition.FindAll(
                    node => node is VariableExpressionAst variable &&
                            variable.VariablePath.UserPath.Equals(assignedVariable, StringComparison.OrdinalIgnoreCase) &&
                            !PowerShellAssignmentTargetPolicy.IsDirectAssignmentTarget(variable),
                    searchNestedScriptBlocks: false).Any())
                continue;

            var effective = FindEffectiveEnumeration(enumerations, assignedVariable, loader.Extent.StartOffset);
            if (effective is not null && !ReferenceEquals(effective, assignment) &&
                effective.Extent.StartOffset >= assignment.Extent.StartOffset)
                continue;

            var inputs = new List<VariableExpressionAst>();
            var stable = TryReadFileEnumerationInputs(loader.Condition, inputs) &&
                inputs.All(input => HasStableEnumeration(root, loader, input, enumerations));
            foreach (var dotSource in dotSources)
                yield return (new PowerShellConventionalLoaderIdentity(sourcePath, dotSource.Extent.StartOffset), stable);
        }
    }

    private static AssignmentStatementAst? FindEffectiveEnumeration(
        IEnumerable<(AssignmentStatementAst Assignment, string Name)> enumerations, string name, int before)
        => enumerations.LastOrDefault(enumeration =>
            enumeration.Assignment.Extent.EndOffset <= before &&
            enumeration.Name.Equals(name, StringComparison.OrdinalIgnoreCase)).Assignment;

    private static bool HasStableEnumeration(
        ScriptBlockAst root, ForEachStatementAst loader, VariableExpressionAst input,
        IEnumerable<(AssignmentStatementAst Assignment, string Name)> enumerations)
    {
        var name = input.VariablePath.UserPath;
        var producer = FindEffectiveEnumeration(enumerations, name, loader.Extent.StartOffset);
        return producer is not null &&
            !HasInterveningAssignment(root, name, producer.Extent.EndOffset, loader.Extent.EndOffset) &&
            !HasUnmodeledFileListUse(root, name, loader.Body.Extent.StartOffset, loader.Body.Extent.EndOffset);
    }

    private static bool IsDirectLoopEnumeration(ForEachStatementAst loader, CommandAst producer)
        => producer.Parent is PipelineAst { PipelineElements.Count: 1 } pipeline &&
            (ReferenceEquals(loader.Condition, pipeline) ||
             loader.Condition is PipelineAst { PipelineElements.Count: 1 } condition &&
             condition.PipelineElements[0] is CommandExpressionAst
             {
                 Redirections.Count: 0,
                 Expression: ArrayExpressionAst { SubExpression.Statements.Count: 1 } array
             } && ReferenceEquals(array.SubExpression.Statements[0], pipeline));

    private static IEnumerable<CommandAst> GetPipelineDotSourceLoaders(CommandAst producer, ScriptBlockAst root)
    {
        if (producer.Parent is not PipelineAst { PipelineElements.Count: 2 } pipeline ||
            !ReferenceEquals(pipeline.Parent, root.EndBlock) ||
            !ReferenceEquals(pipeline.PipelineElements[0], producer) ||
            !string.Equals(producer.GetCommandName(), "Get-ChildItem", StringComparison.OrdinalIgnoreCase) ||
            pipeline.PipelineElements[1] is not CommandAst consumer ||
            !string.Equals(consumer.GetCommandName(), "ForEach-Object", StringComparison.OrdinalIgnoreCase) ||
            HasUnsafePrecedingModuleAction(root, pipeline) ||
            consumer.CommandElements.Count != 3 ||
            consumer.CommandElements[1] is not CommandParameterAst parameter ||
            !parameter.ParameterName.Equals("Process", StringComparison.OrdinalIgnoreCase) ||
            consumer.CommandElements[2] is not ScriptBlockExpressionAst block ||
            block.ScriptBlock.EndBlock is not { } endBlock)
            yield break;

        var statements = endBlock.Statements;
        if (statements.Count is < 1 or > 2 ||
            statements.Count == 2 && !IsLoaderVerboseStatement(statements[0]) ||
            statements[statements.Count - 1] is not PipelineAst { PipelineElements.Count: 1 } dotPipeline ||
            dotPipeline.PipelineElements[0] is not CommandAst { InvocationOperator: TokenKind.Dot, CommandElements.Count: 1 } dotSource ||
            dotSource.CommandElements[0] is not MemberExpressionAst
            {
                Expression: VariableExpressionAst variable,
                Member: StringConstantExpressionAst member
            } ||
            !variable.VariablePath.UserPath.Equals("_", StringComparison.OrdinalIgnoreCase) &&
            !variable.VariablePath.UserPath.Equals("PSItem", StringComparison.OrdinalIgnoreCase) ||
            !member.Value.Equals("FullName", StringComparison.OrdinalIgnoreCase))
            yield break;

        yield return dotSource;
    }

    private static bool HasUnsafePrecedingModuleAction(ScriptBlockAst root, PipelineAst pipeline)
    {
        return root.FindAll(node =>
                node.Extent.StartOffset < pipeline.Extent.StartOffset &&
                IsInModuleScope(node, root) &&
                (node is FunctionDefinitionAst or ReturnStatementAst or ThrowStatementAst or ExitStatementAst or BreakStatementAst or ContinueStatementAst ||
                 node is CommandAst command && !IsHarmlessLoaderPrelude(command)),
                searchNestedScriptBlocks: true).Any();
    }

    private static bool IsInModuleScope(Ast node, ScriptBlockAst root)
    {
        for (var parent = node.Parent; parent is not null && !ReferenceEquals(parent, root); parent = parent.Parent)
        {
            if (parent is FunctionDefinitionAst or ScriptBlockExpressionAst ||
                parent is ScriptBlockAst nested && !ReferenceEquals(nested, root))
                return false;
        }
        return true;
    }

    private static bool IsHarmlessLoaderPrelude(CommandAst command)
        => command.InvocationOperator == TokenKind.Unknown &&
           string.Equals(command.GetCommandName(), "Write-Verbose", StringComparison.OrdinalIgnoreCase) &&
           command.CommandElements.Count == 2 &&
           command.CommandElements[1] is StringConstantExpressionAst;

    private static bool IsLoaderVerboseStatement(StatementAst statement)
        => statement is PipelineAst { PipelineElements.Count: 1 } pipeline &&
           pipeline.PipelineElements[0] is CommandAst command &&
           string.Equals(command.GetCommandName(), "Write-Verbose", StringComparison.OrdinalIgnoreCase) &&
           command.CommandElements.Count == 2 &&
           command.CommandElements[1] is MemberExpressionAst
           {
               Expression: VariableExpressionAst variable,
               Member: StringConstantExpressionAst member
           } &&
           (variable.VariablePath.UserPath.Equals("_", StringComparison.OrdinalIgnoreCase) ||
            variable.VariablePath.UserPath.Equals("PSItem", StringComparison.OrdinalIgnoreCase)) &&
           member.Value.Equals("FullName", StringComparison.OrdinalIgnoreCase);

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
        => root.Find(
            node => node.Extent.StartOffset >= producerOffset && node.Extent.StartOffset < loaderOffset &&
                    (node is AssignmentStatementAst assignment && WritesVariable(assignment.Left, variableName) ||
                     node is ForEachStatementAst loop && MatchesVariable(loop.Variable, variableName) ||
                     node is UnaryExpressionAst { TokenKind: TokenKind.PlusPlus or TokenKind.MinusMinus } unary &&
                        WritesVariable(unary.Child, variableName) ||
                     node is CommandAst command && CanChangeFileList(command, variableName)),
            searchNestedScriptBlocks: true) is not null;

    private static bool CanChangeFileList(CommandAst command, string variableName)
    {
        var name = GetUnqualifiedCommandName(command);
        if (name is null || !VariableWritingCommands.Contains(name))
            return false;

        var elements = command.CommandElements;
        for (var index = 1; index < elements.Count; index++)
        {
            if (elements[index] is not CommandParameterAst parameter ||
                !"Name".StartsWith(parameter.ParameterName, StringComparison.OrdinalIgnoreCase))
                continue;
            return CouldTargetVariable(parameter.Argument ?? (index + 1 < elements.Count ? elements[index + 1] : null), variableName);
        }
        return CouldTargetVariable(elements.Count > 1 ? elements[1] : null, variableName);
    }

    private static bool CouldTargetVariable(CommandElementAst? argument, string name)
        => argument is not StringConstantExpressionAst target ||
           new WildcardPattern(PowerShellAssignmentTargetPolicy.GetUnscopedVariableName(target.Value), WildcardOptions.IgnoreCase)
               .IsMatch(name);

    private static bool WritesVariable(ExpressionAst target, string name) => target switch
    {
        ArrayLiteralAst array => array.Elements.Any(element => WritesVariable(element, name)),
        IndexExpressionAst index => WritesVariable(index.Target, name),
        MemberExpressionAst member => WritesVariable(member.Expression, name),
        ConvertExpressionAst conversion => WritesVariable(conversion.Child, name),
        VariableExpressionAst variable => MatchesVariable(variable, name),
        _ => false
    };

    private static bool MatchesVariable(VariableExpressionAst variable, string name)
        => PowerShellAssignmentTargetPolicy.GetUnscopedVariableName(variable.VariablePath.UserPath).Equals(
            PowerShellAssignmentTargetPolicy.GetUnscopedVariableName(name), StringComparison.OrdinalIgnoreCase);

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

    private static string? GetUnqualifiedCommandName(CommandAst command)
    {
        var name = command.GetCommandName();
        return name is null ? null : name.Substring(name.LastIndexOf('\\') + 1);
    }

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
