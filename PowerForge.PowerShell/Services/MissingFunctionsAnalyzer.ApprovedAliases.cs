using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Language;

namespace PowerForge;

public sealed partial class MissingFunctionsAnalyzer
{
    private static AuthoredAliases ReadRootAliases(ScriptBlockAst ast, FunctionDefinitionAst[] functions)
    {
        var names = new HashSet<string>(functions.SelectMany(PowerShellAdvancedFunctionPolicy.GetAliases),
            StringComparer.OrdinalIgnoreCase);
        var sharedNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        bool hasUnknownNames = functions.Any(PowerShellAdvancedFunctionPolicy.HasNonLiteralAlias);
        var scriptLevelCommands = new HashSet<CommandAst>(
            ast.FindAll(static node => node is CommandAst, searchNestedScriptBlocks: false).Cast<CommandAst>());
        foreach (var command in ast.FindAll(static node => node is CommandAst, searchNestedScriptBlocks: true).Cast<CommandAst>())
        {
            if (!scriptLevelCommands.Contains(command) && !PowerShellAliasDefinitionPolicy.MayWriteOutsideLocalScope(command))
                continue;
            if (PowerShellAliasDefinitionPolicy.TryGetLiteralDefinition(command, out string alias, out _))
            {
                names.Add(alias);
                if (PowerShellAliasDefinitionPolicy.MayShareAliasWithChildScopes(command))
                    sharedNames.Add(alias);
            }
            else if (PowerShellAliasDefinitionPolicy.IsAliasDefinitionCommand(command) ||
                     PowerShellAliasDefinitionPolicy.TargetsAliasProvider(command, ast))
                hasUnknownNames = true;
        }
        return new AuthoredAliases(names, sharedNames, hasUnknownNames);
    }

    private static MissingFunctionCommand[] DistinctResolvedCommands(IEnumerable<MissingFunctionCommand> commands)
        => commands.GroupBy(command => command.Source + "\0" + command.Name + "\0" + command.CommandType,
                StringComparer.OrdinalIgnoreCase)
            .Select(group => group.FirstOrDefault(command => !string.IsNullOrWhiteSpace(command.Error)) ?? group.First())
            .ToArray();

    private static IEnumerable<string> ReadLocalAliasTargets(
        ScriptBlockAst root,
        IEnumerable<CommandAst> commands,
        NestedFunctionVisibilityAnalyzer visibility)
    {
        var rootCommands = new HashSet<CommandAst>(
            root.FindAll(static node => node is CommandAst, searchNestedScriptBlocks: false).Cast<CommandAst>());
        foreach (var command in commands)
        {
            if (!rootCommands.Contains(command) &&
                PowerShellAliasDefinitionPolicy.TryGetLiteralDefinition(command, out _, out string target) &&
                !PowerShellAliasDefinitionPolicy.MayWriteOutsideLocalScope(command) &&
                !visibility.HasEstablishedLocalAlias(command, target) &&
                !visibility.IsDeclaredInVisibleScope(command, target))
                yield return target;
        }
    }

    private string? FindUnpreservableAlias(
        ScriptBlockAst body,
        ApprovedModuleSource? source,
        Func<string, ApprovedModuleSource?, MissingFunctionCommand> resolveInvocation,
        HashSet<string> consumerFunctions,
        AuthoredAliases consumerAliases)
    {
        NestedFunctionVisibilityAnalyzer? visibility = null;
        foreach (var command in body.FindAll(static node => node is CommandAst, searchNestedScriptBlocks: true).Cast<CommandAst>())
        {
            if (!PowerShellAliasDefinitionPolicy.IsAliasDefinitionCommand(command))
            {
                if (PowerShellAliasDefinitionPolicy.TargetsAliasProvider(command, body))
                    return "changes the donor alias scope through the Alias provider, which cannot be preserved after merging.";
                continue;
            }

            if (PowerShellAliasDefinitionPolicy.MayWriteOutsideLocalScope(command))
                return "changes the donor alias scope, which cannot be preserved after merging.";
            if (PowerShellAliasDefinitionPolicy.MaySkipThroughShouldProcess(command))
                return "conditionally creates a local alias with WhatIf or Confirm, whose binding cannot be proven after merging.";
            if (!PowerShellAliasDefinitionPolicy.TryGetLiteralDefinition(command, out string alias, out string target))
                return "creates a local alias whose name or target cannot be resolved statically.";
            if (consumerAliases.SharedNames.Contains(alias))
                return $"creates local alias '{alias}', which conflicts with a consumer alias shared with child scopes.";

            visibility ??= NestedFunctionVisibilityAnalyzer.Create(body);
            if (visibility.HasLocalAliasDeclaration(command, target))
            {
                if (visibility.HasEstablishedLocalAlias(command, target))
                    continue;
                return $"creates local alias '{alias}' with target '{target}', whose function-local binding is ambiguous.";
            }
            if (visibility.IsDeclaredInVisibleScope(command, target))
                continue;

            var unqualified = GetModuleQualifier(target) is null;
            var observed = unqualified && source is not null
                ? GetCommandFromModuleScopeCached(source, target, includePrivate: true, requireOwnership: false)
                : null;
            var resolution = resolveInvocation(target, source);
            if (!string.IsNullOrWhiteSpace(resolution.Error) || unqualified && (source is not null && observed is null ||
                    consumerFunctions.Contains(target) || consumerAliases.Names.Contains(target)))
            {
                return $"creates local alias '{alias}' with target '{target}', whose donor binding cannot be preserved after merging.";
            }
        }
        return null;
    }

    private bool RejectUnsafeApprovedBindings(
        List<MissingFunctionCommand> commands, HashSet<string> approvedModules,
        IReadOnlyDictionary<string, ApprovedModuleSource> approvedModuleSources,
        HashSet<string> consumerFunctions, ParsedInput consumer,
        List<ApprovedInvocationScope> invocationScopes,
        Func<string, ApprovedModuleSource?, MissingFunctionCommand> resolveInvocation)
    {
        var functions = commands.Where(command => command.ScriptBlock is not null && approvedModules.Contains(command.Source))
            .ToArray();
        var comparer = StringComparer.OrdinalIgnoreCase;
        var functionsByName = functions.ToLookup(GetInlineFunctionName, comparer);
        var emittedAliases = functions.SelectMany(command => ReadFunctionAliases(command)
            .Concat(command.InlineFunctionName is null ? Array.Empty<string>() : new[] { command.Name })
            .Distinct(comparer)
            .Select(name => (Name: name, Command: command)))
            .ToArray();
        var aliasesByName = emittedAliases.ToLookup(binding => binding.Name, binding => binding.Command, comparer);
        var aliasesByTarget = emittedAliases.ToLookup(binding => binding.Command);

        foreach (var function in functions)
        {
            string target = GetInlineFunctionName(function);
            approvedModuleSources.TryGetValue(function.Source, out var source);
            var body = function.ScriptBlock!.Ast switch
            {
                // Function-header parameters become a param block in the emitted definition.
                FunctionDefinitionAst { Parameters.Count: > 0 } => Parser.ParseInput(function.ScriptBlock.ToString(), out _, out _),
                FunctionDefinitionAst declaration => declaration.Body,
                ScriptBlockAst script => script,
                _ => null
            };
            if (body is null)
            {
                function.Error = $"Cannot inspect the script body of '{function.Name}' from '{function.Source}'.";
                continue;
            }
            if (FindDonorContextIssue(body, source, consumerFunctions, consumer.DeclaredAliases) is { } contextIssue)
            {
                function.Error = $"Command '{function.Name}' from '{function.Source}' {contextIssue}";
                continue;
            }
            if (FindUnpreservableAlias(body, source, resolveInvocation, consumerFunctions, consumer.DeclaredAliases) is { } aliasError)
            {
                function.Error = $"Command '{function.Name}' from '{function.Source}' {aliasError}";
                continue;
            }
            if (consumer.DeclaredAliases.HasUnknownNames)
            {
                function.Error = $"Command '{function.Name}' from '{function.Source}' cannot be merged safely because a consumer alias declaration cannot be resolved statically.";
                continue;
            }
            if (consumerFunctions.Contains(target) || consumer.DeclaredAliases.Names.Contains(target) ||
                aliasesByName.Contains(target) || functionsByName[target].Any(other => !SameTarget(other)))
            {
                function.Error = DescribeConflict(target, "the merged module");
                continue;
            }

            var names = aliasesByTarget[function].Select(binding => binding.Name).ToArray();
            var conflict = names.FirstOrDefault(name => consumerFunctions.Contains(name) ||
                consumer.DeclaredAliases.Names.Contains(name) || functionsByName.Contains(name) ||
                aliasesByName[name].Any(other => !SameTarget(other)));
            if (conflict is not null)
            {
                function.Error = DescribeConflict(conflict, "the merged module");
                continue;
            }

            foreach (var scope in invocationScopes)
            {
                if (comparer.Equals(function.Source, scope.Donor?.Name))
                    continue;
                conflict = names.FirstOrDefault(name => scope.LocalFunctionCalls.Contains(name) || ChangesBinding(name));
                if (conflict is null && ChangesBinding(target))
                    conflict = target;
                if (conflict is null)
                    continue;
                string caller = scope.Donor is null ? "the consumer" : $"donor '{scope.Donor.Name}'";
                function.Error = DescribeConflict(conflict, caller);
                break;

                bool ChangesBinding(string name)
                    => scope.InvokedNames.Contains(name) && !SameTarget(resolveInvocation(name, scope.Donor));
            }

            bool SameTarget(MissingFunctionCommand command)
                => string.IsNullOrWhiteSpace(command.Error) && comparer.Equals(command.Source, function.Source) &&
                   comparer.Equals(GetInlineFunctionName(command), target);

            string DescribeConflict(string name, string scope)
                => $"Command '{function.Name}' from '{function.Source}', with function '{target}', would cause a name conflict at '{name}' in {scope}.";
        }

        return functions.Any(command => !string.IsNullOrWhiteSpace(command.Error));
    }

    private static string GetInlineFunctionName(MissingFunctionCommand command)
        => command.InlineFunctionName ?? command.Name;

    private static IEnumerable<string> ReadFunctionAliases(MissingFunctionCommand command)
        => command.ScriptBlock!.Attributes.OfType<AliasAttribute>().SelectMany(attribute => attribute.AliasNames);

    private sealed class AuthoredAliases
    {
        internal AuthoredAliases(HashSet<string> names, HashSet<string> sharedNames, bool hasUnknownNames)
            => (Names, SharedNames, HasUnknownNames) = (names, sharedNames, hasUnknownNames);

        internal HashSet<string> Names { get; }
        internal HashSet<string> SharedNames { get; }
        internal bool HasUnknownNames { get; }
    }

    private sealed class ApprovedInvocationScope
    {
        public ApprovedInvocationScope(ApprovedModuleSource? donor, ParsedInput parsed)
        {
            Donor = donor;
            InvokedNames = new HashSet<string>(parsed.CommandNames, StringComparer.OrdinalIgnoreCase);
            LocalFunctionCalls = parsed.LocalFunctionCalls;
        }

        public ApprovedModuleSource? Donor { get; }
        public HashSet<string> InvokedNames { get; }
        public HashSet<string> LocalFunctionCalls { get; }
    }

    private MissingFunctionCommand CreateApprovedResolution(
        ApprovedModuleSource source,
        CommandInfo command,
        bool qualified = false,
        bool isPrivate = true)
    {
        if (command is not AliasInfo alias)
            return CreateResolution(command, isAlias: false, isPrivate: isPrivate);

        // Resolve inside the selected module. The target can be private or reached through
        // another alias, and the build host can have unrelated commands with the same names.
        CommandInfo? target = alias;
        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        while (!qualified && target is AliasInfo next && visited.Add(next.Name))
        {
            if (GetModuleQualifier(next.Definition) is not null || WildcardPattern.ContainsWildcardCharacters(next.Definition))
                break;
            target = GetCommandFromModuleScopeCached(source, next.Definition, includePrivate: true);
        }

        if (target is FunctionInfo function && function.ScriptBlock is not null)
        {
            return new MissingFunctionCommand(alias.Name, source.Name, "Alias", isAlias: true,
                isPrivate: isPrivate, error: string.Empty, scriptBlock: function.ScriptBlock)
            {
                InlineFunctionName = function.Name,
                InlineFunctionIsFilter = function is FilterInfo
            };
        }

        string error = qualified
            ? $"The module-qualified alias '{source.Name}\\{alias.Name}' cannot refer to a donor removed from the package."
            : $"The alias '{alias.Name}' does not resolve to a script function owned by '{source.Name}'.";
        return new MissingFunctionCommand(alias.Name, source.Name, "Alias", isAlias: true,
            isPrivate: isPrivate, error: error, scriptBlock: null);
    }
}
