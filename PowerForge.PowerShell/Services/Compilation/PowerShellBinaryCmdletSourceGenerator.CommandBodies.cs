using System.Text;

namespace PowerForge;

internal static partial class PowerShellBinaryCmdletSourceGenerator
{
    private static void AppendCmdlet(
        StringBuilder builder,
        PowerShellTypedCompilationResult typed,
        CmdletDescriptor cmdlet,
        string? targetFramework)
    {
        ValidateDescriptor(cmdlet, targetFramework);

        if (cmdlet.Method.Aliases.Length > 0)
            builder.AppendLine($"[Alias({string.Join(", ", cmdlet.Method.Aliases.Select(PowerShellCSharpLiteral.QuoteString))})]");
        builder.AppendLine(GenerateCmdletAttribute(cmdlet));
        if (cmdlet.Method.OutputTypeDeclarations.Length > 0)
        {
            foreach (var declaration in cmdlet.Method.OutputTypeDeclarations)
            {
                var types = declaration.UseClrTypes
                    ? declaration.TypeNames.Select(name => $"typeof({GetGeneratedTypeName(name)})")
                    : declaration.TypeNames.Select(PowerShellCSharpLiteral.QuoteString);
                var parameterSet = string.IsNullOrEmpty(declaration.ParameterSetName)
                    ? string.Empty
                    : $", ParameterSetName = new string[] {{ {PowerShellCSharpLiteral.QuoteString(declaration.ParameterSetName)} }}";
                builder.AppendLine($"[OutputType({string.Join(", ", types)}{parameterSet})]");
            }
        }
        else if (cmdlet.Method.DeclaredOutputTypeIsSemanticContract &&
            !string.IsNullOrWhiteSpace(cmdlet.Method.DeclaredOutputType))
            builder.AppendLine($"[OutputType(typeof({GetGeneratedTypeName(cmdlet.Method.DeclaredOutputType)}))]");
        else if (!string.IsNullOrWhiteSpace(cmdlet.Method.DeclaredOutputType))
            builder.AppendLine($"[OutputType({PowerShellCSharpLiteral.QuoteString(cmdlet.Method.DeclaredOutputType)})]");
        else if ((!string.IsNullOrWhiteSpace(cmdlet.Method.SuccessOutputType) ? cmdlet.Method.SuccessOutputType :
            GetCmdletOutputTypeName(cmdlet.Method.ReturnType)) is { } inferredOutputType)
            builder.AppendLine($"[OutputType(typeof({GetGeneratedTypeName(inferredOutputType)}))]");
        var requiresProviderCancellation = cmdlet.Method.RequiresProviderCancellation;
        var implementsDisposable = requiresProviderCancellation ||
            cmdlet.Method.Lifecycle?.Execution == PowerShellCompilationLifecycleExecution.HostedSteppablePipeline;
        builder.AppendLine($"public sealed class {cmdlet.ClassName} : PSCmdlet{(implementsDisposable ? ", global::System.IDisposable" : string.Empty)}");
        builder.AppendLine("{");
        builder.AppendLine("    // Reserved storage for the authored CLR command name; finalized before artifact hashing and signing.");
        builder.AppendLine("    private const string " + GetCommandIdentityStorage(cmdlet.Method) + " = " + PowerShellCSharpLiteral.QuoteString(cmdlet.Method.SourceName) + ";");
        if (requiresProviderCancellation)
            AppendProviderCancellationMembers(builder);
        for (var index = 0; index < cmdlet.Method.Parameters.Length; index++)
        {
            var parameter = cmdlet.Method.Parameters[index];
            foreach (var binding in GetEffectiveBindings(cmdlet.Method, parameter, index))
                builder.AppendLine("    " + GenerateParameterAttribute(binding));
            if (parameter.Aliases.Length > 0)
                builder.AppendLine($"    [Alias({string.Join(", ", parameter.Aliases.Select(PowerShellCSharpLiteral.QuoteString))})]");
            var propertyType = parameter.IsSwitch ? "SwitchParameter" : GetGeneratedTypeName(parameter.TypeName);
            if (parameter.TypeName == typeof(string).FullName)
                builder.AppendLine($"    [global::PowerForge.Generated.Runtime.PowerShellStringParameterAttribute({(cmdlet.Method.IsAdvancedFunction ? "true" : "false")})]");
            if (RequiresInvariantParameterConversion(parameter))
                builder.AppendLine($"    [{InvariantParameterAttributeName}(typeof({propertyType}))]");
            if (parameter.AllowNull)
                builder.AppendLine("    [AllowNull]");
            if (parameter.AllowEmptyString)
                builder.AppendLine("    [AllowEmptyString]");
            if (parameter.AllowEmptyCollection)
                builder.AppendLine("    [AllowEmptyCollection]");
            if (parameter.SupportsWildcards)
                builder.AppendLine("    [SupportsWildcards]");
            foreach (var validation in parameter.Validations.Where(validation => ShouldGenerateValidationAttribute(parameter, validation)))
                builder.AppendLine("    " + GenerateValidationAttribute(validation));
            var initializer = parameter.IsSwitch || IsGeneratedValueType(parameter.TypeName)
                ? string.Empty
                : parameter.TypeName == typeof(string).FullName
                    ? " = string.Empty;"
                    : " = default!;";
            builder.AppendLine($"    public {propertyType} {PowerShellCSharpSymbolRenderer.Identifier(parameter.Name)} {{ get; set; }}{initializer}");
            builder.AppendLine();
        }
        if (!cmdlet.Method.IsAdvancedFunction)
        {
            builder.AppendLine("    [Parameter(ValueFromRemainingArguments = true, DontShow = true)]");
            builder.AppendLine($"    public object[] {RemainingArgumentsMemberName} {{ get; set; }} = global::System.Array.Empty<object>();");
            builder.AppendLine();
        }
        if (cmdlet.Method.Lifecycle?.Execution == PowerShellCompilationLifecycleExecution.HostedSteppablePipeline)
        {
            PowerShellHostedLifecycleSourceGenerator.AppendMembers(builder, cmdlet.Method);
            builder.AppendLine("}");
            builder.AppendLine();
            return;
        }
        if (cmdlet.Method.RequiresPowerShellCommandRegions)
        {
            builder.AppendLine("    private void InvokePowerShellRegion(global::PowerForge.Generated.Runtime.PowerShellStatementErrorContext context, string script, object?[] arguments, global::PowerForge.Generated.Runtime.PowerShellHostedRegionSource? source)");
            builder.AppendLine("    {");
            builder.AppendLine("        var block = source is null ? ScriptBlock.Create(script) : source.CreateScriptBlock(script);");
            builder.AppendLine("        global::PowerForge.Generated.Runtime.PowerShellStatementErrorContext.BindModule(this, block);");
            builder.AppendLine("        context.InvokeCommandRegion(block, arguments);");
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.AppendLine("    private object? CapturePowerShellRegion(global::PowerForge.Generated.Runtime.PowerShellStatementErrorContext context, string script, object?[] arguments, global::PowerForge.Generated.Runtime.PowerShellHostedRegionSource? source)");
            builder.AppendLine("    {");
            builder.AppendLine("        var block = source is null ? ScriptBlock.Create(script) : source.CreateScriptBlock(script);");
            builder.AppendLine("        global::PowerForge.Generated.Runtime.PowerShellStatementErrorContext.BindModule(this, block);");
            builder.AppendLine("        return context.CaptureCommandRegion(block, arguments);");
            builder.AppendLine("    }");
            builder.AppendLine();
        }
        if (cmdlet.Method.RequiresPowerShellRuntimeState)
        {
            builder.AppendLine("    private global::System.Collections.Generic.IReadOnlyDictionary<string, object?> CaptureRuntimeState()");
            builder.AppendLine("    {");
            builder.AppendLine("        var moduleState = global::PowerForge.Generated.Runtime.PowerShellModuleSessionState.Resolve(this);");
            builder.AppendLine("        var values = new global::System.Collections.Generic.Dictionary<string, object?>(global::System.StringComparer.OrdinalIgnoreCase);");
            builder.AppendLine("        values[\"LanguageMode\"] = moduleState.LanguageMode;");
            foreach (var preference in new[] { "VerbosePreference", "DebugPreference", "WarningPreference", "InformationPreference", "ErrorActionPreference", "ProgressPreference", "ConfirmPreference" })
                builder.AppendLine($"        values[{PowerShellCSharpLiteral.QuoteString(preference)}] = moduleState.PSVariable.GetValue({PowerShellCSharpLiteral.QuoteString(preference)});");
            builder.AppendLine("        var errors = moduleState.PSVariable.GetValue(\"Error\") as global::System.Collections.ICollection;");
            builder.AppendLine("        values[\"Error\"] = new global::System.Collections.ArrayList(errors ?? global::System.Array.Empty<object>());");
            builder.AppendLine("        return values;");
            builder.AppendLine("    }");
            builder.AppendLine();
        }
        if (cmdlet.Method.RequiresPowerShellModuleStateRead)
        {
            builder.AppendLine("    private object? ReadPowerShellModuleVariable(string name)");
            builder.AppendLine("    {");
            builder.AppendLine("        var runspaceId = global::System.Management.Automation.Runspaces.Runspace.DefaultRunspace?.InstanceId ?? global::System.Guid.Empty;");
            builder.AppendLine($"        var result = {GetRuntimeRegionHostTypeName(typed)}.ReadModuleVariable(runspaceId, name);");
            builder.AppendLine($"        if (result.Error is not null) {GetRuntimeRegionHostTypeName(typed)}.ThrowPowerShellModuleStateError(result.Error);");
            builder.AppendLine("        return result.Value;");
            builder.AppendLine("    }");
            builder.AppendLine();
        }
        if (cmdlet.Method.RequiresPowerShellModuleStateWrite)
        {
            builder.AppendLine("    private void WritePowerShellModuleVariable(string name, object? value)");
            builder.AppendLine("    {");
            builder.AppendLine("        var runspaceId = global::System.Management.Automation.Runspaces.Runspace.DefaultRunspace?.InstanceId ?? global::System.Guid.Empty;");
            builder.AppendLine($"        var result = {GetRuntimeRegionHostTypeName(typed)}.WriteModuleVariable(runspaceId, name, value);");
            builder.AppendLine($"        if (result.Error is not null) {GetRuntimeRegionHostTypeName(typed)}.ThrowPowerShellModuleStateError(result.Error);");
            builder.AppendLine("    }");
            builder.AppendLine();
        }
        var hasPipelineBinding = cmdlet.Method.Parameters.Any(static parameter => parameter.AcceptsPipelineInput);
        if (hasPipelineBinding)
        {
            builder.AppendLine("    protected override void BeginProcessing()");
            builder.AppendLine("        => global::PowerForge.Generated.Runtime.PowerShellCommandVariableScope.PrepareInputBinding(this);");
        }
        var lifecycleMethod = hasPipelineBinding
            ? "EndProcessing"
            : "ProcessRecord";
        builder.AppendLine($"    protected override void {lifecycleMethod}()");
        builder.AppendLine("    {");
        var requiresModuleState = PowerShellModuleSessionStatePolicy.RequiresState(cmdlet.Method);
        if (requiresModuleState)
        {
            builder.AppendLine("        using (global::PowerForge.Generated.Runtime.PowerShellStatementErrorContext.EnterModule(this))");
            builder.AppendLine("        {");
        }
        if (hasPipelineBinding && !cmdlet.Method.RequiresPowerShellStatementErrors)
            builder.AppendLine("        using var __commandVariables = global::PowerForge.Generated.Runtime.PowerShellCommandVariableScope.EnterClause(this);");
        var arguments = cmdlet.Method.Parameters.Select(parameter =>
            PowerShellCSharpSymbolRenderer.Identifier(parameter.Name) + (parameter.IsSwitch ? ".IsPresent" : string.Empty));
        if (cmdlet.Method.RequiresPowerShellStatementErrors)
        {
            arguments = arguments.Append("__statementErrors");
            builder.AppendLine("        var __statementErrors = new global::PowerForge.Generated.Runtime.PowerShellStatementErrorContext(this, " + PowerShellCSharpLiteral.QuoteString(cmdlet.Method.SourceName) + ");");
            builder.AppendLine("        try");
            builder.AppendLine("        {");
            builder.AppendLine("        try");
            builder.AppendLine("        {");
        }
        if (cmdlet.Method.RequiresPowerShellStopping)
            arguments = arguments.Append("global::PowerForge.Generated.Runtime.PowerShellStatementErrorContext.CreateLoopInterrupt(this)");
        if (cmdlet.Method.RequiresPowerShellStreams)
            arguments = arguments.Concat(new[]
            {
                cmdlet.Method.RequiresPowerShellStatementErrors ? "__statementErrors.WriteOutputRecord" : "value => WriteObject(value, enumerateCollection: false)",
                "WriteVerbose",
                "WriteDebug",
                "WriteWarning",
                "message => WriteInformation(new global::System.Management.Automation.InformationRecord(message, \"PowerForge.Compiled\"))",
                "message => { var hostMessage = new global::System.Management.Automation.HostInformationMessage { Message = message, NoNewLine = false }; var record = new global::System.Management.Automation.InformationRecord(hostMessage, \"Write-Host\"); record.Tags.Add(\"PSHOST\"); WriteInformation(record); }",
                "message => WriteError(new global::System.Management.Automation.ErrorRecord(new global::System.InvalidOperationException(message), \"PowerForge.CompiledCommandError\", global::System.Management.Automation.ErrorCategory.NotSpecified, null))"
            });
        if (requiresProviderCancellation)
            arguments = arguments.Append("_providerCancellation.Token");
        if (cmdlet.Method.RequiresPowerShellCommandRegions)
            arguments = arguments.Concat(new[] { "InvokePowerShellRegion", "CapturePowerShellRegion" });
        if (cmdlet.Method.RequiresPowerShellRuntimeState)
        {
            arguments = arguments.Concat(new[]
            {
                "target => ShouldProcess(target)",
                "(target, action) => ShouldProcess(target, action)",
                "((global::System.Collections.IDictionary)global::PowerForge.Generated.Runtime.PowerShellModuleSessionState.Resolve(this).PSVariable.GetValue(\"PSVersionTable\"))[\"PSVersion\"]!",
                "global::PowerForge.Generated.Runtime.PowerShellModuleSessionState.Resolve(this).PSVariable.GetValue(\"WhatIfPreference\")",
                "CaptureRuntimeState()"
            });
        }
        if (cmdlet.Method.RequiresPowerShellModuleStateRead)
            arguments = arguments.Append("ReadPowerShellModuleVariable");
        if (cmdlet.Method.RequiresPowerShellModuleStateWrite)
            arguments = arguments.Append("WritePowerShellModuleVariable");
        if (cmdlet.Method.RequiresPowerShellBoundParameters)
            arguments = arguments.Append("new global::System.Collections.Generic.HashSet<string>(MyInvocation.BoundParameters.Keys, global::System.StringComparer.OrdinalIgnoreCase)");
        var invocation = $"{typed.TypeName}.{cmdlet.Method.GeneratedName}({string.Join(", ", arguments)})";
        if (cmdlet.Method.RequiresPowerShellModuleState)
        {
            builder.AppendLine("        try");
            builder.AppendLine("        {");
        }
        if (requiresProviderCancellation)
            AppendProviderCancellationInvocation(
                builder,
                invocation,
                cmdlet.Method.ReturnType.Equals(typeof(void).FullName, StringComparison.Ordinal));
        else if (cmdlet.Method.ReturnType.Equals(typeof(void).FullName, StringComparison.Ordinal))
            builder.AppendLine($"        {invocation};");
        else
            builder.AppendLine($"        WriteObject({invocation}, enumerateCollection: true);");
        if (cmdlet.Method.RequiresPowerShellModuleState)
        {
            builder.AppendLine("        }");
            builder.AppendLine($"        catch (global::System.Exception exception) when ({GetRuntimeRegionHostTypeName(typed)}.TryTakePowerShellModuleStateError(exception, out var moduleStateError))");
            builder.AppendLine("        {");
            builder.AppendLine("            ThrowTerminatingError(moduleStateError);");
            builder.AppendLine("        }");
        }
        if (cmdlet.Method.RequiresPowerShellStatementErrors)
        {
            builder.AppendLine("        }");
            builder.AppendLine("        finally { __statementErrors.Dispose(); }");
            builder.AppendLine("        }");
            builder.AppendLine("        catch (global::System.Exception __statementError) when (global::PowerForge.Generated.Runtime.PowerShellStatementErrorContext.IsOperationFailure(__statementError))");
            builder.AppendLine("        {");
            builder.AppendLine("            throw __statementErrors.LeaveCommand(__statementError);");
            builder.AppendLine("        }");
        }
        if (requiresModuleState) builder.AppendLine("        }");
        builder.AppendLine("    }");
        builder.AppendLine("}");
        builder.AppendLine();
    }
}
