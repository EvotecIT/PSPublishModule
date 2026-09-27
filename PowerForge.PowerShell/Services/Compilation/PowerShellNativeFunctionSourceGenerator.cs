using System.Text;

namespace PowerForge;

/// <summary>Connects emitted CLR bodies to the shared native function host without retaining authored bodies.</summary>
internal static class PowerShellNativeFunctionSourceGenerator
{
    internal static string FactoryType(PowerShellTypedCompilationResult typed)
        => PowerShellCSharpSymbolRenderer.Identifier(typed.TypeName + "NativeFunctions");

    internal static string FactoryMethod(PowerShellCompiledMethod method)
        => PowerShellCSharpSymbolRenderer.Identifier("Create_" + method.GeneratedName.TrimStart('@'));

    internal static string ExecutableFactoryMethod(PowerShellCompiledMethod method)
        => PowerShellCSharpSymbolRenderer.Identifier("CreateExecutable_" + method.GeneratedName.TrimStart('@'));

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
            AppendFactory(builder, typed, method, executable: false);
            AppendFactory(builder, typed, method, executable: true);
        }
        builder.AppendLine("}");
    }

    private static void AppendFactory(StringBuilder builder, PowerShellTypedCompilationResult typed,
        PowerShellCompiledMethod method, bool executable)
    {
        builder.Append("    public static global::System.Management.Automation.ScriptBlock ")
            .Append(executable ? ExecutableFactoryMethod(method) : FactoryMethod(method))
            .AppendLine(executable ? "(global::System.Management.Automation.SessionState sessionState, string sourcePath)" : "(global::System.Management.Automation.PSModuleInfo module, string sourcePath)")
            .AppendLine("    {");
        var binding = method.NativeFunctionBinding!;
        if (!binding.HasClean)
        {
            builder.Append("        return global::PowerForge.Generated.Runtime.PowerShellNativeFunctionHost.CreateOwnedDeclaration(")
                .Append(executable ? "sessionState" : "module").Append(", ")
                .Append(PowerShellCSharpLiteral.QuoteString(method.SourceName)).AppendLine(",")
                .Append("            ").Append(PowerShellCSharpLiteral.QuoteString(binding.ParameterDeclaration)).AppendLine(",")
                .Append("            new string[] { ").Append(string.Join(", ", binding.LocalNames.Select(PowerShellCSharpLiteral.QuoteString))).AppendLine(" },")
                .Append("            new string[] { ").Append(string.Join(", ", binding.LocalTypeDeclarations.Select(PowerShellCSharpLiteral.QuoteString))).AppendLine(" },")
                .Append("            ").Append(Callback(binding.HasBegin, 0)).AppendLine(",")
                .Append("            ").Append(Callback(binding.HasProcess, 1)).AppendLine(",")
                .Append("            ").Append(Callback(binding.HasEnd, 2)).AppendLine(",")
                .Append("            new string[] { ").Append(string.Join(", ", binding.FunctionTypeDeclarations.Select(PowerShellCSharpLiteral.QuoteString))).AppendLine(" });");
        }
        else
        {
            // Retain the existing clean-block host compatibility route: emitting a clean keyword
            // into a module declaration would prevent older PowerShell hosts from importing it.
            builder.Append(executable
                ? "        return global::PowerForge.Generated.Runtime.PowerShellNativeFunctionHost.Create(\n"
                : "        return global::PowerForge.Generated.Runtime.PowerShellNativeFunctionHost.Create(module,\n")
                .Append("            ").Append(PowerShellCSharpLiteral.QuoteString(binding.ParameterDeclaration))
                .Append(", sourcePath, new string[] { ")
                .Append(string.Join(", ", binding.LocalNames.Select(PowerShellCSharpLiteral.QuoteString)))
                .AppendLine(" },")
                .Append("            ").Append(Callback(binding.HasBegin, 0)).AppendLine(",")
                .Append("            ").Append(Callback(binding.HasProcess, 1)).AppendLine(",")
                .Append("            ").Append(Callback(binding.HasEnd, 2)).AppendLine(",")
                .Append("            ").Append(Callback(binding.HasClean, 3)).AppendLine(",")
                .Append("            localTypeDeclarations: new string[] { ")
                .Append(string.Join(", ", binding.LocalTypeDeclarations.Select(PowerShellCSharpLiteral.QuoteString)))
                .AppendLine(" });");
        }
        builder
                .AppendLine("            void Invoke(global::PowerForge.Generated.Runtime.PowerShellNativeFunctionContext context, int clause)")
                .AppendLine("            {");
            PowerShellNativeCallbackSource.AppendBody(builder, "                ",
                PowerShellCSharpSymbolRenderer.Identifier(typed.TypeName) + "." + method.GeneratedName, method.SourceName,
                method.RequiresPowerShellStatementErrors, method.RequiresPowerShellStopping, method.RequiresPowerShellStreams,
                method.ReturnType == typeof(void).FullName, method.OutputScalarization == "EnumerateCollection");
        builder.AppendLine("            }").AppendLine("    }");
    }

    private static string DeclarationBody(PowerShellNativeFunctionBinding binding)
        => binding.HasClean
            ? binding.ParameterDeclaration + "\nthrow 'Compiled function body was not installed.'"
            : global::PowerForge.Generated.Runtime.PowerShellNativeFunctionHost.CreateDeclarationSource(
                binding.ParameterDeclaration, null, binding.LocalNames.ToArray(),
                binding.HasBegin, binding.HasProcess, binding.HasEnd, false, binding.LocalTypeDeclarations.ToArray(), binding.FunctionTypeDeclarations.ToArray());

    private static string Callback(bool present, int clause)
        => PowerShellNativeCallbackSource.Callback(present, clause);

    internal static string Registration(PowerShellTypedCompilationResult typed, PowerShellCompiledMethod method, string declaration)
        => Registration(typed, method, declaration, executable: false);

    internal static string Registration(PowerShellTypedCompilationResult typed, PowerShellCompiledMethod method,
        string declaration, bool executable, bool executableDependency = false)
    {
        // A function declaration can share its line with another declaration or command.
        // Its replacement must provide statement separators on both sides, including aliases.
        var builder = new StringBuilder().AppendLine().Append(declaration).AppendLine(" {")
            .AppendLine(DeclarationBody(method.NativeFunctionBinding!))
            .AppendLine("}")
            .Append("if ([PowerForge.Generated.Runtime.PowerShellNativeFunctionHost]::InstallDeclaredFunction($ExecutionContext.SessionState")
            .Append(executable ? string.Empty : ".Module")
            .Append(", '");
        builder.Append(method.SourceName.Replace("'", "''"))
            .Append("', [").Append(typed.NamespaceName).Append('.').Append(FactoryType(typed))
            .Append("]::").Append(executable ? ExecutableFactoryMethod(method) : FactoryMethod(method))
            .Append(executable
                ? executableDependency ? "($ExecutionContext.SessionState, $PSCommandPath))) { }" : "($ExecutionContext.SessionState, [PowerForge.Compiled.PowerForgePackagedEntryPoint]::Path))) { }"
                : "($ExecutionContext.SessionState.Module, $PSCommandPath))) { }");
        return builder.AppendLine().ToString();
    }
}
