using System.Text;

namespace PowerForge;

internal sealed partial class PowerShellBoundCSharpBackend
{
    private static string NativeScriptBlockFactoryName(PowerShellSymbolId target)
        => "__Create_" + PowerShellCSharpSymbolRenderer.Identifier(target.Name).TrimStart('@');

    private void AppendNativeScriptBlocks(StringBuilder builder, PowerShellLoweredFunction function,
        PowerShellCompilationCapability capabilities, ICollection<PowerShellCompilationSourceMapEntry> sourceMap)
    {
        var blocks = PowerShellLoweredScriptBlockClosure.DirectBlocks(function);
        foreach (var block in blocks)
        {
            var child = _functions[block.Target.StableKey];
            var binding = child.NativeFunctionBinding ?? throw new InvalidOperationException("A compiled script block requires native binding.");
            var emitted = new PowerShellBoundCSharpBackend { _functions = _functions }.EmitFunction(child, capabilities);
            builder.AppendLine();
            var lineOffset = PowerShellGeneratedSourcePosition.Get(builder).Line - 1;
            builder.AppendLine(emitted.Source);
            foreach (var entry in emitted.SourceMap)
                sourceMap.Add(new PowerShellCompilationSourceMapEntry(entry.SourceStartLine, entry.SourceStartColumn,
                    entry.SourceEndLine, entry.SourceEndColumn, entry.GeneratedStartLine + lineOffset, entry.GeneratedStartColumn,
                    entry.GeneratedEndLine + lineOffset, entry.GeneratedEndColumn));
            builder.Append("    private static global::System.Management.Automation.ScriptBlock ")
                .Append(NativeScriptBlockFactoryName(block.Target))
                .AppendLine("(global::PowerForge.Generated.Runtime.PowerShellNativeFunctionContext creationContext)")
                .AppendLine("    {")
                .Append(block.DeclarationName is not null ? "        return creationContext.CreateFunctionBody(" :
                    block.IsSwitchPredicate ? "        return creationContext.CreateSwitchPredicate(" : "        return creationContext.CreateScriptBlock(")
                .Append(PowerShellCSharpLiteral.QuoteString(block.SourceDocument))
                .Append(", ").Append(block.Span.StartOffset).Append(", ").Append(block.Span.EndOffset).AppendLine(",")
                .Append("            ").Append(Callback(binding.HasBegin, 0)).AppendLine(",")
                .Append("            ").Append(Callback(binding.HasProcess, 1)).AppendLine(",")
                .Append("            ").Append(Callback(binding.HasEnd, 2)).AppendLine(",")
                .Append("            ").Append(Callback(binding.HasClean, 3)).AppendLine(");")
                .AppendLine("        static void Invoke(global::PowerForge.Generated.Runtime.PowerShellNativeFunctionContext context, int clause)")
                .AppendLine("        {");
            PowerShellNativeCallbackSource.AppendBody(builder, "            ", child.GeneratedName, string.Empty,
                child.RequiresPowerShellStatementErrors, child.RequiresPowerShellStopping, child.RequiresPowerShellStreams,
                child.ReturnType == typeof(void), child.OutputCardinality == PowerShellOutputCardinality.Collection);
            builder.AppendLine("        }").AppendLine("    }");
        }
    }

    private static string Callback(bool present, int clause)
        => PowerShellNativeCallbackSource.Callback(present, clause);

    private static string EmitNativeBlockValue(PowerShellLoweredNativeScriptBlockExpression block)
    {
        var value = NativeScriptBlockFactoryName(block.Target) + "(__nativeFunction)";
        return block.DeclarationName is null ? value : "__nativeFunction.DeclareFunction(" +
            PowerShellCSharpLiteral.QuoteString(block.SourceDocument) + ", " + block.Span.StartOffset + ", " + block.Span.EndOffset + ", " + value + ")";
    }
}
