using System.Security.Cryptography;
using System.Text;

namespace PowerForge;

/// <summary>Emits a source-locked factory and launcher wiring for a native script root.</summary>
internal static class PowerShellHybridNativeEntryEmitter
{
    private const string NamespaceName = "PowerForge.Compiled";
    private const string TypeName = "CompiledNativeScriptEntry";
    internal const string DeclaringTypeName = NamespaceName + "." + TypeName;

    internal static string GenerateSource(PowerShellTypedExecutableCompilation compiled, ParsedSourceDocument document)
    {
        var method = compiled.EntryPointMethod;
        if (method.NativeFunctionBinding is null)
            throw new InvalidOperationException("Native entry emission requires native script binding.");
        string sourceHash;
        using (var sha = SHA256.Create())
            sourceHash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(document.Text)))
                .Replace("-", string.Empty).ToLowerInvariant();
        var builder = new StringBuilder("#nullable enable\nnamespace " + NamespaceName + ";\npublic static class " + TypeName + "\n{\n")
            .AppendLine(string.Join(Environment.NewLine, PowerShellCSharpSourceDocumentPool.Declarations(new[] { method })))
            .AppendLine(method.Source);
        foreach (var owner in new[] { "global::System.Management.Automation.Runspaces.Runspace", "global::System.Management.Automation.SessionState" })
        {
            builder.Append("    public static global::System.Management.Automation.ExternalScriptInfo Create(")
            .Append(owner).AppendLine(" owner, string path)")
            .AppendLine("    {")
            .Append("        return global::PowerForge.Generated.Runtime.PowerShellNativeFunctionHost.CreateScriptEntry(owner, path, ")
            .Append(PowerShellCSharpLiteral.QuoteString(sourceHash)).AppendLine(",")
            .Append("            ").Append(method.NativeFunctionBinding.HasBegin ? "context => InvokeClause(context, 0)" : "null").AppendLine(",")
            .Append("            ").Append(method.NativeFunctionBinding.HasProcess ? "context => InvokeClause(context, 1)" : "null").AppendLine(",")
            .Append("            ").Append(method.NativeFunctionBinding.HasEnd ? "context => InvokeClause(context, 2)" : "null").AppendLine(");")
            .AppendLine("        static void InvokeClause(global::PowerForge.Generated.Runtime.PowerShellNativeFunctionContext context, int clause)")
            .AppendLine("        {");
        PowerShellNativeCallbackSource.AppendBody(builder, "            ", method.GeneratedName, "<script>",
            method.RequiresPowerShellStatementErrors, method.RequiresPowerShellStopping,
            method.RequiresPowerShellStreams, method.ReturnType == typeof(void), method.ReturnType.IsArray);
            builder.AppendLine("        }\n    }");
        }
        return builder.AppendLine("}").ToString();
    }

    internal static string LauncherInvocation()
        => "            var arguments = ParseArguments(args).ToArray();\n" +
           "            var entry = global::PowerForge.Compiled.CompiledNativeScriptEntry.Create(runspace, PowerForgePackagedEntryPoint.Path);\n" +
           "            using var entryLookup = global::PowerForge.Generated.Runtime.PowerShellNativeFunctionHost.AddScriptEntryCommand(powerShell, entry, arguments);";
}
