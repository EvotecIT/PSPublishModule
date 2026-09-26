using System.Management.Automation.Language;
using System.Text;

namespace PowerForge;

/// <summary>Emits the private command that enters one already-bound Hybrid script body.</summary>
internal static class PowerShellHybridExecutableEntryEmitter
{
    internal static string GenerateSource(PowerShellTypedExecutableCompilation compiled)
    {
        var method = compiled.EntryPointMethod;
        var parameters = compiled.EntryPoint.Parameters;
        var arguments = string.Join(", ", parameters.Select((_, index) => $"GetStringValue({index})")
            .Concat(new[] { "context", "InvokeRegion", "CaptureRegion" }));
        var source = new StringBuilder()
            .AppendLine("#nullable enable")
            .AppendLine("using System;")
            .AppendLine("using System.Management.Automation;")
            .AppendLine("namespace PowerForge.Compiled;")
            .AppendLine("internal static class CompiledPowerShellEntry")
            .AppendLine("{")
            .AppendLine(method.Source)
            .AppendLine("}")
            .AppendLine("[Cmdlet(\"Invoke\", \"PowerForgeCompiledEntry\")]")
            .AppendLine("public sealed class PowerForgeCompiledEntryCommand : PSCmdlet")
            .AppendLine("{")
            .AppendLine("    [Parameter]")
            .AppendLine("    [AllowEmptyCollection]")
            .AppendLine("    public object?[] Values { get; set; } = Array.Empty<object?>();")
            .AppendLine("    protected override void ProcessRecord()")
            .AppendLine("    {")
            .Append("        if (Values.Length != ").Append(parameters.Length).AppendLine(")")
            .AppendLine("            throw new InvalidOperationException(\"The private compiled entry frame has an invalid parameter count.\");")
            .AppendLine("        using var context = new global::PowerForge.Generated.Runtime.PowerShellStatementErrorContext(this, \"<script>\");")
            .AppendLine("        using (global::PowerForge.Generated.Runtime.PowerShellStatementErrorContext.EnterModule(this))")
            .AppendLine("        {")
            .AppendLine("            try")
            .AppendLine("            {")
            .Append("                CompiledPowerShellEntry.").Append(method.GeneratedName).Append('(').Append(arguments).AppendLine(");")
            .AppendLine("            }")
            .AppendLine("            catch (Exception error) when (global::PowerForge.Generated.Runtime.PowerShellStatementErrorContext.IsOperationFailure(error))")
            .AppendLine("            {")
            .AppendLine("                throw context.LeaveCommand(error);")
            .AppendLine("            }")
            .AppendLine("        }")
            .AppendLine("    }")
            .AppendLine("    private string GetStringValue(int index)")
            .AppendLine("        => Values[index] is null ? string.Empty : Values[index] as string")
            .AppendLine("           ?? throw new InvalidOperationException(\"The private compiled entry frame received an unbound parameter type.\");")
            .AppendLine("    private void InvokeRegion(global::PowerForge.Generated.Runtime.PowerShellStatementErrorContext context, string script, object?[] arguments, global::PowerForge.Generated.Runtime.PowerShellHostedRegionSource? source)")
            .AppendLine("    {")
            .AppendLine("        var block = source is null ? ScriptBlock.Create(script) : source.CreateScriptBlock(script);")
            .AppendLine("        global::PowerForge.Generated.Runtime.PowerShellStatementErrorContext.BindModule(this, block);")
            .AppendLine("        context.InvokeCommandRegion(block, arguments);")
            .AppendLine("    }")
            .AppendLine("    private object? CaptureRegion(global::PowerForge.Generated.Runtime.PowerShellStatementErrorContext context, string script, object?[] arguments, global::PowerForge.Generated.Runtime.PowerShellHostedRegionSource? source)")
            .AppendLine("    {")
            .AppendLine("        var block = source is null ? ScriptBlock.Create(script) : source.CreateScriptBlock(script);")
            .AppendLine("        global::PowerForge.Generated.Runtime.PowerShellStatementErrorContext.BindModule(this, block);")
            .AppendLine("        return context.CaptureCommandRegion(block, arguments);")
            .AppendLine("    }")
            .AppendLine("}");
        return source.ToString();
    }

    internal static string ComposeScript(string packagedSource, string authoredSourcePath, PowerShellTypedExecutableCompilation compiled)
    {
        var authored = Parser.ParseFile(authoredSourcePath, out _, out var authoredErrors);
        var packaged = Parser.ParseInput(packagedSource, out _, out var packagedErrors);
        if (authoredErrors.Length != 0 || packagedErrors.Length != 0 ||
            authored.EndBlock?.Statements.Count != 1 || packaged.EndBlock is null)
            throw new InvalidOperationException("The compiled Hybrid entry no longer has one valid authored statement.");
        var original = authored.EndBlock.Statements[0].Extent.Text;
        var matches = packaged.EndBlock.Statements.Where(statement =>
            statement.GetType() == authored.EndBlock.Statements[0].GetType() &&
            statement.Extent.Text.Equals(original, StringComparison.Ordinal)).ToArray();
        if (matches.Length != 1)
            throw new InvalidOperationException("The packaged Hybrid entry no longer has one exact authored statement.");
        var selected = matches[0].Extent;
        var values = compiled.EntryPoint.Parameters.Select(parameter => "$" + parameter.Contract.Name);
        var invocation = "Invoke-PowerForgeCompiledEntry -Values ([object[]] @(" + string.Join(", ", values) + "))";
        return packagedSource.Substring(0, selected.StartOffset) + invocation + packagedSource.Substring(selected.EndOffset);
    }
}
