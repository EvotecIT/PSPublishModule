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
        if (cmdlet.Method.DeclaredOutputTypeIsSemanticContract &&
            !string.IsNullOrWhiteSpace(cmdlet.Method.DeclaredOutputType))
            builder.AppendLine($"[OutputType(typeof({GetGeneratedTypeName(cmdlet.Method.DeclaredOutputType)}))]");
        else if (!string.IsNullOrWhiteSpace(cmdlet.Method.DeclaredOutputType))
            builder.AppendLine($"[OutputType({PowerShellCSharpLiteral.QuoteString(cmdlet.Method.DeclaredOutputType)})]");
        else if (GetCmdletOutputTypeName(cmdlet.Method.ReturnType) is { } inferredOutputType)
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
            builder.AppendLine("    private void InvokePowerShellRegion(string script, object?[] arguments)");
            builder.AppendLine("    {");
            builder.AppendLine("        var runspaceId = global::System.Management.Automation.Runspaces.Runspace.DefaultRunspace?.InstanceId ?? global::System.Guid.Empty;");
            builder.AppendLine($"        var dispatcher = {GetRuntimeRegionHostTypeName(typed)}.GetDispatcher(runspaceId);");
            builder.AppendLine("        var values = dispatcher is null");
            builder.AppendLine("            ? InvokeCommand.InvokeScript(SessionState, ScriptBlock.Create(script), arguments)");
            builder.AppendLine("            : dispatcher.Invoke(script, arguments);");
            builder.AppendLine("        foreach (var value in values)");
            builder.AppendLine("            WriteObject(value, enumerateCollection: false);");
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.AppendLine("    private object? CapturePowerShellRegion(string script, object?[] arguments)");
            builder.AppendLine("    {");
            builder.AppendLine("        var runspaceId = global::System.Management.Automation.Runspaces.Runspace.DefaultRunspace?.InstanceId ?? global::System.Guid.Empty;");
            builder.AppendLine($"        var dispatcher = {GetRuntimeRegionHostTypeName(typed)}.GetDispatcher(runspaceId);");
            builder.AppendLine("        var values = dispatcher is null");
            builder.AppendLine("            ? InvokeCommand.InvokeScript(SessionState, ScriptBlock.Create(script), arguments)");
            builder.AppendLine("            : dispatcher.Invoke(script, arguments);");
            builder.AppendLine("        if (values.Count == 0) return null;");
            builder.AppendLine("        if (values.Count == 1) return NormalizeCapturedPowerShellValue(values[0]);");
            builder.AppendLine("        var captured = new object?[values.Count];");
            builder.AppendLine("        for (var index = 0; index < values.Count; index++)");
            builder.AppendLine("            captured[index] = NormalizeCapturedPowerShellValue(values[index]);");
            builder.AppendLine("        return captured;");
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.AppendLine("    private static object? NormalizeCapturedPowerShellValue(global::System.Management.Automation.PSObject? value)");
            builder.AppendLine("    {");
            builder.AppendLine("        if (value is null) return null;");
            builder.AppendLine("        var baseObject = value.BaseObject;");
            builder.AppendLine("        if (baseObject is null) return value;");
            builder.AppendLine("        if (baseObject is global::System.Management.Automation.PSCustomObject) return value;");
            builder.AppendLine("        var baseTypeName = baseObject.GetType().FullName;");
            builder.AppendLine("        if (value.TypeNames.Count > 0 && !global::System.String.Equals(value.TypeNames[0], baseTypeName, global::System.StringComparison.Ordinal)) return value;");
            builder.AppendLine("        foreach (var member in value.Members)");
            builder.AppendLine("        {");
            builder.AppendLine("            if (!member.IsInstance) continue;");
            builder.AppendLine("            if (member.MemberType != global::System.Management.Automation.PSMemberTypes.Property &&");
            builder.AppendLine("                member.MemberType != global::System.Management.Automation.PSMemberTypes.Method &&");
            builder.AppendLine("                member.MemberType != global::System.Management.Automation.PSMemberTypes.ParameterizedProperty &&");
            builder.AppendLine("                member.MemberType != global::System.Management.Automation.PSMemberTypes.Event)");
            builder.AppendLine("                return value;");
            builder.AppendLine("        }");
            builder.AppendLine("        return baseObject;");
            builder.AppendLine("    }");
            builder.AppendLine();
        }
        if (cmdlet.Method.RequiresPowerShellRuntimeState)
        {
            builder.AppendLine("    private global::System.Collections.Generic.IReadOnlyDictionary<string, object?> CaptureRuntimeState()");
            builder.AppendLine("    {");
            builder.AppendLine("        var values = new global::System.Collections.Generic.Dictionary<string, object?>(global::System.StringComparer.OrdinalIgnoreCase);");
            builder.AppendLine("        values[\"LanguageMode\"] = SessionState.LanguageMode;");
            foreach (var preference in new[] { "VerbosePreference", "DebugPreference", "WarningPreference", "InformationPreference", "ErrorActionPreference", "ProgressPreference", "ConfirmPreference" })
                builder.AppendLine($"        values[{PowerShellCSharpLiteral.QuoteString(preference)}] = SessionState.PSVariable.GetValue({PowerShellCSharpLiteral.QuoteString(preference)});");
            builder.AppendLine("        if (MyInvocation.BoundParameters.TryGetValue(\"Verbose\", out var verbose)) values[\"VerbosePreference\"] = global::System.Management.Automation.LanguagePrimitives.IsTrue(verbose) ? global::System.Management.Automation.ActionPreference.Continue : global::System.Management.Automation.ActionPreference.SilentlyContinue;");
            builder.AppendLine("        if (MyInvocation.BoundParameters.TryGetValue(\"Debug\", out var debug)) values[\"DebugPreference\"] = global::System.Management.Automation.LanguagePrimitives.IsTrue(debug) ? (typeof(global::System.Management.Automation.PSObject).Assembly.GetName().Version?.Major >= 7 ? global::System.Management.Automation.ActionPreference.Continue : global::System.Management.Automation.ActionPreference.Inquire) : global::System.Management.Automation.ActionPreference.SilentlyContinue;");
            foreach (var pair in new[] { ("WarningAction", "WarningPreference"), ("InformationAction", "InformationPreference"), ("ErrorAction", "ErrorActionPreference"), ("ProgressAction", "ProgressPreference") })
            {
                var localName = char.ToLowerInvariant(pair.Item1[0]) + pair.Item1.Substring(1);
                builder.AppendLine($"        if (MyInvocation.BoundParameters.TryGetValue({PowerShellCSharpLiteral.QuoteString(pair.Item1)}, out var {localName})) values[{PowerShellCSharpLiteral.QuoteString(pair.Item2)}] = {localName};");
            }
            builder.AppendLine("        if (MyInvocation.BoundParameters.TryGetValue(\"Confirm\", out var confirm)) values[\"ConfirmPreference\"] = global::System.Management.Automation.LanguagePrimitives.IsTrue(confirm) ? global::System.Management.Automation.ConfirmImpact.Low : global::System.Management.Automation.ConfirmImpact.None;");
            builder.AppendLine("        var errors = SessionState.PSVariable.GetValue(\"Error\") as global::System.Collections.ICollection;");
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
        if (cmdlet.Method.RequiresPowerShellStreams)
            arguments = arguments.Concat(new[]
            {
                "value => WriteObject(value, enumerateCollection: true)",
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
                "((global::System.Collections.IDictionary)SessionState.PSVariable.GetValue(\"PSVersionTable\"))[\"PSVersion\"]!",
                "MyInvocation.BoundParameters.ContainsKey(\"WhatIf\") ? global::System.Management.Automation.LanguagePrimitives.IsTrue(MyInvocation.BoundParameters[\"WhatIf\"]) : global::System.Management.Automation.LanguagePrimitives.IsTrue(SessionState.PSVariable.GetValue(\"WhatIfPreference\"))",
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
        builder.AppendLine("    }");
        builder.AppendLine("}");
        builder.AppendLine();
    }
}
