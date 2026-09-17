using System;
using System.Linq;
using System.Management.Automation.Language;

namespace PowerForge;

internal sealed partial class NestedFunctionVisibilityAnalyzer
{
    private bool IsEscapingOrIsolatedScriptBlock(ScriptBlockExpressionAst scriptBlockExpression)
    {
        if (scriptBlockExpression.Parent is not CommandAst invocation)
            return true;

        if (invocation.InvocationOperator == TokenKind.Ampersand ||
            invocation.InvocationOperator == TokenKind.Dot)
        {
            if (IsInvocationTarget(scriptBlockExpression, invocation))
            {
                return false;
            }
        }

        var rawInvocationName = invocation.GetCommandName();
        var invocationName = NormalizeInvocationName(rawInvocationName);
        if (invocationName.Length == 0)
            return true;

        if (!HasTrustedModuleQualification(rawInvocationName, invocationName))
            return true;

        if (IsPotentiallyShadowedByScriptFunction(invocation, rawInvocationName, invocationName))
            return true;

        if (string.Equals(invocationName, "Start-Job", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(invocationName, "Start-ThreadJob", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(invocationName, "Start-RSJob", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(invocationName, "Register-ObjectEvent", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(invocationName, "Register-EngineEvent", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(invocationName, "Register-CimIndicationEvent", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(invocationName, "Register-WmiEvent", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (string.Equals(invocationName, "Invoke-Command", StringComparison.OrdinalIgnoreCase))
        {
            if (HasUnresolvedSplat(invocation))
                return true;

            var remoteBinding = TryHasAnyBoundParameter(
                invocation,
                "ComputerName",
                "ConnectionUri",
                "Session",
                "HostName",
                "SSHConnection",
                "VMId",
                "VMName",
                "ContainerId");
            if (remoteBinding.HasValue)
                return remoteBinding.Value;

            return HasAnyParameter(
                       invocation,
                       "ComputerName",
                       "ConnectionUri",
                       "Session",
                       "HostName",
                       "SSHConnection",
                       "VMId",
                       "VMName",
                       "ContainerId") ||
                   HasPositionalRemoteTarget(invocation);
        }

        if (string.Equals(invocationName, "ForEach-Object", StringComparison.OrdinalIgnoreCase))
        {
            if (HasUnresolvedSplat(invocation))
                return true;

            var parallelBinding = TryHasAnyBoundParameter(invocation, "Parallel");
            return parallelBinding ?? HasAnyParameter(invocation, "Parallel");
        }

        return !IsKnownSynchronousScriptBlockConsumer(invocationName);
    }

    private bool IsPotentiallyShadowedByScriptFunction(
        CommandAst invocation,
        string? rawInvocationName,
        string normalizedInvocationName)
    {
        if (!string.IsNullOrWhiteSpace(rawInvocationName) && rawInvocationName!.IndexOf('\\') >= 0)
            return false;

        if (!_functionDeclarationsByName.ContainsKey(normalizedInvocationName))
            return false;

        if (!_shadowResolutionInProgress.Add(invocation))
            return true;

        try
        {
            if (IsDeclaredInVisibleScope(invocation, normalizedInvocationName))
                return true;

            foreach (var declaration in _functionDeclarationsByName[normalizedInvocationName])
            {
                if (declaration.Extent.EndOffset > invocation.Extent.StartOffset ||
                    !TryGetPotentialDeclarationContext(
                        declaration,
                        out var declarationScope,
                        out var declarationBlock))
                {
                    continue;
                }

                if (IsDeclarationBlockCompatibleWithCommand(declarationBlock, invocation, declarationScope))
                    return true;
            }

            return false;
        }
        finally
        {
            _shadowResolutionInProgress.Remove(invocation);
        }
    }

    private static bool IsKnownSynchronousScriptBlockConsumer(string invocationName)
    {
        return string.Equals(invocationName, "Where-Object", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(invocationName, "Sort-Object", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(invocationName, "Group-Object", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(invocationName, "Measure-Object", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(invocationName, "Select-Object", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(invocationName, "Measure-Command", StringComparison.OrdinalIgnoreCase) ||
               string.Equals(invocationName, "Trace-Command", StringComparison.OrdinalIgnoreCase);
    }

    private static bool HasTrustedModuleQualification(string? rawInvocationName, string normalizedInvocationName)
    {
        if (string.IsNullOrWhiteSpace(rawInvocationName))
            return true;

        var separator = rawInvocationName!.LastIndexOf('\\');
        if (separator < 0)
            return true;

        var moduleName = rawInvocationName.Substring(0, separator);
        if (string.Equals(normalizedInvocationName, "ForEach-Object", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalizedInvocationName, "Where-Object", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalizedInvocationName, "Invoke-Command", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalizedInvocationName, "Start-Job", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(moduleName, "Microsoft.PowerShell.Core", StringComparison.OrdinalIgnoreCase);
        }

        if (string.Equals(normalizedInvocationName, "Sort-Object", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalizedInvocationName, "Group-Object", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalizedInvocationName, "Measure-Object", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalizedInvocationName, "Select-Object", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalizedInvocationName, "Measure-Command", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalizedInvocationName, "Trace-Command", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalizedInvocationName, "Register-ObjectEvent", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalizedInvocationName, "Register-EngineEvent", StringComparison.OrdinalIgnoreCase))
        {
            return string.Equals(moduleName, "Microsoft.PowerShell.Utility", StringComparison.OrdinalIgnoreCase);
        }

        if (string.Equals(normalizedInvocationName, "Register-WmiEvent", StringComparison.OrdinalIgnoreCase))
            return string.Equals(moduleName, "Microsoft.PowerShell.Management", StringComparison.OrdinalIgnoreCase);
        if (string.Equals(normalizedInvocationName, "Register-CimIndicationEvent", StringComparison.OrdinalIgnoreCase))
            return string.Equals(moduleName, "CimCmdlets", StringComparison.OrdinalIgnoreCase);
        if (string.Equals(normalizedInvocationName, "Start-ThreadJob", StringComparison.OrdinalIgnoreCase))
            return string.Equals(moduleName, "ThreadJob", StringComparison.OrdinalIgnoreCase);
        if (string.Equals(normalizedInvocationName, "Start-RSJob", StringComparison.OrdinalIgnoreCase))
            return string.Equals(moduleName, "PoshRSJob", StringComparison.OrdinalIgnoreCase);

        return false;
    }

    private static bool HasUnresolvedSplat(CommandAst invocation)
    {
        return invocation.CommandElements
            .OfType<VariableExpressionAst>()
            .Any(variable => variable.Splatted);
    }

    private static string NormalizeInvocationName(string? invocationName)
    {
        if (string.IsNullOrWhiteSpace(invocationName))
            return string.Empty;

        var normalized = invocationName!.Trim();
        var moduleSeparator = normalized.LastIndexOf('\\');
        if (moduleSeparator >= 0 && moduleSeparator + 1 < normalized.Length)
            normalized = normalized.Substring(moduleSeparator + 1);

        if (string.Equals(normalized, "sajb", StringComparison.OrdinalIgnoreCase))
            return "Start-Job";
        if (string.Equals(normalized, "icm", StringComparison.OrdinalIgnoreCase))
            return "Invoke-Command";
        if (string.Equals(normalized, "%", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "foreach", StringComparison.OrdinalIgnoreCase))
        {
            return "ForEach-Object";
        }
        if (string.Equals(normalized, "?", StringComparison.OrdinalIgnoreCase) ||
            string.Equals(normalized, "where", StringComparison.OrdinalIgnoreCase))
        {
            return "Where-Object";
        }
        if (string.Equals(normalized, "sort", StringComparison.OrdinalIgnoreCase))
            return "Sort-Object";
        if (string.Equals(normalized, "group", StringComparison.OrdinalIgnoreCase))
            return "Group-Object";
        if (string.Equals(normalized, "measure", StringComparison.OrdinalIgnoreCase))
            return "Measure-Object";
        if (string.Equals(normalized, "select", StringComparison.OrdinalIgnoreCase))
            return "Select-Object";

        return normalized;
    }

    private static bool HasAnyParameter(CommandAst invocation, params string[] parameterNames)
    {
        foreach (var parameter in invocation.CommandElements.OfType<CommandParameterAst>())
        {
            var actualName = parameter.ParameterName;
            if (string.IsNullOrWhiteSpace(actualName))
                continue;

            if (parameterNames.Any(name => name.StartsWith(actualName, StringComparison.OrdinalIgnoreCase)))
                return true;
        }

        return false;
    }

    private static bool? TryHasAnyBoundParameter(CommandAst invocation, params string[] parameterNames)
    {
        try
        {
            var binding = StaticParameterBinder.BindCommand(invocation);
            if (binding.BoundParameters.Keys.Any(
                    key => parameterNames.Any(name => string.Equals(name, key, StringComparison.OrdinalIgnoreCase))))
            {
                return true;
            }

            if (binding.BindingExceptions.Count == 0)
                return false;
        }
        catch
        {
            // Fall back to AST-only matching when command metadata is unavailable.
        }

        return null;
    }

    private static bool HasPositionalRemoteTarget(CommandAst invocation)
    {
        foreach (var element in invocation.CommandElements.Skip(1))
        {
            if (element is CommandParameterAst)
                continue;

            if (element is ScriptBlockExpressionAst)
                return false;

            return element is StringConstantExpressionAst ||
                   element is ExpandableStringExpressionAst ||
                   element is ArrayLiteralAst ||
                   element is ArrayExpressionAst;
        }

        return false;
    }
}
