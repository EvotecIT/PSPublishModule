using System.Text;

namespace PowerForge;

/// <summary>Connects emitted CLR bodies to the shared native function host without retaining authored bodies.</summary>
internal static class PowerShellNativeFunctionSourceGenerator
{
    internal static string FactoryType(PowerShellTypedCompilationResult typed)
        => PowerShellCSharpSymbolRenderer.Identifier(typed.TypeName + "NativeFunctions");

    internal static string FactoryMethod(PowerShellCompiledMethod method)
        => PowerShellCSharpSymbolRenderer.Identifier("Create_" + method.GeneratedName.TrimStart('@'));

    internal static void Validate(PowerShellCompiledMethod method)
    {
        if (method.RequiresPowerShellRuntimeState ||
            method.RequiresPowerShellModuleStateRead || method.RequiresPowerShellModuleStateWrite ||
            method.RequiresProviderCancellation ||
            method.RequiresPowerShellBoundParameters)
            throw new InvalidOperationException($"Native function '{method.SourceName}' requires a host operation that has not been connected to the native invocation ABI.");
    }

    internal static void Append(StringBuilder builder, PowerShellTypedCompilationResult typed)
    {
        var functions = typed.Methods.Where(static method => method.NativeFunctionBinding is not null).ToArray();
        if (functions.Length == 0) return;
        builder.Append("public static class ").Append(FactoryType(typed)).AppendLine("\n{");
        foreach (var method in functions)
        {
            Validate(method);
            builder.Append("    public static global::System.Management.Automation.ScriptBlock ")
                .Append(FactoryMethod(method)).AppendLine("(global::System.Management.Automation.PSModuleInfo module, string sourcePath)")
                .AppendLine("    {")
                .AppendLine("        return global::PowerForge.Generated.Runtime.PowerShellNativeFunctionHost.Create(module,")
                .Append("            ").Append(PowerShellCSharpLiteral.QuoteString(method.NativeFunctionBinding!.ParameterDeclaration))
                .Append(", sourcePath, new string[] { ")
                .Append(string.Join(", ", method.NativeFunctionBinding.LocalNames.Select(PowerShellCSharpLiteral.QuoteString)))
                .AppendLine(" }, null, null, context =>")
                .AppendLine("            {");
            if (method.RequiresPowerShellStatementErrors || method.RequiresPowerShellStopping)
                builder.Append("                using var statementErrors = global::PowerForge.Generated.Runtime.PowerShellStatementErrorContext.CreateNativeFunction(context.FunctionContext, ")
                    .Append(PowerShellCSharpLiteral.QuoteString(method.SourceName)).AppendLine(");");
            var arguments = new List<string> { "context" };
            if (method.RequiresPowerShellStatementErrors) arguments.Add("statementErrors");
            if (method.RequiresPowerShellStopping) arguments.Add("statementErrors.CheckLoopInterrupts");
            if (method.RequiresPowerShellStreams)
                arguments.AddRange(new[] { "context.WriteValue", "context.WriteVerbose", "context.WriteDebug", "context.WriteWarning",
                    "context.WriteInformation", "context.WriteHost", "context.WriteError" });
            var call = PowerShellCSharpSymbolRenderer.Identifier(typed.TypeName) + "." + method.GeneratedName +
                "(" + string.Join(", ", arguments) + ")";
            if (method.ReturnType == typeof(void).FullName)
                builder.Append("                ").Append(call).AppendLine(";");
            else if (method.OutputScalarization == "EnumerateCollection")
                builder.Append("                foreach (var value in ").Append(call).AppendLine(") context.WriteValue(value);");
            else
                builder.Append("                context.WriteValue(").Append(call).AppendLine(");");
            builder.AppendLine("            });").AppendLine("    }");
        }
        builder.AppendLine("}");
    }

    internal static string Registration(PowerShellTypedCompilationResult typed, PowerShellCompiledMethod method)
    {
        var builder = new StringBuilder("Microsoft.PowerShell.Management\\Set-Item -LiteralPath '");
        builder.Append(("Function:\\local:" + method.SourceName).Replace("'", "''"))
            .Append("' -Value ([").Append(typed.NamespaceName).Append('.').Append(FactoryType(typed))
            .Append("]::").Append(FactoryMethod(method)).Append("($ExecutionContext.SessionState.Module, $PSCommandPath))");
        foreach (var alias in method.Aliases)
            builder.AppendLine().Append("Microsoft.PowerShell.Utility\\Set-Alias -Scope Local -Name '")
                .Append(alias.Replace("'", "''")).Append("' -Value '").Append(method.SourceName.Replace("'", "''")).Append("'");
        return builder.ToString();
    }
}
