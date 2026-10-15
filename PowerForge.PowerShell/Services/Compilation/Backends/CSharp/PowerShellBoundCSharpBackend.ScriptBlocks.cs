using System.Text;

namespace PowerForge;

internal sealed partial class PowerShellBoundCSharpBackend
{
    private static string NativeScriptBlockFactoryName(PowerShellSymbolId target)
        => "__Create_" + PowerShellCSharpSymbolRenderer.Identifier(target.Name).TrimStart('@');

    private void AppendNativeScriptBlocks(StringBuilder builder, PowerShellLoweredFunction function,
        PowerShellCompilationCapability capabilities, ICollection<PowerShellCompilationSourceMapEntry> sourceMap,
        ICollection<PowerShellCompiledBodyIdentity> nestedBodies)
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
            // The public source map remains flattened. Retain the same shifted entry
            // instances in each private body's sidecar so owner selection cannot credit
            // a descendant statement to the containing root or another child body.
            var shiftedMaps = emitted.SourceMap.ToDictionary(static entry => entry, entry =>
                new PowerShellCompilationSourceMapEntry(entry.SourceStartLine, entry.SourceStartColumn,
                    entry.SourceEndLine, entry.SourceEndColumn, entry.GeneratedStartLine + lineOffset, entry.GeneratedStartColumn,
                    entry.GeneratedEndLine + lineOffset, entry.GeneratedEndColumn));
            foreach (var entry in emitted.SourceMap) sourceMap.Add(shiftedMaps[entry]);
            var descendantMaps = emitted.NestedBodies.SelectMany(static body => body.SourceMap).ToHashSet();
            var span = block.Span;
            nestedBodies.Add(new PowerShellCompiledBodyIdentity(emitted.GeneratedName, block.DeclarationName,
                span.DocumentId, child.SourcePath, span.StartOffset, span.EndOffset,
                span.StartLine, span.StartColumn, span.EndLine, span.EndColumn,
                emitted.SourceMap.Where(entry => !descendantMaps.Contains(entry)).Select(entry => shiftedMaps[entry]).ToArray())
            {
                NativeFunctionBinding = binding,
                RegionGraph = emitted.RegionGraph,
                RequiresPowerShellCommandRegions = emitted.RequiresPowerShellCommandRegions,
                RequiresPowerShellRuntimeState = emitted.RequiresPowerShellRuntimeState,
                RequiresPowerShellStatementErrors = emitted.RequiresPowerShellStatementErrors,
                RequiresPowerShellStopping = emitted.RequiresPowerShellStopping,
                RequiresPowerShellModuleStateRead = emitted.RequiresPowerShellModuleStateRead,
                RequiresPowerShellModuleStateWrite = emitted.RequiresPowerShellModuleStateWrite,
                RequiredPowerShellModuleVariables = emitted.ModuleStateVariableNames,
                WrittenPowerShellModuleVariables = emitted.WrittenModuleStateVariableNames
            });
            foreach (var body in emitted.NestedBodies)
                nestedBodies.Add(body.WithSourceMap(body.SourceMap.Select(entry => shiftedMaps[entry]).ToArray()));
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
