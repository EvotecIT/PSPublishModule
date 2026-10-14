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
            .AppendLine("    public static global::System.Management.Automation.ExternalScriptInfo Create(global::System.Management.Automation.Runspaces.Runspace runspace, string path)")
            .AppendLine("    {")
            .Append("        return global::PowerForge.Generated.Runtime.PowerShellNativeFunctionHost.CreateScriptEntry(runspace, path, ")
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
           "                var value = argument.Name is not null && SwitchParameters.Contains(argument.Name) && argument.Value is null\n" +
           "                    ? (object)global::System.Management.Automation.SwitchParameter.Present : argument.Value;\n" +
           "                arguments.Add(new System.Management.Automation.Runspaces.CommandParameter(argument.Name, value));\n" +
           "            }\n" +
           "            var entry = global::PowerForge.Compiled.CompiledNativeScriptEntry.Create(runspace, PowerForgePackagedEntryPoint.Path);\n" +
           "            using var entryLookup = global::PowerForge.Generated.Runtime.PowerShellNativeFunctionHost.AddScriptEntryCommand(powerShell, entry, arguments);";
}
