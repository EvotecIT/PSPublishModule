using System;
using System.Collections.Generic;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Language;

namespace PowerForge;

public sealed partial class MissingFunctionsAnalyzer
{
    private static HashSet<string> ReadRootAliasNames(ScriptBlockAst ast, FunctionDefinitionAst[] functions)
    {
        var names = new HashSet<string>(functions.SelectMany(PowerShellAdvancedFunctionPolicy.GetAliases),
            StringComparer.OrdinalIgnoreCase);
        foreach (var command in ast.FindAll(node => node is CommandAst, searchNestedScriptBlocks: false).Cast<CommandAst>())
            if (PowerShellAliasDefinitionPolicy.TryGetLiteralDefinition(command, out string alias, out _))
                names.Add(alias);
        return names;
    }

    private static bool RejectConflictingAliasBindings(
        List<MissingFunctionCommand> commands, HashSet<string> approvedModules,
        HashSet<string> consumerFunctions, HashSet<string> consumerAliases,
        List<AliasInvocationScope> invocationScopes,
        Func<string, ApprovedModuleSource?, MissingFunctionCommand> resolveInvocation)
    {
        var aliases = commands.Where(command => command.InlineFunctionName is not null).ToArray();
        if (aliases.Length == 0)
            return false;

        var comparer = StringComparer.OrdinalIgnoreCase;
        var functions = commands.Where(command => command.ScriptBlock is not null && approvedModules.Contains(command.Source));
        var functionsByName = functions.ToLookup(command => command.InlineFunctionName ?? command.Name, comparer);
        var aliasesByName = aliases.Select(alias => (Name: alias.Name, Command: alias))
            .Concat(functions.SelectMany(command => ReadFunctionAliases(command).Select(name => (Name: name, Command: command))))
            .ToLookup(binding => binding.Name, binding => binding.Command, comparer);

        foreach (var alias in aliases)
        {
            string target = alias.InlineFunctionName!;
            var aliasNames = new HashSet<string>(ReadFunctionAliases(alias), comparer) { alias.Name };
            var conflict = aliasNames.FirstOrDefault(name => consumerFunctions.Contains(name) ||
                consumerAliases.Contains(name) || functionsByName.Contains(name) ||
                aliasesByName[name].Any(other => !SameTarget(other)));
            if (conflict is null && (consumerFunctions.Contains(target) || consumerAliases.Contains(target) ||
                aliasesByName.Contains(target) || functionsByName[target].Any(other => !SameTarget(other))))
                conflict = target;
            if (conflict is not null)
            {
                alias.Error = $"The alias '{alias.Name}' from '{alias.Source}', with target '{target}', would cause a name conflict at '{conflict}' in the merged module.";
                continue;
            }

            foreach (var scope in invocationScopes)
            {
                if (comparer.Equals(alias.Source, scope.Donor?.Name))
                    continue;
                conflict = aliasNames.FirstOrDefault(name => scope.LocalFunctionCalls.Contains(name) || ChangesBinding(name));
                if (conflict is null && ChangesBinding(target))
                    conflict = target;
                if (conflict is null)
                    continue;
                string caller = scope.Donor is null ? "the consumer" : $"'{scope.Donor.Name}'";
                alias.Error = $"The alias '{alias.Name}' from '{alias.Source}', with target '{target}', would cause a name conflict by changing the binding of '{conflict}' in {caller}.";
                break;

                bool ChangesBinding(string name)
                    => scope.InvokedNames.Contains(name) && !SameTarget(resolveInvocation(name, scope.Donor));
            }

            bool SameTarget(MissingFunctionCommand command)
                => comparer.Equals(command.Source, alias.Source) &&
                   comparer.Equals(command.InlineFunctionName ?? command.Name, target);
        }

        return aliases.Any(alias => !string.IsNullOrWhiteSpace(alias.Error));
    }

    private static IEnumerable<string> ReadFunctionAliases(MissingFunctionCommand command)
        => command.ScriptBlock!.Attributes.OfType<AliasAttribute>().SelectMany(attribute => attribute.AliasNames);

    private sealed class AliasInvocationScope
    {
        public AliasInvocationScope(ApprovedModuleSource? donor, ParsedInput parsed)
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
