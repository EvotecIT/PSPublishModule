using System.Security.Cryptography;
using System.Text;

namespace PowerForge;

/// <summary>Emits a source-locked factory and launcher wiring for a native script root.</summary>
internal static class PowerShellHybridNativeEntryEmitter
{
    internal static string GenerateSource(PowerShellTypedExecutableCompilation compiled, string sourcePath)
    {
        var method = compiled.EntryPointMethod;
        if (method.NativeFunctionBinding is null)
            throw new InvalidOperationException("Native entry emission requires native script binding.");
        string sourceHash;
        using (var sha = SHA256.Create())
            sourceHash = BitConverter.ToString(sha.ComputeHash(Encoding.UTF8.GetBytes(File.ReadAllText(sourcePath))))
                .Replace("-", string.Empty).ToLowerInvariant();
        var builder = new StringBuilder("#nullable enable\nnamespace PowerForge.Compiled;\npublic static class CompiledNativeScriptEntry\n{\n")
            .AppendLine(method.Source)
            .AppendLine("    public static global::System.Management.Automation.ExternalScriptInfo Create(global::System.Management.Automation.SessionState session, string path)")
            .AppendLine("    {")
            .Append("        return global::PowerForge.Generated.Runtime.PowerShellNativeFunctionHost.CreateScriptEntry(session, path, ")
            .Append(PowerShellCSharpLiteral.QuoteString(sourceHash)).AppendLine(", context =>")
            .AppendLine("        {")
            .AppendLine("            var clause = 2;");
        PowerShellNativeCallbackSource.AppendBody(builder, "            ", method.GeneratedName, "<script>",
            method.RequiresPowerShellStatementErrors, method.RequiresPowerShellStopping,
            method.RequiresPowerShellStreams, method.ReturnType == typeof(void), method.ReturnType.IsArray);
        return builder.AppendLine("        });\n    }\n}").ToString();
    }

    internal static string LauncherInvocation()
        => "            var arguments = new List<System.Management.Automation.Runspaces.CommandParameter>();\n" +
           "            foreach (var argument in ParseArguments(args))\n" +
           "            {\n" +
           "                arguments.Add(new System.Management.Automation.Runspaces.CommandParameter(argument.Name, argument.Value));\n" +
           "            }\n" +
           "            var invocation = global::PowerForge.Generated.Runtime.PowerShellNativeFunctionHost.CreateScriptEntryInvocation(arguments, SwitchParameters, out var values);\n" +
           "            powerShell.AddScript(\"param($Invocation,$Values) try { $entry = [PowerForge.Compiled.CompiledNativeScriptEntry]::Create($ExecutionContext.SessionState,[PowerForge.Compiled.PowerForgePackagedEntryPoint]::Path); & $Invocation $entry $Values } catch { throw }\", useLocalScope: false)\n" +
           "                .AddParameter(\"Invocation\", invocation).AddParameter(\"Values\", values);";
}
