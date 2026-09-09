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
                .AppendLine(" },")
                .Append("            ").Append(Callback(method.NativeFunctionBinding.HasBegin, 0)).AppendLine(",")
                .Append("            ").Append(Callback(method.NativeFunctionBinding.HasProcess, 1)).AppendLine(",")
                .Append("            ").Append(Callback(method.NativeFunctionBinding.HasEnd, 2)).AppendLine(",")
                .Append("            ").Append(Callback(method.NativeFunctionBinding.HasClean, 3)).AppendLine(",")
                .Append("            localTypeDeclarations: new string[] { ")
                .Append(string.Join(", ", method.NativeFunctionBinding.LocalTypeDeclarations.Select(PowerShellCSharpLiteral.QuoteString)))
                .AppendLine(" });")
                .AppendLine("            void Invoke(global::PowerForge.Generated.Runtime.PowerShellNativeFunctionContext context, int clause)")
                .AppendLine("            {");
            PowerShellNativeCallbackSource.AppendBody(builder, "                ",
                PowerShellCSharpSymbolRenderer.Identifier(typed.TypeName) + "." + method.GeneratedName, method.SourceName,
                method.RequiresPowerShellStatementErrors, method.RequiresPowerShellStopping, method.RequiresPowerShellStreams,
                method.ReturnType == typeof(void).FullName, method.OutputScalarization == "EnumerateCollection");
            builder.AppendLine("            }").AppendLine("    }");
        }
        builder.AppendLine("}");
    }

    private static string Callback(bool present, int clause)
        => PowerShellNativeCallbackSource.Callback(present, clause);

    internal static string Registration(PowerShellTypedCompilationResult typed, PowerShellCompiledMethod method, string declaration)
    {
        // A function declaration can share its line with another declaration or command.
        // Its replacement must provide statement separators on both sides, including aliases.
        var builder = new StringBuilder().AppendLine().Append(declaration).AppendLine(" {")
            .AppendLine(method.NativeFunctionBinding!.ParameterDeclaration)
            .AppendLine("throw 'Compiled function body was not installed.'")
            .AppendLine("}")
            .Append("if ([PowerForge.Generated.Runtime.PowerShellNativeFunctionHost]::InstallDeclaredFunction($ExecutionContext.SessionState.Module, '");
        builder.Append(method.SourceName.Replace("'", "''"))
            .Append("', [").Append(typed.NamespaceName).Append('.').Append(FactoryType(typed))
            .Append("]::").Append(FactoryMethod(method)).Append("($ExecutionContext.SessionState.Module, $PSCommandPath))) { }");
        return builder.AppendLine().ToString();
    }
}
