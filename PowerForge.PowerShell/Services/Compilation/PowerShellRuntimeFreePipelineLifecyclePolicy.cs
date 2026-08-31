using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>
/// Defines the deliberately bounded, runtime-free begin/process/end lifecycle shape.
/// The executable ABI receives the complete typed input collection so one generated
/// method can run begin once, process once per record, and end once.
/// </summary>
internal static class PowerShellRuntimeFreePipelineLifecyclePolicy
{
    internal static bool HasNamedLifecycle(ScriptBlockAst body)
        => body.BeginBlock is not null || body.ProcessBlock is not null || GetCleanBlock(body) is not null;

    internal static bool TryGetPipelineParameter(
        ScriptBlockAst body,
        PowerShellCompilationCapability capabilities,
        out ParameterAst parameter,
        out string reason)
    {
        parameter = null!;
        reason = string.Empty;
        if (!HasNamedLifecycle(body)) return false;
        if (!capabilities.HasFlag(PowerShellCompilationCapability.ExecutableParameterBinding))
        {
            reason = "Runtime-free pipeline lifecycle lowering currently requires the typed executable collection ABI.";
            return false;
        }
        if (body.DynamicParamBlock is not null || GetCleanBlock(body) is not null ||
            body.BeginBlock is null || body.ProcessBlock is null || body.EndBlock is null)
        {
            reason = "Runtime-free pipeline lifecycle lowering requires explicit begin, process, and end blocks without dynamicparam or clean.";
            return false;
        }
        var parameters = body.ParamBlock?.Parameters.ToArray() ?? Array.Empty<ParameterAst>();
        if (parameters.Length != 1)
        {
            reason = "Runtime-free pipeline lifecycle lowering currently requires exactly one typed ValueFromPipeline parameter.";
            return false;
        }
        var candidate = parameters[0];
        var bindings = PowerShellParameterContractBinder.GetBindings(candidate);
        if (bindings.Length != 1 || !bindings[0].ValueFromPipeline ||
            bindings[0].ValueFromPipelineByPropertyName || bindings[0].ValueFromRemainingArguments ||
            !string.IsNullOrWhiteSpace(bindings[0].ParameterSetName))
        {
            reason = "Runtime-free pipeline lifecycle lowering requires one all-parameter-sets ValueFromPipeline binding by value.";
            return false;
        }
        if (candidate.DefaultValue is not null ||
            candidate.StaticType == typeof(object) ||
            candidate.StaticType == typeof(System.Management.Automation.SwitchParameter) ||
            !candidate.Attributes.OfType<TypeConstraintAst>().Any() ||
            !PowerShellStableScalarTypePolicy.IsSupported(candidate.StaticType))
        {
            reason = "Runtime-free pipeline lifecycle lowering requires one explicitly typed stable scalar pipeline parameter without a default value.";
            return false;
        }
        if (candidate.Attributes.OfType<AttributeAst>().Any(attribute =>
                !PowerShellParameterContractBinder.IsAttributeNamed(attribute, "Parameter")))
        {
            reason = "Runtime-free pipeline lifecycle lowering does not yet apply aliases, validation, or other parameter transforms per pipeline record.";
            return false;
        }
        var parameterName = candidate.Name.VariablePath.UserPath;
        if (ReferencesVariable(body.BeginBlock, parameterName) || ReferencesVariable(body.EndBlock, parameterName))
        {
            reason = "The runtime-free pipeline parameter is available only during process; begin and end cannot observe its stale or unbound value.";
            return false;
        }
        if (body.ProcessBlock.FindAll(static node => node is ReturnStatementAst or BreakStatementAst or ContinueStatementAst or ExitStatementAst or TrapStatementAst, searchNestedScriptBlocks: false).Any())
        {
            reason = "Runtime-free process blocks do not yet support return, exit, trap, break, or continue lifecycle control flow.";
            return false;
        }
        if (body.EndBlock.Statements.Count == 0 ||
            body.EndBlock.Statements[body.EndBlock.Statements.Count - 1] is not PipelineAst and not ReturnStatementAst)
        {
            reason = "Runtime-free pipeline lifecycle lowering requires one terminal end-block success-output expression.";
            return false;
        }
        parameter = candidate;
        return true;
    }

    private static bool ReferencesVariable(NamedBlockAst block, string name)
        => block.FindAll(node => node is VariableExpressionAst variable &&
                variable.VariablePath.UserPath.Equals(name, StringComparison.OrdinalIgnoreCase), searchNestedScriptBlocks: false)
            .Any();

    private static NamedBlockAst? GetCleanBlock(ScriptBlockAst body)
        => body.GetType().GetProperty("CleanBlock")?.GetValue(body) as NamedBlockAst;
}
