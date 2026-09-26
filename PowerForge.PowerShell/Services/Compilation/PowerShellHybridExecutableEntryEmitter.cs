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
            authored.EndBlock?.Statements is not { Count: > 0 } authoredStatements ||
            packaged.EndBlock is null || packaged.EndBlock.Statements.Count < authoredStatements.Count)
            throw new InvalidOperationException("The compiled Hybrid entry no longer has a valid authored statement sequence.");
        var packagedStatements = packaged.EndBlock.Statements;
        var matches = Enumerable.Range(0, packagedStatements.Count - authoredStatements.Count + 1)
            .Where(start => authoredStatements.Select((statement, offset) =>
                packagedStatements[start + offset].GetType() == statement.GetType() &&
                packagedStatements[start + offset].Extent.Text.Equals(statement.Extent.Text, StringComparison.Ordinal))
                .All(static matched => matched)).ToArray();
        if (matches.Length != 1)
            throw new InvalidOperationException("The packaged Hybrid entry no longer has one exact authored statement sequence.");
        var selected = packagedStatements.Skip(matches[0]).Take(authoredStatements.Count)
            .Select(static statement => statement.Extent).ToArray();
        var values = compiled.EntryPoint.Parameters.Select(parameter => "$" + parameter.Contract.Name);
        var invocation = "Invoke-PowerForgeCompiledEntry -Values ([object[]] @(" + string.Join(", ", values) + "))";
        var source = new StringBuilder(packagedSource);
        for (var index = selected.Length - 1; index > 0; index--)
            source.Remove(selected[index].StartOffset, selected[index].EndOffset - selected[index].StartOffset);
        source.Remove(selected[0].StartOffset, selected[0].EndOffset - selected[0].StartOffset);
        source.Insert(selected[0].StartOffset, invocation);
        return source.ToString();
    }
}
