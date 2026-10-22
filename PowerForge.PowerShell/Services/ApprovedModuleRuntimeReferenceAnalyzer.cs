using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Management.Automation;
using System.Management.Automation.Language;
using System.Text.RegularExpressions;
using Microsoft.PowerShell.Commands;

namespace PowerForge;

/// <summary>
/// Finds approved modules that must remain runtime dependencies because script syntax still refers
/// to the original module identity after eligible helper functions are inlined.
/// </summary>
internal static class ApprovedModuleRuntimeReferenceAnalyzer
{
    private static readonly HashSet<string> ModuleManagementSwitchParameters = new(StringComparer.OrdinalIgnoreCase)
    {
        "All",
        "AsCustomObject",
        "Confirm",
        "Debug",
        "DisableNameChecking",
        "Force",
        "Global",
        "ListAvailable",
        "NoClobber",
        "PassThru",
        "Refresh",
        "SkipEditionCheck",
        "UseWindowsPowerShell",
        "Verbose",
        "WhatIf"
    };

    internal static string[] Find(
        ScriptBlockAst ast,
        IEnumerable<string> commandNames,
        IReadOnlyDictionary<string, ApprovedModuleSource> approvedModuleSources,
        Func<ApprovedModuleSource, string, bool>? typeOwnershipResolver = null)
    {
        if (approvedModuleSources is null || approvedModuleSources.Count == 0)
            return Array.Empty<string>();

        var blocked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var commandName in commandNames ?? Array.Empty<string>())
        {
            var qualifier = GetModuleQualifier(commandName);
            if (qualifier is not null && approvedModuleSources.ContainsKey(qualifier))
                blocked.Add(qualifier);
        }

        foreach (var statement in ast.FindAll(
                     static node => node is UsingStatementAst usingStatement &&
                                    usingStatement.UsingStatementKind == UsingStatementKind.Module,
                     searchNestedScriptBlocks: true)
                 .Cast<UsingStatementAst>())
        {
            var referencedModule = GetUsingModuleName(statement);
            if (referencedModule is not null && approvedModuleSources.ContainsKey(referencedModule))
                blocked.Add(referencedModule);
        }

        foreach (var requiredModule in ast.ScriptRequirements?.RequiredModules.AsEnumerable() ?? Enumerable.Empty<ModuleSpecification>())
        {
            var referencedModule = NormalizeModuleReferenceName(requiredModule.Name);
            if (referencedModule is not null && approvedModuleSources.ContainsKey(referencedModule))
                blocked.Add(referencedModule);
        }

        foreach (var command in ast.FindAll(
                     static node => node is CommandAst,
                     searchNestedScriptBlocks: true)
                 .Cast<CommandAst>())
        {
            if (!IsModuleManagementCommand(command, out bool moduleFilter))
                continue;

            var targets = GetModuleTargetExpressions(command, moduleFilter).ToArray();
            if (targets.Length == 0)
            {
                // Module-management commands can receive their name from pipeline input. With no target in the
                // command AST, the runtime module cannot be proven and every approved donor must
                // remain available to the generated module.
                blocked.UnionWith(approvedModuleSources.Keys);
                continue;
            }

            foreach (var target in targets)
            {
                foreach (var importedModule in GetStaticModuleNames(target))
                {
                    var pattern = new WildcardPattern(importedModule, WildcardOptions.IgnoreCase);
                    blocked.UnionWith(approvedModuleSources.Keys.Where(pattern.IsMatch));
                }

                if (!IsFullyStaticModuleReference(target))
                    blocked.UnionWith(approvedModuleSources.Keys);
            }
        }

        foreach (var typeName in ast.FindAll(
                     static node => node is TypeConstraintAst || node is TypeExpressionAst,
                     searchNestedScriptBlocks: true)
                 .Select(static node => node switch
                 {
                     TypeConstraintAst constraint => constraint.TypeName,
                     TypeExpressionAst expression => expression.TypeName,
                     _ => null
                 })
                 .Where(static typeName => typeName is not null)
                 .Cast<ITypeName>())
        {
            foreach (var candidateType in EnumerateTypeNames(typeName))
            {
                foreach (var source in approvedModuleSources.Values)
                {
                    if (TypeBelongsToApprovedModule(candidateType, source) ||
                        (typeOwnershipResolver?.Invoke(source, candidateType.FullName ?? string.Empty) ?? false))
                    {
                        blocked.Add(source.Name);
                    }
                }
            }
        }

        return blocked.ToArray();
    }

    private static IEnumerable<ITypeName> EnumerateTypeNames(ITypeName typeName)
    {
        yield return typeName;

        if (typeName is GenericTypeName generic)
        {
            foreach (var argument in generic.GenericArguments)
            foreach (var nested in EnumerateTypeNames(argument))
                yield return nested;
        }
        else if (typeName is ArrayTypeName array)
        {
            foreach (var nested in EnumerateTypeNames(array.ElementType))
                yield return nested;
        }
    }

    private static string? GetUsingModuleName(UsingStatementAst statement)
    {
        var simpleName = statement.Name?.Value?.Trim();
        if (!string.IsNullOrWhiteSpace(simpleName))
            return NormalizeModuleReferenceName(simpleName!);

        var match = Regex.Match(
            statement.Extent.Text ?? string.Empty,
            "(?is)\\bModuleName\\s*=\\s*['\\\"](?<name>[^'\\\"]+)['\\\"]",
            RegexOptions.CultureInvariant);
        return match.Success ? NormalizeModuleReferenceName(match.Groups["name"].Value) : null;
    }

    private static string? NormalizeModuleReferenceName(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;

        var trimmed = value.Trim().Trim('\'', '"');
        if (trimmed.IndexOf('/') >= 0 || trimmed.IndexOf('\\') >= 0 ||
            trimmed.EndsWith(".psd1", StringComparison.OrdinalIgnoreCase) ||
            trimmed.EndsWith(".psm1", StringComparison.OrdinalIgnoreCase))
        {
            return Path.GetFileNameWithoutExtension(trimmed.Replace('/', Path.DirectorySeparatorChar));
        }

        return trimmed;
    }

    private static bool IsModuleManagementCommand(CommandAst command, out bool moduleFilter)
    {
        moduleFilter = false;
        var commandName = command.GetCommandName();
        if (string.IsNullOrWhiteSpace(commandName))
            return false;

        var leafName = commandName!.Split('\\').Last();
        moduleFilter = (leafName.Equals("Get-Command", StringComparison.OrdinalIgnoreCase) ||
                        leafName.Equals("gcm", StringComparison.OrdinalIgnoreCase)) &&
                       (command.CommandElements.OfType<CommandParameterAst>().Any(parameter =>
                            "Module".StartsWith(parameter.ParameterName, StringComparison.OrdinalIgnoreCase)) ||
                        command.CommandElements.OfType<VariableExpressionAst>().Any(variable => variable.Splatted));
        return leafName.Equals("Import-Module", StringComparison.OrdinalIgnoreCase) ||
               leafName.Equals("ipmo", StringComparison.OrdinalIgnoreCase) ||
               leafName.Equals("Get-Module", StringComparison.OrdinalIgnoreCase) ||
               leafName.Equals("gmo", StringComparison.OrdinalIgnoreCase) ||
               leafName.Equals("Remove-Module", StringComparison.OrdinalIgnoreCase) ||
               leafName.Equals("rmo", StringComparison.OrdinalIgnoreCase) || moduleFilter;
    }

    private static IEnumerable<ExpressionAst> GetModuleTargetExpressions(CommandAst command, bool moduleFilter)
    {
        var positionalFound = false;
        for (var index = 1; index < command.CommandElements.Count; index++)
        {
            if (command.CommandElements[index] is not CommandParameterAst parameter)
            {
                if (!moduleFilter && !positionalFound && command.CommandElements[index] is ExpressionAst positional)
                {
                    yield return positional;
                    positionalFound = true;
                }
                continue;
            }

            var isModuleNameParameter = moduleFilter
                ? "Module".StartsWith(parameter.ParameterName, StringComparison.OrdinalIgnoreCase)
                : parameter.ParameterName.Equals("Name", StringComparison.OrdinalIgnoreCase) ||
                  parameter.ParameterName.Equals("FullyQualifiedName", StringComparison.OrdinalIgnoreCase);
            if (isModuleNameParameter && parameter.Argument is not null)
            {
                yield return parameter.Argument;
            }
            else if (isModuleNameParameter &&
                     index + 1 < command.CommandElements.Count &&
                     command.CommandElements[index + 1] is ExpressionAst argument)
            {
                yield return argument;
                index++;
            }
            else if (parameter.Argument is null &&
                     !ModuleManagementSwitchParameters.Contains(parameter.ParameterName) &&
                     index + 1 < command.CommandElements.Count &&
                     command.CommandElements[index + 1] is ExpressionAst)
            {
                // A value belonging to another named parameter is not the positional module name.
                index++;
            }
        }
    }

    internal static bool TryReadStaticModuleNames(StatementAst statement, out string[] names)
    {
        names = Array.Empty<string>();
        ExpressionAst? expression = GetHashtableValueExpression(statement);
        if (expression is ArrayExpressionAst emptyArray && emptyArray.SubExpression.Statements.Count == 0)
            return true;
        if (expression is null || !IsFullyStaticModuleReference(expression))
            return false;
        names = GetStaticModuleNames(expression).ToArray();
        return true;
    }

    private static bool IsFullyStaticModuleReference(ExpressionAst expression)
    {
        if (expression is StringConstantExpressionAst literal)
            return NormalizeModuleReferenceName(literal.Value) is not null;

        if (expression is ExpandableStringExpressionAst expandable)
            return expandable.NestedExpressions.Count == 0 &&
                   NormalizeModuleReferenceName(expandable.Value) is not null;

        if (expression is ArrayLiteralAst array)
            return array.Elements.Count > 0 && array.Elements.All(IsFullyStaticModuleReference);

        if (expression is ArrayExpressionAst arrayExpression)
        {
            var elements = arrayExpression.SubExpression.Statements
                .Select(GetHashtableValueExpression)
                .ToArray();
            return elements.Length > 0 &&
                   elements.All(static element => element is not null) &&
                   elements.Cast<ExpressionAst>().All(IsFullyStaticModuleReference);
        }

        if (expression is not HashtableAst hashtable)
            return false;

        foreach (var pair in hashtable.KeyValuePairs)
        {
            if (pair.Item1 is StringConstantExpressionAst key &&
                key.Value.Equals("ModuleName", StringComparison.OrdinalIgnoreCase))
            {
                return GetHashtableValueExpression(pair.Item2) is { } value &&
                       IsFullyStaticModuleReference(value);
            }
        }

        return false;
    }

    private static IEnumerable<string> GetStaticModuleNames(ExpressionAst expression)
    {
        if (expression is StringConstantExpressionAst literal)
        {
            var normalized = NormalizeModuleReferenceName(literal.Value);
            if (normalized is not null)
                yield return normalized;
            yield break;
        }

        if (expression is ExpandableStringExpressionAst expandable && expandable.NestedExpressions.Count == 0)
        {
            var normalized = NormalizeModuleReferenceName(expandable.Value);
            if (normalized is not null)
                yield return normalized;
            yield break;
        }

        if (expression is ArrayLiteralAst array)
        {
            foreach (var element in array.Elements)
            foreach (var name in GetStaticModuleNames(element))
                yield return name;
            yield break;
        }

        if (expression is ArrayExpressionAst arrayExpression)
        {
            foreach (var statement in arrayExpression.SubExpression.Statements)
            {
                if (GetHashtableValueExpression(statement) is not { } element)
                    continue;

                foreach (var name in GetStaticModuleNames(element))
                    yield return name;
            }
            yield break;
        }

        if (expression is not HashtableAst hashtable)
            yield break;

        foreach (var pair in hashtable.KeyValuePairs)
        {
            if (pair.Item1 is not StringConstantExpressionAst key ||
                !key.Value.Equals("ModuleName", StringComparison.OrdinalIgnoreCase) ||
                GetHashtableValueExpression(pair.Item2) is not { } value)
            {
                continue;
            }

            foreach (var name in GetStaticModuleNames(value))
                yield return name;
        }
    }

    private static ExpressionAst? GetHashtableValueExpression(StatementAst value)
    {
        if (value is not PipelineAst pipeline ||
            pipeline.PipelineElements.Count != 1 ||
            pipeline.PipelineElements[0] is not CommandExpressionAst expression)
        {
            return null;
        }

        return expression.Expression;
    }

    private static bool TypeBelongsToApprovedModule(ITypeName typeName, ApprovedModuleSource source)
    {
        var fullName = typeName.FullName ?? string.Empty;
        if (fullName.Equals(source.Name, StringComparison.OrdinalIgnoreCase) ||
            fullName.StartsWith(source.Name + ".", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        try
        {
            var type = typeName.GetReflectionType();
            var assemblyPath = type?.Assembly?.Location;
            if (string.IsNullOrWhiteSpace(assemblyPath) || string.IsNullOrWhiteSpace(source.ModuleBasePath))
                return false;

            var moduleRoot = Path.GetFullPath(source.ModuleBasePath)
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
            var resolvedAssembly = Path.GetFullPath(assemblyPath!);
            return resolvedAssembly.StartsWith(moduleRoot, StringComparison.OrdinalIgnoreCase);
        }
        catch
        {
            return false;
        }
    }

    private static string? GetModuleQualifier(string commandName)
    {
        if (string.IsNullOrWhiteSpace(commandName))
            return null;

        var separator = commandName.IndexOf('\\');
        return separator > 0 ? commandName.Substring(0, separator).Trim() : null;
    }
}
