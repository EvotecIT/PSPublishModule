using System.Globalization;
using System.Text;

namespace PowerForge;

internal sealed partial class PowerShellBoundCSharpBackend
{
    private PowerShellCSharpMethodEmission EmitFunction(
        PowerShellLoweredFunction function,
        PowerShellCompilationCapability targetCapabilities)
    {
        _sourceFunction = function;
        var transfersCapturedReturn = TransfersCapturedReturn(function);
        if (transfersCapturedReturn && function.ReturnType != typeof(void))
            throw new InvalidOperationException("A native captured return requires a void success-stream method.");
        var managedInstance = targetCapabilities.HasFlag(PowerShellCompilationCapability.RuntimeFreeModuleState);
        var initializer = function.Symbol.Kind == PowerShellSymbolKind.ModuleInitializer;
        var bodyIndent = managedInstance ? 5 : 3;
        var builder = new StringBuilder();
        var sourceMap = new List<PowerShellCompilationSourceMapEntry>();
        var nestedBodies = new List<PowerShellCompiledBodyIdentity>();
        var parameterParts = function.Parameters.Select(parameter =>
            $"{PowerShellCSharpSymbolRenderer.TypeName(parameter.ClrType)} {PowerShellCSharpSymbolRenderer.Identifier(parameter.Symbol.Name)}").ToList();
        if (function.NativeFunctionBinding is not null)
            parameterParts = new List<string> { "global::PowerForge.Generated.Runtime.PowerShellNativeFunctionContext __nativeFunction" };
        var requiresBoundParameters = function.NativeFunctionBinding is null && (function.RequiresPowerShellBoundParameters || function.Parameters.Any(parameter =>
            parameter.DefaultsToCurrentDirectory ||
            PowerShellParameterValidationPolicy.RequiresBoundParameterSet(parameter.Contract, targetCapabilities)));
        AddHostParameters(parameterParts, function, requiresBoundParameters);
        var parameters = string.Join(", ", parameterParts);
        var usedIdentifiers = function.Parameters.Select(static parameter => PowerShellClrSymbolMapper.MapIdentifier(parameter.Symbol.Name))
            .Concat(function.Locals.Select(static local => PowerShellClrSymbolMapper.MapIdentifier(local.Symbol.Name)))
            .ToHashSet(StringComparer.Ordinal);
        var temporaryIndex = 0;
        string GetTemporaryIdentifier(string prefix)
        {
            string candidate;
            do { candidate = $"__{prefix}_{temporaryIndex++}"; } while (!usedIdentifiers.Add(candidate));
            return candidate;
        }
        _getTemporaryIdentifier = GetTemporaryIdentifier;
        InitializeLoopTransfers(function, GetTemporaryIdentifier);
        var discardHelper = ContainsDiscardValue(function.Statements)
            ? GetTemporaryIdentifier("discardValue")
            : null;
        _statementValueDiscardHelper = discardHelper;
        if (!initializer && function.Symbol.Kind != PowerShellSymbolKind.NativeScriptBlock)
            AppendPublicMethodDocumentation(builder, function);
        builder.Append(initializer ? "    private " : function.Symbol.Kind == PowerShellSymbolKind.NativeScriptBlock ? "    private static " :
                function.Symbol.Kind == PowerShellSymbolKind.ConditionalFunction ? "    internal static " : managedInstance ? "    public " : "    public static ")
            .Append(PowerShellCSharpSymbolRenderer.TypeName(function.ReturnType))
            .Append(' ')
            .Append(function.GeneratedName)
            .Append('(')
            .Append(parameters)
            .AppendLine(")")
            .AppendLine("    {");
        if (managedInstance)
            builder.AppendLine("        lock (this.__moduleSync)")
                .AppendLine("        {")
                .AppendLine("            this.__ThrowIfDisposed();")
                .AppendLine("            this.__moduleActiveCalls++;")
                .AppendLine("            try")
                .AppendLine("            {");
        builder.AppendLine(managedInstance ? "                checked" : "        checked")
            .AppendLine(managedInstance ? "                {" : "        {");

        if (targetCapabilities.HasFlag(PowerShellCompilationCapability.PowerShellLanguageConversions))
        {
            builder.AppendLine("            static T __powerForgeConvertInvariant<T>(object? value)")
                .AppendLine("            {")
                .AppendLine("                if (global::System.Management.Automation.LanguagePrimitives.TryConvertTo<T>(value, global::System.Globalization.CultureInfo.InvariantCulture, out T result)) return result;")
                .AppendLine("                return (T)global::System.Management.Automation.LanguagePrimitives.ConvertTo(value, typeof(T), global::System.Globalization.CultureInfo.InvariantCulture)!;")
                .AppendLine("            }");
        }
        if (discardHelper is not null)
        {
            builder.Append("            static void ").Append(discardHelper).AppendLine("<T>(T value) { }");
        }
        var parameterContracts = function.Parameters.Where(_ => function.NativeFunctionBinding is null).Select(static parameter => new PowerShellParameterEmissionContract(
            parameter.Symbol.Name,
            parameter.ClrType,
            parameter.Contract,
            parameter.DefaultsToCurrentDirectory)).ToArray();
        var prologue = new PowerShellParameterPrologueRenderer(
            targetCapabilities,
            PowerShellCSharpSymbolRenderer.TypeName,
            GetTemporaryIdentifier).Render(parameterContracts);
        if (prologue.Length > 0)
        {
            foreach (var line in prologue.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None))
                builder.Append(new string(' ', bodyIndent * 4)).AppendLine(line);
        }

        if (function.NativeFunctionBinding is not null && function.RequiresPowerShellStatementErrors)
            EmitNativeEntrySequencePoint(builder, function);
        if (transfersCapturedReturn)
            builder.Append(new string(' ', bodyIndent * 4)).AppendLine("try")
                .Append(new string(' ', bodyIndent * 4)).AppendLine("{");
        foreach (var statement in function.Statements)
            EmitStatement(builder, statement, bodyIndent + (transfersCapturedReturn ? 1 : 0), GetTemporaryIdentifier, discardHelper, sourceMap);
        if (transfersCapturedReturn)
            builder.Append(new string(' ', bodyIndent * 4)).AppendLine("}")
                .Append(new string(' ', bodyIndent * 4)).AppendLine("catch (global::PowerForge.Generated.Runtime.PowerShellCapturedReturnSignal) { return; }");

        foreach (var helper in _numericHelpers.Values)
            builder.AppendLine(helper.Source);
        EmitFormatHelper(builder);

        builder.AppendLine(managedInstance ? "                }" : "        }");
        if (managedInstance) builder.AppendLine("            }")
            .AppendLine("            finally { this.__moduleActiveCalls--; }")
            .AppendLine("        }");
        builder.Append("    }");
        AppendNativeScriptBlocks(builder, function, targetCapabilities, sourceMap, nestedBodies);
        var regionGraph = PowerShellLoweredRegionGraphBuilder.Create(function, _functions);
        var closureStatements = PowerShellLoweredScriptBlockClosure.Enumerate(function, _functions).SelectMany(static body => body.Statements).ToArray();
        var commandProviders = PowerShellLoweredCommandProviderCollector.Collect(closureStatements);
        var moduleStateVariableNames = PowerShellLoweredModuleStateCollector.Collect(closureStatements);
        var moduleStateReadSiteCount = regionGraph.ModuleStateReadBoundarySites;
        var writtenModuleStateVariableNames = PowerShellLoweredModuleStateCollector.CollectWrites(closureStatements);
        var moduleStateWriteSiteCount = regionGraph.ModuleStateWriteBoundarySites;
        var hostedRegionSiteCount = regionGraph.HostedCommandBoundarySites;
        var supportsBasicCommandQuerySurface =
            function.RequiresPowerShellCommandRegions &&
            !function.RequiresPowerShellStreams &&
            !ContainsNonQueryHostedBoundary(function.Statements) &&
            commandProviders.Length > 0 &&
            commandProviders.All(static provider => provider.Family == PowerShellCompilationCommandFamily.CommandDiscovery);
        return new PowerShellCSharpMethodEmission(
            function.GeneratedName,
            function.ReturnType,
            builder.ToString(),
            function.Span,
            requiresPowerShellStreams: function.RequiresPowerShellStreams,
            requiresProviderCancellation: function.RequiresProviderCancellation,
            requiresPowerShellCommandRegions: function.RequiresPowerShellCommandRegions,
            requiresPowerShellBoundParameters: requiresBoundParameters,
            requiresPowerShellRuntimeState: function.RequiresPowerShellRuntimeState,
            requiresPowerShellModuleStateRead: function.RequiresPowerShellModuleStateRead,
            requiresPowerShellModuleStateWrite: function.RequiresPowerShellModuleStateWrite,
            requiresPowerShellStatementErrors: function.RequiresPowerShellStatementErrors,
            requiresPowerShellStopping: function.RequiresPowerShellStopping,
            help: function.Help?.ToPublicModel(),
            declaredOutputType: function.DeclaredOutputType,
            declaredOutputTypeName: function.DeclaredOutputTypeName,
            aliases: function.Aliases.ToArray(),
            commandBinding: function.CommandBinding,
            sourceMap: sourceMap.ToArray(),
            commandProviders: commandProviders,
            outputCardinality: function.OutputCardinality.ToString(),
            outputValueStates: function.OutputValueStates.Select(static state => state.ToString()).ToArray(),
            collectionElementType: function.OutputCardinality == PowerShellOutputCardinality.Collection
                ? function.CollectionElementType?.FullName ?? typeof(object).FullName!
                : string.Empty,
            outputScalarization: function.Statements.LastOrDefault() is PowerShellLoweredRegionTransferStatement
                ? "PreserveRegionTransfer" : function.OutputCardinality switch
            {
                PowerShellOutputCardinality.None => "NoOutput",
                PowerShellOutputCardinality.Collection => "EnumerateCollection",
                PowerShellOutputCardinality.Scalar => "PreserveScalar",
                _ => "RuntimeDependent"
            },
            hostedRegionSiteCount: hostedRegionSiteCount,
            supportsBasicCommandQuerySurface: supportsBasicCommandQuerySurface,
            moduleStateVariableNames: moduleStateVariableNames,
            moduleStateReadSiteCount: moduleStateReadSiteCount,
            writtenModuleStateVariableNames: writtenModuleStateVariableNames,
            moduleStateWriteSiteCount: moduleStateWriteSiteCount,
            regionGraph: regionGraph,
            nativeFunctionBinding: function.NativeFunctionBinding,
            successOutputType: function.SuccessOutputType,
            outputTypeDeclarations: function.OutputTypeDeclarations)
        {
            NestedBodies = nestedBodies.ToArray(),
            NativeSourceDocuments = _nativeSourceDocuments.Documents
        };
    }

}
