using System.Management.Automation.Language;
using System.Text;

namespace PowerForge;

/// <summary>
/// Preserves file-backed script semantics when source is embedded and invoked through AddScript.
/// </summary>
internal static class PowerShellPackagedScriptRewriter
{
    private const string PackagedCommandPathExpression =
        "$(& { $entryPath = [System.Reflection.Assembly]::GetEntryAssembly().Location; " +
        "if ([System.IO.Path]::GetFileNameWithoutExtension([System.Environment]::ProcessPath) -eq 'dotnet' -and " +
        "-not [string]::IsNullOrWhiteSpace($entryPath)) { $entryPath } else { [System.Environment]::ProcessPath } })";

    internal static string Rewrite(
        string sourcePath,
        string? packagedCommandPathExpression = null,
        IReadOnlyCollection<string>? embeddedScriptPaths = null,
        string? dependencyCommandPathExpression = null,
        IReadOnlyCollection<string>? embeddedResourceRelativePaths = null,
        string? packagedScriptRootExpression = null)
    {
        var commandPathExpression = string.IsNullOrWhiteSpace(packagedCommandPathExpression)
            ? PackagedCommandPathExpression
            : packagedCommandPathExpression!;
        var ast = Parser.ParseFile(sourcePath, out _, out var errors);
        if (errors.Length > 0)
            throw new InvalidOperationException("Packaged script could not be parsed while preserving script semantics.");

        var fileResolvedUsing = ast.FindAll(static node => node is UsingStatementAst, searchNestedScriptBlocks: false)
            .Cast<UsingStatementAst>()
            .FirstOrDefault(static statement => statement.UsingStatementKind != UsingStatementKind.Namespace);
        if (fileResolvedUsing is not null)
        {
            throw new InvalidOperationException(
                $"Packaged executable generation does not support '{fileResolvedUsing.Extent.Text}' because using module/assembly directives are resolved before the embedded script receives file-backed path metadata.");
        }

        ValidateDotSources(ast, sourcePath, embeddedScriptPaths ?? Array.Empty<string>());

        ValidateHostInteraction(ast);

        var explicitNamedBlock = FindExplicitNamedBlock(ast);
        if (explicitNamedBlock is not null)
        {
            throw new InvalidOperationException(
                $"Packaged executable generation does not support the explicit '{explicitNamedBlock.BlockKind.ToString().ToLowerInvariant()}' named block because embedded AddScript execution cannot preserve script pipeline lifecycle semantics.");
        }

        var exits = ast.FindAll(static node => node is ExitStatementAst, searchNestedScriptBlocks: true)
            .Cast<ExitStatementAst>()
            .ToArray();
        ValidateExits(ast, exits);

        var invocationPaths = FindInvocationPaths(ast).ToArray();
        var parameterBindingPaths = FindParameterBindingPaths(ast).ToArray();
        var dependencyLoadPaths = string.IsNullOrWhiteSpace(dependencyCommandPathExpression)
            ? Array.Empty<VariableExpressionAst>()
            : FindDependencyLoadPaths(ast).ToArray();
        var embeddedResourcePaths = string.IsNullOrWhiteSpace(dependencyCommandPathExpression)
            ? Array.Empty<VariableExpressionAst>()
            : FindEmbeddedResourcePaths(ast, embeddedResourceRelativePaths ?? Array.Empty<string>()).ToArray();
        var replacements = exits.Select(exit => CreateExitReplacement(exit, invocationPaths, commandPathExpression))
            .Concat(invocationPaths
                .Where(path => !exits.Any(exit => Contains(exit.Extent, path.Extent)))
                .Select(path => new SourceReplacement(
                    path.Extent.StartOffset,
                    path.Extent.EndOffset,
                    GetInvocationMetadataExpression(path, commandPathExpression))))
            .Concat(parameterBindingPaths.Select(path => new SourceReplacement(
                path.Extent.StartOffset,
                path.Extent.EndOffset,
                path.VariablePath.UserPath.Equals("PSScriptRoot", StringComparison.OrdinalIgnoreCase)
                    ? "$([System.IO.Path]::GetDirectoryName(" + commandPathExpression + "))"
                    : commandPathExpression)))
            .Concat(dependencyLoadPaths.Select(path => new SourceReplacement(
                path.Extent.StartOffset,
                path.Extent.EndOffset,
                path.VariablePath.UserPath.Equals("PSScriptRoot", StringComparison.OrdinalIgnoreCase)
                    ? "$([System.IO.Path]::GetDirectoryName(" + dependencyCommandPathExpression + "))"
                    : dependencyCommandPathExpression!)))
            .Concat(embeddedResourcePaths.Select(path => new SourceReplacement(
                path.Extent.StartOffset,
                path.Extent.EndOffset,
                "$([System.IO.Path]::GetDirectoryName(" + dependencyCommandPathExpression + "))")))
            .OrderByDescending(static replacement => replacement.StartOffset)
            .ToArray();

        // Parser.ParseFile owns PowerShell's encoding detection (including Windows PowerShell's
        // active-code-page handling for BOM-less files). Reuse that decoded text so the AST
        // offsets and the buffer being rewritten always describe the same source.
        var source = new StringBuilder(ast.Extent.Text);
        foreach (var replacement in replacements)
        {
            source.Remove(replacement.StartOffset, replacement.EndOffset - replacement.StartOffset);
            source.Insert(replacement.StartOffset, replacement.Text);
        }

        var prologueEndOffset = ast.ParamBlock?.Extent.EndOffset ?? 0;
        foreach (var usingStatement in ast.FindAll(static node => node is UsingStatementAst, searchNestedScriptBlocks: false).Cast<UsingStatementAst>())
            prologueEndOffset = Math.Max(prologueEndOffset, usingStatement.Extent.EndOffset);
        prologueEndOffset += replacements
            .Where(replacement => replacement.StartOffset < prologueEndOffset)
            .Sum(static replacement => replacement.Text.Length - (replacement.EndOffset - replacement.StartOffset));
        var pathSemantics = new StringBuilder();
        if (prologueEndOffset > 0 && source[prologueEndOffset - 1] is not '\r' and not '\n') pathSemantics.AppendLine();
        pathSemantics.Append("$script:PSCommandPath = ").AppendLine(commandPathExpression);
        pathSemantics.Append("$script:PSScriptRoot = ").AppendLine(
            string.IsNullOrWhiteSpace(packagedScriptRootExpression)
                ? "[System.IO.Path]::GetDirectoryName($script:PSCommandPath)"
                : packagedScriptRootExpression);
        source.Insert(prologueEndOffset, pathSemantics.ToString());
        return source.ToString();
    }

    private static NamedBlockAst? FindExplicitNamedBlock(ScriptBlockAst ast)
    {
        if (ast.DynamicParamBlock is not null) return ast.DynamicParamBlock;
        if (ast.BeginBlock is not null) return ast.BeginBlock;
        if (ast.ProcessBlock is not null) return ast.ProcessBlock;
        if (ast.GetType().GetProperty("CleanBlock")?.GetValue(ast) is NamedBlockAst cleanBlock) return cleanBlock;
        return ast.EndBlock is { Unnamed: false } ? ast.EndBlock : null;
    }

    private static void ValidateExits(ScriptBlockAst ast, ExitStatementAst[] exits)
    {
        if (exits.Length > 0 && ast.FindAll(static node => node is TrapStatementAst, searchNestedScriptBlocks: true).Any())
            throw new InvalidOperationException("Packaged scripts that combine exit with trap are not supported because exception instrumentation would change trap semantics.");
        if (exits.Any(exit => exits.Any(other => !ReferenceEquals(exit, other) && Contains(exit.Extent, other.Extent))))
            throw new InvalidOperationException("Packaged executable generation does not support nested exit statements.");
        foreach (var exit in exits)
        {
            for (var parent = exit.Parent; parent is not null && !ReferenceEquals(parent, ast); parent = parent.Parent)
            {
                if (parent is FunctionDefinitionAst or ScriptBlockExpressionAst ||
                    parent is ScriptBlockAst scriptBlock && !ReferenceEquals(scriptBlock, ast) ||
                    parent is TryStatementAst tryStatement && tryStatement.CatchClauses.Count > 0)
                    throw new InvalidOperationException($"exit at line {exit.Extent.StartLineNumber} cannot be packaged safely because exception instrumentation would change nested or catch behavior.");
            }
        }
    }

    private static SourceReplacement CreateExitReplacement(
        ExitStatementAst exit,
        MemberExpressionAst[] invocationPaths,
        string commandPathExpression)
    {
        var expression = exit.Pipeline?.Extent.Text;
        var exitCode = "0";
        if (!string.IsNullOrWhiteSpace(expression))
        {
            var rewritten = new StringBuilder(expression);
            foreach (var path in invocationPaths
                         .Where(path => Contains(exit.Pipeline!.Extent, path.Extent))
                         .OrderByDescending(static path => path.Extent.StartOffset))
            {
                var offset = path.Extent.StartOffset - exit.Pipeline!.Extent.StartOffset;
                rewritten.Remove(offset, path.Extent.EndOffset - path.Extent.StartOffset);
                rewritten.Insert(offset, GetInvocationMetadataExpression(path, commandPathExpression));
            }
            exitCode = "[int](" + rewritten + ")";
        }
        return new SourceReplacement(
            exit.Extent.StartOffset,
            exit.Extent.EndOffset,
            "throw [PowerForge.Compiled.PowerForgeScriptExitException]::new(" + exitCode + ")");
    }

    private static bool Contains(IScriptExtent outer, IScriptExtent inner)
        => inner.StartOffset >= outer.StartOffset && inner.EndOffset <= outer.EndOffset;

    private static IEnumerable<MemberExpressionAst> FindInvocationPaths(ScriptBlockAst ast)
    {
        var indirectInvocation = FindIndirectMyInvocationLookup(ast);
        if (indirectInvocation is not null)
        {
            throw new InvalidOperationException(
                $"Packaged executable generation does not preserve indirect top-level invocation metadata lookup '{indirectInvocation.Extent.Text}'. Inspect one of the explicitly supported MyCommand path members directly.");
        }

        var escapedInvocation = ast.FindAll(
                static node => node is VariableExpressionAst variable &&
                               variable.VariablePath.UserPath.Equals("MyInvocation", StringComparison.OrdinalIgnoreCase),
                searchNestedScriptBlocks: true)
            .Cast<VariableExpressionAst>()
            .FirstOrDefault(variable => IsTopLevel(variable, ast) && !IsSupportedMyInvocationReceiver(variable));
        if (escapedInvocation is not null)
        {
            throw new InvalidOperationException(
                $"Packaged executable generation does not preserve escaped top-level invocation metadata '{escapedInvocation.Extent.Text}'; inspect one of the explicitly supported MyCommand path members directly.");
        }

        foreach (var member in ast.FindAll(static node => node is MemberExpressionAst, searchNestedScriptBlocks: true).Cast<MemberExpressionAst>())
        {
            if (IsTopLevelMyCommandReference(member, ast) &&
                !(member.Parent is MemberExpressionAst parent && ReferenceEquals(parent.Expression, member)))
            {
                throw new InvalidOperationException(
                    $"Packaged executable generation does not preserve the top-level invocation command object '{member.Extent.Text}'; inspect one of the explicitly supported MyCommand members instead.");
            }
            if (IsTopLevelDirectMyInvocationMember(member, ast) && !IsTopLevelMyCommandReference(member, ast))
            {
                throw new InvalidOperationException(
                    $"Packaged executable generation does not preserve direct top-level invocation metadata '{member.Extent.Text}'; only explicitly supported MyCommand path metadata can be packaged.");
            }
            if (!IsTopLevelInvocationMetadata(member, ast)) continue;
            if (member.Member is not StringConstantExpressionAst name ||
                !IsSupportedInvocationMetadata(name.Value))
            {
                throw new InvalidOperationException(
                    $"Packaged executable generation does not preserve top-level invocation metadata '{member.Extent.Text}'. Supported MyCommand members are Path, Definition, Name, and CommandType.");
            }
            yield return member;
        }
    }

    private static IEnumerable<VariableExpressionAst> FindParameterBindingPaths(ScriptBlockAst ast)
    {
        if (ast.ParamBlock is null) yield break;
        foreach (var parameter in ast.ParamBlock.Parameters)
        {
            foreach (var variable in parameter
                         .FindAll(static node => node is VariableExpressionAst, searchNestedScriptBlocks: true)
                         .Cast<VariableExpressionAst>())
            {
                if (variable.VariablePath.UserPath.Equals("PSScriptRoot", StringComparison.OrdinalIgnoreCase) ||
                    variable.VariablePath.UserPath.Equals("PSCommandPath", StringComparison.OrdinalIgnoreCase))
                    yield return variable;
            }
        }
    }

    private static IEnumerable<VariableExpressionAst> FindDependencyLoadPaths(ScriptBlockAst ast)
    {
        foreach (var command in ast.FindAll(
                     static node => node is CommandAst { InvocationOperator: TokenKind.Dot },
                     searchNestedScriptBlocks: true).Cast<CommandAst>())
        {
            foreach (var variable in command.CommandElements
                         .Take(1)
                         .SelectMany(static element => element.FindAll(
                             static node => node is VariableExpressionAst,
                             searchNestedScriptBlocks: true))
                         .Cast<VariableExpressionAst>())
            {
                if (variable.VariablePath.UserPath.Equals("PSScriptRoot", StringComparison.OrdinalIgnoreCase) ||
                    variable.VariablePath.UserPath.Equals("PSCommandPath", StringComparison.OrdinalIgnoreCase))
                    yield return variable;
            }
        }
    }

    private static IEnumerable<VariableExpressionAst> FindEmbeddedResourcePaths(
        ScriptBlockAst ast,
        IReadOnlyCollection<string> embeddedResourceRelativePaths)
    {
        if (embeddedResourceRelativePaths.Count == 0) yield break;
        var selected = embeddedResourceRelativePaths
            .Select(static path => path.Replace('\\', '/'))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        foreach (var literal in ast.FindAll(static node => node is ExpandableStringExpressionAst, searchNestedScriptBlocks: true)
                     .OfType<ExpandableStringExpressionAst>())
        {
            if (literal.NestedExpressions.Count != 1 ||
                literal.NestedExpressions[0] is not VariableExpressionAst variable ||
                !variable.VariablePath.UserPath.Equals("PSScriptRoot", StringComparison.OrdinalIgnoreCase))
                continue;
            var text = literal.Extent.Text.Trim().Trim('"', '\'');
            var prefixes = new[] { "$PSScriptRoot/", "$PSScriptRoot\\", "${PSScriptRoot}/", "${PSScriptRoot}\\" };
            var prefix = prefixes.FirstOrDefault(candidate => text.StartsWith(candidate, StringComparison.OrdinalIgnoreCase));
            if (prefix is null) continue;
            var relative = text.Substring(prefix.Length).Replace('\\', '/');
            if (selected.Contains(relative))
                yield return variable;
        }
    }

    private static bool IsTopLevelInvocationMetadata(MemberExpressionAst member, ScriptBlockAst root)
    {
        if (member.Expression is not MemberExpressionAst command ||
            !IsTopLevelMyCommandReference(command, root))
            return false;

        return IsTopLevel(member, root);
    }

    private static bool IsTopLevelMyCommandReference(MemberExpressionAst member, ScriptBlockAst root)
    {
        if (member.Member is not StringConstantExpressionAst myCommand || !myCommand.Value.Equals("MyCommand", StringComparison.OrdinalIgnoreCase) ||
            member.Expression is not VariableExpressionAst invocation || !invocation.VariablePath.UserPath.Equals("MyInvocation", StringComparison.OrdinalIgnoreCase))
            return false;

        return IsTopLevel(member, root);
    }

    private static bool IsTopLevelDirectMyInvocationMember(MemberExpressionAst member, ScriptBlockAst root)
        => member.Expression is VariableExpressionAst invocation &&
           invocation.VariablePath.UserPath.Equals("MyInvocation", StringComparison.OrdinalIgnoreCase) &&
           IsTopLevel(member, root);

    private static bool IsSupportedMyInvocationReceiver(VariableExpressionAst invocation)
        => invocation.Parent is MemberExpressionAst { Expression: var expression } &&
           ReferenceEquals(expression, invocation);

    private static Ast? FindIndirectMyInvocationLookup(ScriptBlockAst ast)
    {
        var command = ast.FindAll(static node => node is CommandAst, searchNestedScriptBlocks: true)
            .Cast<CommandAst>()
            .FirstOrDefault(candidate => IsTopLevel(candidate, ast) && RetrievesMyInvocation(candidate));
        if (command is not null) return command;

        return ast.FindAll(static node => node is InvokeMemberExpressionAst, searchNestedScriptBlocks: true)
            .Cast<InvokeMemberExpressionAst>()
            .FirstOrDefault(candidate =>
                IsTopLevel(candidate, ast) &&
                candidate.Member is StringConstantExpressionAst member &&
                (member.Value.Equals("Get", StringComparison.OrdinalIgnoreCase) ||
                 member.Value.Equals("GetValue", StringComparison.OrdinalIgnoreCase)) &&
                ReferencesPsVariableIntrinsics(candidate.Expression) &&
                (candidate.Arguments.Count != 1 ||
                 candidate.Arguments[0] is not StringConstantExpressionAst argument ||
                 VariablePatternMatches(argument.Value, "MyInvocation")));
    }

    private static bool RetrievesMyInvocation(CommandAst command)
    {
        var commandName = command.GetCommandName();
        if (string.IsNullOrWhiteSpace(commandName)) return false;
        var leafName = commandName!.Split('\\').Last();
        if (leafName.Equals("Get-Variable", StringComparison.OrdinalIgnoreCase) ||
            leafName.Equals("gv", StringComparison.OrdinalIgnoreCase))
            return GetVariableMayRetrieveMyInvocation(command);
        if (!leafName.Equals("Get-Item", StringComparison.OrdinalIgnoreCase) &&
            !leafName.Equals("Get-Content", StringComparison.OrdinalIgnoreCase) &&
            !leafName.Equals("gi", StringComparison.OrdinalIgnoreCase) &&
            !leafName.Equals("gc", StringComparison.OrdinalIgnoreCase))
            return false;
        return GetCommandArgumentLiterals(command).Any(static value =>
            TryGetVariableProviderPattern(value, out var pattern) &&
            VariablePatternMatches(pattern!, "MyInvocation"));
    }

    private static bool GetVariableMayRetrieveMyInvocation(CommandAst command)
    {
        var elements = command.CommandElements.Skip(1).ToArray();
        for (var index = 0; index < elements.Length; index++)
        {
            if (elements[index] is not CommandParameterAst parameter ||
                !parameter.ParameterName.Equals("Name", StringComparison.OrdinalIgnoreCase))
                continue;
            if (parameter.Argument is StringConstantExpressionAst inline)
                return VariablePatternMatches(inline.Value, "MyInvocation");
            return index + 1 >= elements.Length ||
                   elements[index + 1] is not StringConstantExpressionAst named ||
                   VariablePatternMatches(named.Value, "MyInvocation");
        }
        return elements.FirstOrDefault() is not StringConstantExpressionAst positional ||
               VariablePatternMatches(positional.Value, "MyInvocation");
    }

    private static IEnumerable<string> GetCommandArgumentLiterals(CommandAst command)
    {
        foreach (var element in command.CommandElements.Skip(1))
        {
            if (element is StringConstantExpressionAst literal)
            {
                yield return literal.Value;
                continue;
            }
            foreach (var nested in element.FindAll(static node => node is StringConstantExpressionAst, searchNestedScriptBlocks: true)
                         .Cast<StringConstantExpressionAst>())
                yield return nested.Value;
        }
    }

    private static bool ReferencesPsVariableIntrinsics(ExpressionAst expression)
    {
        for (Ast? current = expression; current is MemberExpressionAst member; current = member.Expression)
        {
            if (member.Member is StringConstantExpressionAst name &&
                name.Value.Equals("PSVariable", StringComparison.OrdinalIgnoreCase))
                return true;
        }
        return false;
    }

    private static bool TryGetVariableProviderPattern(string value, out string? pattern)
    {
        pattern = null;
        const string prefix = "Variable:";
        if (!value.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return false;
        pattern = value.Substring(prefix.Length).TrimStart('\\', '/');
        return !string.IsNullOrWhiteSpace(pattern);
    }

    private static bool VariablePatternMatches(string pattern, string name)
        => new System.Management.Automation.WildcardPattern(
                pattern,
                System.Management.Automation.WildcardOptions.IgnoreCase |
                System.Management.Automation.WildcardOptions.CultureInvariant)
            .IsMatch(name);

    internal static void ValidateDotSources(
        ScriptBlockAst ast,
        string sourcePath,
        IReadOnlyCollection<string> embeddedScriptPaths)
    {
        var allowed = embeddedScriptPaths
            .Select(Path.GetFullPath)
            .ToHashSet(PowerShellCompilationPathSafety.PathComparer);
        var sourceDirectory = Path.GetDirectoryName(Path.GetFullPath(sourcePath)) ?? Directory.GetCurrentDirectory();
        foreach (var command in ast.FindAll(
                     static node => node is CommandAst { InvocationOperator: TokenKind.Dot },
                     searchNestedScriptBlocks: true).Cast<CommandAst>())
        {
            var expression = command.CommandElements.FirstOrDefault();
            if (!TryGetScriptRootRelativePath(expression, out var relativePath))
            {
                throw new InvalidOperationException(
                    $"Packaged executable generation does not support dot-sourced command '{command.Extent.Text}' because its target is not a literal $PSScriptRoot path embedded with the artifact.");
            }

            string targetPath;
            try
            {
                targetPath = Path.GetFullPath(Path.Combine(
                    sourceDirectory,
                    relativePath!.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar)));
            }
            catch (Exception exception) when (exception is ArgumentException or NotSupportedException or PathTooLongException)
            {
                throw new InvalidOperationException(
                    $"Packaged executable generation cannot resolve dot-sourced command '{command.Extent.Text}' to an embedded dependency.",
                    exception);
            }
            if (!allowed.Contains(targetPath))
            {
                throw new InvalidOperationException(
                    $"Packaged executable generation does not support dot-sourced command '{command.Extent.Text}' because resolved dependency '{targetPath}' is not embedded with the artifact.");
            }
        }
    }

    private static bool TryGetScriptRootRelativePath(CommandElementAst? expression, out string? relativePath)
    {
        relativePath = null;
        if (expression is not ExpandableStringExpressionAst expandable ||
            expandable.NestedExpressions.Count != 1 ||
            expandable.NestedExpressions[0] is not VariableExpressionAst variable ||
            !variable.VariablePath.UserPath.Equals("PSScriptRoot", StringComparison.OrdinalIgnoreCase))
            return false;
        var text = expression.Extent.Text.Trim().Trim('"');
        var prefixes = new[] { "$PSScriptRoot/", "$PSScriptRoot\\", "${PSScriptRoot}/", "${PSScriptRoot}\\" };
        var prefix = prefixes.FirstOrDefault(candidate => text.StartsWith(candidate, StringComparison.OrdinalIgnoreCase));
        if (prefix is null) return false;
        var candidate = text.Substring(prefix.Length);
        if (string.IsNullOrWhiteSpace(candidate) || candidate.IndexOf('$') >= 0 || candidate.IndexOf('`') >= 0)
            return false;
        relativePath = candidate;
        return true;
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

    private static bool IsSupportedInvocationMetadata(string name)
        => name.Equals("Path", StringComparison.OrdinalIgnoreCase) ||
           name.Equals("Definition", StringComparison.OrdinalIgnoreCase) ||
           name.Equals("Name", StringComparison.OrdinalIgnoreCase) ||
           name.Equals("CommandType", StringComparison.OrdinalIgnoreCase);

    private static string GetInvocationMetadataExpression(MemberExpressionAst member, string commandPathExpression)
        => member.Member is StringConstantExpressionAst name
            ? name.Value.ToUpperInvariant() switch
            {
                "NAME" => "$([System.IO.Path]::GetFileName(" + commandPathExpression + "))",
                "COMMANDTYPE" => "([System.Management.Automation.CommandTypes]::ExternalScript)",
                _ => commandPathExpression
            }
            : throw new InvalidOperationException("Packaged invocation metadata must use a literal member name.");

    internal static void ValidateHostInteraction(ScriptBlockAst ast)
    {
        var confirmationInvocation = FindConfirmationInvocation(ast);
        if (SupportsShouldProcess(ast) || confirmationInvocation is not null)
        {
            throw new InvalidOperationException(
                $"Packaged executable generation does not support confirmation-capable script interaction '{confirmationInvocation?.Extent.Text ?? "SupportsShouldProcess"}' because the embedded runspace has no console-backed PSHost.");
        }

        var hostReference = ast.FindAll(
                static node => node is VariableExpressionAst variable &&
                               variable.VariablePath.UserPath.Equals("Host", StringComparison.OrdinalIgnoreCase),
                searchNestedScriptBlocks: true)
            .Cast<VariableExpressionAst>()
            .FirstOrDefault();
        if (hostReference is not null)
        {
            throw new InvalidOperationException(
                $"Packaged executable generation does not support interactive PSHost access '{hostReference.Extent.Text}'. Use a typed console entry point or keep this script on pwsh -File.");
        }

        var interactiveCommand = ast.FindAll(static node => node is CommandAst, searchNestedScriptBlocks: true)
            .Cast<CommandAst>()
            .FirstOrDefault(static command => IsInteractiveCommand(command.GetCommandName()));
        if (interactiveCommand is not null)
        {
            throw new InvalidOperationException(
                $"Packaged executable generation does not support interactive command '{interactiveCommand.GetCommandName()}' because the embedded runspace has no console-backed PSHost.");
        }
    }

    private static bool IsInteractiveCommand(string? commandName)
    {
        if (string.IsNullOrWhiteSpace(commandName)) return false;
        var unqualifiedName = commandName!.Split('\\').Last();
        return unqualifiedName.Equals("Read-Host", StringComparison.OrdinalIgnoreCase) ||
               unqualifiedName.Equals("Get-Credential", StringComparison.OrdinalIgnoreCase) ||
               unqualifiedName.Equals("Show-Command", StringComparison.OrdinalIgnoreCase) ||
               unqualifiedName.Equals("Out-GridView", StringComparison.OrdinalIgnoreCase) ||
               unqualifiedName.Equals("Write-Progress", StringComparison.OrdinalIgnoreCase);
    }

    private static bool SupportsShouldProcess(ScriptBlockAst ast)
        => ast.ParamBlock?.Attributes
            .OfType<AttributeAst>()
            .Where(static attribute => attribute.TypeName.Name.Equals("CmdletBinding", StringComparison.OrdinalIgnoreCase) ||
                                       attribute.TypeName.Name.Equals("CmdletBindingAttribute", StringComparison.OrdinalIgnoreCase))
            .SelectMany(static attribute => attribute.NamedArguments)
            .Any(static argument => argument.ArgumentName.Equals("SupportsShouldProcess", StringComparison.OrdinalIgnoreCase) &&
                                    IsConfirmationCapable(argument)) == true;

    private static InvokeMemberExpressionAst? FindConfirmationInvocation(ScriptBlockAst ast)
        => ast.FindAll(
                static node => node is InvokeMemberExpressionAst invocation &&
                               invocation.Expression is VariableExpressionAst variable &&
                               variable.VariablePath.UserPath.Equals("PSCmdlet", StringComparison.OrdinalIgnoreCase) &&
                               invocation.Member is StringConstantExpressionAst member &&
                               (member.Value.Equals("ShouldProcess", StringComparison.OrdinalIgnoreCase) ||
                                member.Value.Equals("ShouldContinue", StringComparison.OrdinalIgnoreCase)),
                searchNestedScriptBlocks: true)
            .Cast<InvokeMemberExpressionAst>()
            .FirstOrDefault();

    private static bool IsConfirmationCapable(NamedAttributeArgumentAst argument)
    {
        try
        {
            return argument.Argument.SafeGetValue() is not bool value || value;
        }
        catch (InvalidOperationException)
        {
            return true;
        }
    }

    private readonly struct SourceReplacement
    {
        internal SourceReplacement(int startOffset, int endOffset, string text)
        {
            StartOffset = startOffset;
            EndOffset = endOffset;
            Text = text;
        }

        internal int StartOffset { get; }
        internal int EndOffset { get; }
        internal string Text { get; }
    }
}
