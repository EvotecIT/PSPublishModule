using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>
/// Defines the deliberately bounded, runtime-free begin/process/end lifecycle shape.
/// The executable or CLR library ABI receives the complete typed input collection so one generated
/// method can run begin once, process once per record, and an authored end once. Optional
/// switch arguments retain their source order and are available throughout the invocation. A bounded process
/// block may emit homogeneous top-level stable scalars; the compiler materializes those
/// values and the terminal end value in authored order.
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
        if (capabilities.HasFlag(PowerShellCompilationCapability.PowerShellHostTypes) ||
            !capabilities.HasFlag(PowerShellCompilationCapability.ExecutableParameterBinding) &&
            !capabilities.HasFlag(PowerShellCompilationCapability.ClrPipelineCollectionBinding))
        {
            reason = "Runtime-free pipeline lifecycle lowering requires a typed executable or CLR library collection ABI.";
            return false;
        }
        if (body.DynamicParamBlock is not null || GetCleanBlock(body) is not null ||
            body.BeginBlock is null || body.ProcessBlock is null)
        {
            reason = "Runtime-free pipeline lifecycle lowering requires begin and process blocks without dynamicparam or clean.";
            return false;
        }
        var parameters = PowerShellParameterSyntax.GetParameters(body).ToArray();
        var pipelineParameters = parameters.Where(candidate => PowerShellParameterContractBinder.GetBindings(candidate)
            .Any(static binding => binding.ValueFromPipeline || binding.ValueFromPipelineByPropertyName || binding.ValueFromRemainingArguments)).ToArray();
        if (pipelineParameters.Length != 1)
        {
            reason = "Runtime-free pipeline lifecycle lowering requires exactly one typed ValueFromPipeline parameter.";
            return false;
        }
        var candidate = pipelineParameters[0];
        if (parameters.Where(parameter => !ReferenceEquals(parameter, candidate)).Any(parameter =>
                parameter.StaticType != typeof(System.Management.Automation.SwitchParameter) ||
                parameter.DefaultValue is not null ||
                parameter.Attributes.OfType<AttributeAst>().Any(attribute =>
                    !PowerShellParameterContractBinder.IsAttributeNamed(attribute, "Parameter")) ||
                PowerShellParameterContractBinder.GetBindings(parameter).Any(static binding =>
                    binding.Mandatory || !string.IsNullOrWhiteSpace(binding.ParameterSetName))))
        {
            reason = "Additional runtime-free lifecycle parameters currently support optional all-parameter-sets switches without defaults or transforms.";
            return false;
        }
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
        if (bindings[0].Mandatory &&
            (!candidate.StaticType.IsValueType || Nullable.GetUnderlyingType(candidate.StaticType) is not null))
        {
            reason = "Mandatory nullable pipeline records require per-record PowerShell validation and error continuation.";
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
        if (body.EndBlock is { Statements.Count: > 0 } end &&
            end.Statements[end.Statements.Count - 1] is not PipelineAst and not ReturnStatementAst)
        {
            reason = "Runtime-free pipeline lifecycle lowering requires one terminal end-block success-output expression.";
            return false;
        }
        parameter = candidate;
        return true;
    }

    private static bool ReferencesVariable(NamedBlockAst? block, string name)
        => block?.FindAll(node => node is VariableExpressionAst variable &&
                variable.VariablePath.UserPath.Equals(name, StringComparison.OrdinalIgnoreCase), searchNestedScriptBlocks: false)
            .Any() == true;

    internal static bool RequiresNonNullCollection(Type elementType, PowerShellCompilationCapability capabilities)
        => capabilities.HasFlag(PowerShellCompilationCapability.ClrPipelineCollectionBinding) &&
           !PowerShellPipelineNullInputSemanticPolicy.CanBindNullRecord(elementType);

    private static NamedBlockAst? GetCleanBlock(ScriptBlockAst body)
        => body.GetType().GetProperty("CleanBlock")?.GetValue(body) as NamedBlockAst;
}
