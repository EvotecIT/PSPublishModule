using System.Text;

namespace PowerForge;

/// <summary>Renders the generated cmdlet host for one explicit advanced-function lifecycle contract.</summary>
internal static class PowerShellHostedLifecycleSourceGenerator
{
    internal static void AppendMembers(StringBuilder builder, PowerShellCompiledMethod method)
    {
        var lifecycle = method.Lifecycle!;
        builder.AppendLine("    private SteppablePipeline? __powerForgePipeline;");
        builder.AppendLine("    private bool __powerForgeCleaned;");
        builder.AppendLine("    private bool __powerForgePipelineInputExplicitlyBound;");
        builder.AppendLine();
        builder.AppendLine("    protected override void BeginProcessing()");
        builder.AppendLine("    {");
        builder.AppendLine("        var bound = new global::System.Collections.Generic.Dictionary<string, object?>(global::System.StringComparer.OrdinalIgnoreCase);");
        builder.AppendLine("        foreach (var item in MyInvocation.BoundParameters)");
        builder.AppendLine("            bound[item.Key] = item.Value;");
        if (lifecycle.PipelineParameterNames.Length > 0)
        {
            builder.Append("        __powerForgePipelineInputExplicitlyBound = ")
                .Append(string.Join(" || ", lifecycle.PipelineParameterNames.Select(name =>
                    "MyInvocation.BoundParameters.ContainsKey(" + PowerShellCSharpLiteral.QuoteString(name) + ")")))
                .AppendLine(";");
        }
        var hostedSource = "param($bound)" + Environment.NewLine + "& " + method.HostedLifecycleSource + " @bound";
        builder.Append("        var script = ScriptBlock.Create(")
            .Append(PowerShellCSharpLiteral.QuoteString(hostedSource))
            .AppendLine(");");
        builder.AppendLine("        __powerForgePipeline = script.GetSteppablePipeline(CommandOrigin.Internal, new object[] { bound });");
        var expectsPipelineInput = lifecycle.ValueFromPipeline || lifecycle.ValueFromPipelineByPropertyName;
        builder.Append("        __powerForgePipeline.Begin(")
            .Append(expectsPipelineInput ? "!__powerForgePipelineInputExplicitlyBound" : "false")
            .AppendLine(");");
        builder.AppendLine("    }");
        builder.AppendLine();
        AppendProcessRecord(builder, method, lifecycle);
        builder.AppendLine("    protected override void EndProcessing()");
        builder.AppendLine("    {");
        builder.AppendLine("        if (__powerForgePipeline is null) return;");
        builder.AppendLine("        try { WriteLifecycleOutput(__powerForgePipeline.End()); }");
        builder.AppendLine("        finally { CleanLifecycle(); }");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    protected override void StopProcessing() => CleanLifecycle();");
        builder.AppendLine();
        builder.AppendLine("    private void WriteLifecycleOutput(global::System.Array? values)");
        builder.AppendLine("    {");
        builder.AppendLine("        if (values is null) return;");
        builder.AppendLine("        foreach (var value in values) WriteObject(value, enumerateCollection: false);");
        builder.AppendLine("    }");
        builder.AppendLine();
        builder.AppendLine("    private void CleanLifecycle()");
        builder.AppendLine("    {");
        builder.AppendLine("        if (__powerForgeCleaned) return;");
        builder.AppendLine("        __powerForgeCleaned = true;");
        builder.AppendLine("        var pipeline = __powerForgePipeline;");
        builder.AppendLine("        __powerForgePipeline = null;");
        builder.AppendLine("        if (pipeline is null) return;");
        builder.AppendLine("        var clean = typeof(SteppablePipeline).GetMethod(\"Clean\", global::System.Type.EmptyTypes);");
        builder.AppendLine("        clean?.Invoke(pipeline, null);");
        builder.AppendLine("    }");
    }

    private static void AppendProcessRecord(
        StringBuilder builder,
        PowerShellCompiledMethod method,
        PowerShellCompilationLifecycleContract lifecycle)
    {
        builder.AppendLine("    protected override void ProcessRecord()");
        builder.AppendLine("    {");
        builder.AppendLine("        if (__powerForgePipeline is null) return;");
        builder.AppendLine("        try");
        builder.AppendLine("        {");
        if (lifecycle.ValueFromPipeline)
        {
            var parameter = method.Parameters.First(parameter => parameter.Bindings.Any(static binding => binding.ValueFromPipeline));
            builder.AppendLine("            if (__powerForgePipelineInputExplicitlyBound)");
            builder.AppendLine("                WriteLifecycleOutput(__powerForgePipeline.Process());");
            builder.AppendLine("            else");
            builder.Append("                WriteLifecycleOutput(__powerForgePipeline.Process(")
                .Append(PowerShellCSharpSymbolRenderer.Identifier(parameter.Name))
                .AppendLine("));");
        }
        else if (lifecycle.ValueFromPipelineByPropertyName)
        {
            builder.AppendLine("            if (__powerForgePipelineInputExplicitlyBound)");
            builder.AppendLine("            {");
            builder.AppendLine("                WriteLifecycleOutput(__powerForgePipeline.Process());");
            builder.AppendLine("                return;");
            builder.AppendLine("            }");
            builder.AppendLine("            var input = new PSObject();");
            foreach (var parameter in method.Parameters.Where(static parameter =>
                         parameter.Bindings.Any(static binding => binding.ValueFromPipelineByPropertyName)))
            {
                builder.Append("            input.Properties.Add(new PSNoteProperty(")
                    .Append(PowerShellCSharpLiteral.QuoteString(parameter.Name))
                    .Append(", ")
                    .Append(PowerShellCSharpSymbolRenderer.Identifier(parameter.Name))
                    .AppendLine("));");
            }
            builder.AppendLine("            WriteLifecycleOutput(__powerForgePipeline.Process(input));");
        }
        else if (lifecycle.HasProcess)
        {
            builder.AppendLine("            WriteLifecycleOutput(__powerForgePipeline.Process());");
        }
        builder.AppendLine("        }");
        builder.AppendLine("        catch");
        builder.AppendLine("        {");
        builder.AppendLine("            CleanLifecycle();");
        builder.AppendLine("            throw;");
        builder.AppendLine("        }");
        builder.AppendLine("    }");
        builder.AppendLine();
    }
}
