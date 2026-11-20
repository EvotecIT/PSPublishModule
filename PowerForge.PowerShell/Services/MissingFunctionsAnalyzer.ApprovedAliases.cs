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
        List<MissingFunctionCommand> commands, HashSet<string> consumerFunctions, HashSet<string> consumerAliases,
        List<AliasInvocationScope> invocationScopes,
        Func<string, ApprovedModuleSource?, MissingFunctionCommand> resolveInvocation)
    {
        var aliases = commands.Where(command => command.InlineFunctionName is not null).ToArray();
        if (aliases.Length == 0)
            return false;

        var comparer = StringComparer.OrdinalIgnoreCase;
        var functionsByName = commands.Where(command => command.ScriptBlock is not null)
            .ToLookup(command => command.InlineFunctionName ?? command.Name, comparer);
        var aliasesByName = aliases.ToLookup(alias => alias.Name, comparer);
        foreach (var alias in aliases)
        {
            string target = alias.InlineFunctionName!;
            if (consumerFunctions.Contains(target) || consumerFunctions.Contains(alias.Name) ||
                consumerAliases.Contains(target) || consumerAliases.Contains(alias.Name) ||
                functionsByName.Contains(alias.Name) || aliasesByName.Contains(target) ||
                functionsByName[target].Any(function => !comparer.Equals(function.Source, alias.Source)) ||
                aliasesByName[alias.Name].Any(other => !comparer.Equals(other.Source, alias.Source) ||
                                                      !comparer.Equals(other.InlineFunctionName, target)))
            {
                alias.Error = $"The alias '{alias.Name}' from '{alias.Source}' cannot bind to '{target}' without a name conflict in the merged module.";
                continue;
            }

            foreach (var scope in invocationScopes)
            {
                if (comparer.Equals(alias.Source, scope.Donor?.Name))
                    continue;
                string? conflict = ChangesBinding(alias.Name) ? alias.Name : ChangesBinding(target) ? target : null;
                if (conflict is null)
                    continue;
                string caller = scope.Donor is null ? "the consumer" : $"'{scope.Donor.Name}'";
                alias.Error = $"The alias '{alias.Name}' from '{alias.Source}', with target '{target}', would cause a name conflict by changing the binding of '{conflict}' in {caller}.";
                break;

                bool ChangesBinding(string name)
                {
                    if (!scope.InvokedNames.Contains(name))
                        return false;
                    // Reuse the original scope even for names filtered out of recursive analysis.
                    var binding = resolveInvocation(name, scope.Donor);
                    return !comparer.Equals(binding.Source, alias.Source) ||
                           !comparer.Equals(binding.InlineFunctionName ?? binding.Name, target);
                }
            }
        }

        return aliases.Any(alias => !string.IsNullOrWhiteSpace(alias.Error));
    }

    private sealed class AliasInvocationScope
    {
        public AliasInvocationScope(ApprovedModuleSource? donor, IEnumerable<string> invokedNames)
        {
            Donor = donor;
            InvokedNames = new HashSet<string>(invokedNames, StringComparer.OrdinalIgnoreCase);
        }

        public ApprovedModuleSource? Donor { get; }
        public HashSet<string> InvokedNames { get; }
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
                InlineFunctionName = function.Name
            };
        }

        string error = qualified
            ? $"The module-qualified alias '{source.Name}\\{alias.Name}' cannot refer to a donor removed from the package."
            : $"The alias '{alias.Name}' does not resolve to a script function owned by '{source.Name}'.";
        return new MissingFunctionCommand(alias.Name, source.Name, "Alias", isAlias: true,
            isPrivate: isPrivate, error: error, scriptBlock: null);
    }
}
