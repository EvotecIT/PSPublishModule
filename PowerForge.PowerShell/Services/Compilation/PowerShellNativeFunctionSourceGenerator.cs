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
        if (method.NativeFunctionBinding is { HasClean: true, DynamicParameterDeclaration: not null })
            throw new InvalidOperationException("Dynamic discovery does not qualify the clean compatibility route.");
        if (method.IsModuleInitializer && method.NativeFunctionBinding is { UsesDeferredTypeBinding: true })
            throw new InvalidOperationException("A compiled module initializer requires a native script factory.");
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
        if (functions.Any(static method => method.NativeFunctionBinding!.UsesDeferredTypeBinding))
            builder.AppendLine("    public static global::System.Management.Automation.ErrorRecord TakeDeferredClauseError(global::System.Management.Automation.EngineIntrinsics engine)")
                .AppendLine("        => global::PowerForge.Generated.Runtime.PowerShellNativeFunctionHost.TakeDeferredClauseError(engine);");
        foreach (var method in functions)
        {
            Validate(method);
            if (method.NativeFunctionBinding!.UsesDeferredTypeBinding)
            {
                AppendDeferredBridge(builder, typed, method);
                continue;
            }
            AppendFactory(builder, typed, method, executable: false);
            if (!method.IsModuleInitializer)
                AppendFactory(builder, typed, method, executable: true);
        }
        builder.AppendLine("}");
    }

    private static string DeferredMethod(PowerShellCompiledMethod method)
        => PowerShellCSharpSymbolRenderer.Identifier("InvokeDeferred_" + method.GeneratedName.TrimStart('@'));

    private static void AppendDeferredBridge(StringBuilder builder, PowerShellTypedCompilationResult typed, PowerShellCompiledMethod method)
    {
        builder.Append("    public static bool ").Append(DeferredMethod(method))
            .AppendLine("(global::System.Management.Automation.EngineIntrinsics engine, int clause)")
            .AppendLine("    {")
            .Append("        return global::PowerForge.Generated.Runtime.PowerShellNativeFunctionHost.InvokeDeferredTypeClause(engine, ")
            .Append(PowerShellCSharpLiteral.QuoteString(method.SourceName)).Append(", ")
            .Append(PowerShellCSharpLiteral.QuoteString(DeferredDeclarationBody(typed, method))).AppendLine(", context =>")
            .AppendLine("        {");
        PowerShellNativeCallbackSource.AppendBody(builder, "            ",
            PowerShellCSharpSymbolRenderer.Identifier(typed.TypeName) + "." + method.GeneratedName, method.SourceName,
            method.RequiresPowerShellStatementErrors, method.RequiresPowerShellStopping, method.RequiresPowerShellStreams,
            method.ReturnType == typeof(void).FullName, method.OutputScalarization == "EnumerateCollection");
        builder.AppendLine("        });").AppendLine("    }");
    }

    private static void AppendFactory(StringBuilder builder, PowerShellTypedCompilationResult typed,
        PowerShellCompiledMethod method, bool executable)
    {
        builder.Append("    public static global::System.Management.Automation.ScriptBlock ")
            .Append(executable ? ExecutableFactoryMethod(method) : FactoryMethod(method))
            .Append(executable ? "(global::System.Management.Automation.SessionState sessionState, string sourcePath" : "(global::System.Management.Automation.PSModuleInfo module, string sourcePath")
            .AppendLine(method.IsModuleInitializer ? ", global::System.Management.Automation.InvocationInfo moduleInvocation, object boundParameters)" : ")")
            .AppendLine("    {");
        var binding = method.NativeFunctionBinding!;
        if (!binding.HasClean && !method.IsModuleInitializer)
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
                .Append("            new string[] { ").Append(string.Join(", ", binding.FunctionTypeDeclarations.Select(PowerShellCSharpLiteral.QuoteString))).Append(" }, ")
                .Append(binding.DynamicParameterDeclaration is null ? "null" : PowerShellCSharpLiteral.QuoteString(binding.DynamicParameterDeclaration)).AppendLine(");");
        }
        else
        {
            // Initializers have no function declaration. Clean blocks also use a standalone
            // block so older PowerShell hosts can still parse and import the module.
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
            if (method.NativeModuleInitializerSource is { } authoredSource)
                builder.Append("                context.SetModuleInvocation(moduleInvocation, boundParameters, ")
                    .Append(PowerShellCSharpLiteral.QuoteString(authoredSource)).AppendLine(");");
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
                binding.HasBegin, binding.HasProcess, binding.HasEnd, false, binding.LocalTypeDeclarations.ToArray(), binding.FunctionTypeDeclarations.ToArray(),
                binding.DynamicParameterDeclaration);

    private static string DeferredDeclarationBody(PowerShellTypedCompilationResult typed, PowerShellCompiledMethod method)
    {
        var body = DeclarationBody(method.NativeFunctionBinding!);
        var factory = "[" + typed.NamespaceName + "." + FactoryType(typed) + "]";
        return global::PowerForge.Generated.Runtime.PowerShellNativeFunctionHost.BindDeferredClauses(body,
            factory + "::" + DeferredMethod(method), factory + "::TakeDeferredClauseError");
    }

    private static string Callback(bool present, int clause)
        => PowerShellNativeCallbackSource.Callback(present, clause);

    internal static string Registration(PowerShellTypedCompilationResult typed, PowerShellCompiledMethod method, string declaration)
        => Registration(typed, method, declaration, executable: false);

    internal static string Registration(PowerShellTypedCompilationResult typed, PowerShellCompiledMethod method,
        string declaration, bool executable, bool executableDependency = false)
    {
        if (method.NativeFunctionBinding!.UsesDeferredTypeBinding)
        {
            var body = DeferredDeclarationBody(typed, method);
            return "\n" + declaration + " {\n" + body + "\n}\n";
        }
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
