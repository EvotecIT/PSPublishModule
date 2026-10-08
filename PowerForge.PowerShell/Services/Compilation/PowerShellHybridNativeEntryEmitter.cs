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
        => "            var named = new Hashtable(StringComparer.OrdinalIgnoreCase);\n" +
           "            var positional = new List<object?>();\n" +
           "            foreach (var argument in ParseArguments(args))\n" +
           "            {\n" +
           "                if (argument.Name is null) positional.Add(argument.Value);\n" +
           "                else named.Add(argument.Name, argument.Value ?? new SwitchParameter(true));\n" +
           "            }\n" +
           "            powerShell.AddScript(\"param($Named,$Positional) try { $entry = [PowerForge.Compiled.CompiledNativeScriptEntry]::Create($ExecutionContext.SessionState,[PowerForge.Compiled.PowerForgePackagedEntryPoint]::Path); & $entry @Named @Positional } catch { throw }\", useLocalScope: false)\n" +
           "                .AddParameter(\"Named\", named).AddParameter(\"Positional\", positional.ToArray());";
}
