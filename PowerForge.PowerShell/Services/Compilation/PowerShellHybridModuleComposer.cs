using System.Management.Automation.Language;
using System.Text;

namespace PowerForge;

/// <summary>
/// Composes the script side of a hybrid module while routing selected functions to generated binary cmdlets.
/// </summary>
internal static class PowerShellHybridModuleComposer
{
    internal static string ComposeExecutableRoot(
        string source,
        string sourcePath,
        PowerShellTypedCompilationResult typed)
    {
        var ast = Parser.ParseInput(source, out _, out var errors);
        if (errors.Length > 0)
            throw new InvalidOperationException("Hybrid executable source could not be parsed while composing retained fallback code.");
        var compiledNames = typed.Methods
            .Where(method => method.Lifecycle is null && PowerShellCompilationPathSafety.PathEquals(
                string.IsNullOrWhiteSpace(method.SourcePath) ? typed.SourcePath : method.SourcePath,
                sourcePath))
            .Select(static method => method.SourceName)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var result = new StringBuilder(ast.Extent.Text);
        foreach (var function in ast.FindAll(static node => node is FunctionDefinitionAst, searchNestedScriptBlocks: false)
                     .Cast<FunctionDefinitionAst>()
                     .Where(function => compiledNames.Contains(function.Name))
                     .OrderByDescending(static function => function.Extent.StartOffset))
            result.Remove(function.Extent.StartOffset, function.Extent.EndOffset - function.Extent.StartOffset);
        return result.ToString();
    }

    internal static string ComposeRoot(
        string sourcePath,
        string assemblyFileName,
        PowerShellTypedCompilationResult typed,
        bool manifestControlsExports = false)
    {
        Token[] tokens;
        ParseError[] errors;
        var ast = Parser.ParseFile(sourcePath, out tokens, out errors);
        if (errors.Length > 0)
            throw new InvalidOperationException("Hybrid module source could not be parsed while composing fallback code.");

        var prologueEndOffset = ast.ParamBlock?.Extent.EndOffset ?? 0;
        foreach (var usingStatement in ast.FindAll(static node => node is UsingStatementAst, searchNestedScriptBlocks: false).Cast<UsingStatementAst>())
            prologueEndOffset = Math.Max(prologueEndOffset, usingStatement.Extent.EndOffset);
        var functions = ast.FindAll(static node => node is FunctionDefinitionAst, searchNestedScriptBlocks: false)
            .Cast<FunctionDefinitionAst>()
            .OrderByDescending(static function => function.Extent.StartOffset)
            .ToArray();
        var runtimeVariables = CreateRuntimeRegionVariableNames(ast);
        var readModuleStateVariables = typed.Methods
            .SelectMany(static method => method.RequiredPowerShellModuleVariables)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static name => name, StringComparer.Ordinal)
            .ToArray();
        var writtenModuleStateVariables = typed.Methods
            .SelectMany(static method => method.WrittenPowerShellModuleVariables)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .OrderBy(static name => name, StringComparer.OrdinalIgnoreCase)
            .ThenBy(static name => name, StringComparer.Ordinal)
            .ToArray();
        var source = new StringBuilder(ast.Extent.Text);
        var exportContract = PowerShellModuleExportContract.TryRead(ast);
        var wrapped = GetWrappedCompiledMethodKeys(sourcePath, typed);
        var edits = new List<PowerShellHybridSourceEdit>();
        foreach (var function in functions)
        {
            if (!wrapped.Contains(GetCompiledMethodKey(sourcePath, function.Name, function.Body.Extent.StartLineNumber)))
                continue;
            if (function.Extent.StartOffset < prologueEndOffset)
                throw new InvalidOperationException($"Compiled function '{function.Name}' overlaps the module prologue and cannot be composed safely.");
            edits.Add(new PowerShellHybridSourceEdit(
                function.Extent.StartOffset,
                function.Extent.EndOffset - function.Extent.StartOffset,
                string.Empty,
                "function:" + function.Name));
        }
        if (exportContract is not null)
            edits.AddRange(exportContract.Commands.Select(static command => new PowerShellHybridSourceEdit(
                command.Extent.StartOffset,
                command.Extent.EndOffset - command.Extent.StartOffset,
                string.Empty,
                "export:" + command.Extent.StartOffset)));
        edits.AddRange(PowerShellHybridRegionRewriter.CreateEdits(sourcePath, ast, typed, wrapped));
        foreach (var edit in edits.OrderByDescending(static edit => edit.Start))
        {
            source.Remove(edit.Start, edit.Length);
            source.Insert(edit.Start, edit.Replacement);
        }

        var fallbackFunctions = ReadModuleScopeFunctions(typed.SourcePaths)
            .Where(function => !wrapped.Contains(GetCompiledMethodKey(function.Path, function.Name, function.Line)))
            .Select(static function => function.Name)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var compiledCmdlets = typed.Methods
            .Select(static method => method.SourceName)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var exportedFallbackFunctions = (exportContract?.SelectFunctions(fallbackFunctions) ?? fallbackFunctions)
            .Concat(PowerShellCompiledModuleManifest.GetNestedModuleFunctionExportPatterns(sourcePath, fallbackFunctions))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var exportedCompiledCmdlets = exportContract?.SelectFunctions(compiledCmdlets) ?? compiledCmdlets;
        var additionalCmdlets = exportContract?.Cmdlets ?? Array.Empty<string>();
        var aliases = exportContract?.Aliases ?? new[] { "*" };
        var variables = exportContract?.Variables ?? Array.Empty<string>();
        var import = new StringBuilder();
        if (prologueEndOffset > 0 && source[prologueEndOffset - 1] is not '\r' and not '\n') import.AppendLine();
        import.AppendLine("# Generated by PowerForge hybrid PowerShell compilation.");
        import.AppendLine("Microsoft.PowerShell.Core\\Import-Module -Name (Microsoft.PowerShell.Management\\Join-Path -Path $PSScriptRoot -ChildPath '" + EscapePowerShellSingleQuotedString(assemblyFileName) + "') -Force -ErrorAction Stop");
        var requiresDispatcher = typed.Methods.Any(static method => method.RequiresPowerShellCommandRegions);
        var requiresModuleStateRead = readModuleStateVariables.Length > 0;
        var requiresModuleStateWrite = writtenModuleStateVariables.Length > 0;
        var requiresModuleState = requiresModuleStateRead || requiresModuleStateWrite;
        if (requiresDispatcher || requiresModuleState)
        {
            import.Append('$').Append(runtimeVariables.RunspaceId).AppendLine(" = [System.Management.Automation.Runspaces.Runspace]::DefaultRunspace.InstanceId");
            AppendRuntimeRegionGuardStart(
                import,
                typed,
                runtimeVariables,
                requiresDispatcher,
                requiresModuleStateRead,
                requiresModuleStateWrite);
        }
        if (requiresDispatcher)
        {
            var hostType = "[" + typed.NamespaceName + "." + PowerShellBinaryCmdletSourceGenerator.GetRuntimeRegionHostTypeName(typed) + "]";
            import.Append(hostType).Append("::SetDispatcher($").Append(runtimeVariables.RunspaceId)
                .AppendLine(", { param($script, [object[]] $arguments) & ([scriptblock]::Create($script)) @arguments })");
        }
        if (requiresModuleStateRead)
        {
            var hostType = "[" + typed.NamespaceName + "." + PowerShellBinaryCmdletSourceGenerator.GetRuntimeRegionHostTypeName(typed) + "]";
            foreach (var variable in readModuleStateVariables)
            {
                import.Append(hostType).Append("::SetModuleVariableReader($").Append(runtimeVariables.RunspaceId)
                    .Append(", '").Append(EscapePowerShellSingleQuotedString(variable)).Append("', { try { ")
                    .Append(hostType).Append("::CreateModuleVariableReadSuccess($script:").Append(variable)
                    .Append(") } catch { ").Append(hostType).AppendLine("::CreateModuleVariableReadFailure($_) } })");
            }
        }
        if (requiresModuleStateWrite)
        {
            var hostType = "[" + typed.NamespaceName + "." + PowerShellBinaryCmdletSourceGenerator.GetRuntimeRegionHostTypeName(typed) + "]";
            foreach (var variable in writtenModuleStateVariables)
            {
                import.Append(hostType).Append("::SetModuleVariableWriter($").Append(runtimeVariables.RunspaceId)
                    .Append(", '").Append(EscapePowerShellSingleQuotedString(variable)).Append("', { param([object] $value) try { $script:")
                    .Append(variable).Append(" = $value; ").Append(hostType)
                    .Append("::CreateModuleVariableWriteSuccess() } catch { ").Append(hostType)
                    .AppendLine("::CreateModuleVariableWriteFailure($_) } })");
            }
        }
        import.AppendLine();
        source.Insert(prologueEndOffset, import.ToString());
        var builder = new StringBuilder(source.ToString());
        if (source.Length > 0 && source[source.Length - 1] != '\n') builder.AppendLine();
        if (!manifestControlsExports && (exportContract is null || exportContract.Commands.Length > 0))
        {
            builder.AppendLine();
            builder.Append("Microsoft.PowerShell.Core\\Export-ModuleMember -Function @(").Append(JoinPowerShellNames(exportedFallbackFunctions))
                .Append(") -Cmdlet @(").Append(JoinPowerShellNames(exportedCompiledCmdlets.Concat(additionalCmdlets).Distinct(StringComparer.OrdinalIgnoreCase)))
                .Append(") -Alias @(").Append(JoinPowerShellNames(aliases)).Append(')');
            if (variables.Length > 0)
                builder.Append(" -Variable @(").Append(JoinPowerShellNames(variables)).Append(')');
            builder.AppendLine();
        }
        AppendRuntimeRegionGuardEnd(
            builder,
            typed,
            runtimeVariables,
            requiresDispatcher,
            requiresModuleStateRead,
            requiresModuleStateWrite);
        return builder.ToString();
    }

    private static void AppendRuntimeRegionGuardStart(
        StringBuilder builder,
        PowerShellTypedCompilationResult typed,
        RuntimeRegionVariableNames variables,
        bool requiresDispatcher,
        bool requiresModuleStateRead,
        bool requiresModuleStateWrite)
    {
        var hostType = "[" + typed.NamespaceName + "." + PowerShellBinaryCmdletSourceGenerator.GetRuntimeRegionHostTypeName(typed) + "]";
        builder.Append('$').Append(variables.Module).AppendLine(" = $ExecutionContext.SessionState.Module");
        builder.Append('$').Append(variables.PreviousOnRemove).Append(" = $").Append(variables.Module).AppendLine(".OnRemove");
        builder.Append('$').Append(variables.InitializationFailed).AppendLine(" = $false");
        builder.Append('$').Append(variables.Cleanup).AppendLine(" = {");
        AppendRuntimeRegionClear(builder, hostType, variables, requiresDispatcher, requiresModuleStateRead, requiresModuleStateWrite, "    ");
        builder.AppendLine("}.GetNewClosure()");
        builder.Append('$').Append(variables.InstalledOnRemove).AppendLine(" = {");
        builder.AppendLine("    try {");
        builder.Append("        if ($null -ne $").Append(variables.PreviousOnRemove).Append(") { & $").Append(variables.PreviousOnRemove).AppendLine(" }");
        builder.AppendLine("    } finally {");
        builder.Append("        & $").Append(variables.Cleanup).AppendLine();
        builder.AppendLine("    }");
        builder.AppendLine("}.GetNewClosure()");
        builder.Append('$').Append(variables.Module).Append(".OnRemove = $").Append(variables.InstalledOnRemove).AppendLine();
        builder.AppendLine("try {");
    }

    private static void AppendRuntimeRegionGuardEnd(
        StringBuilder builder,
        PowerShellTypedCompilationResult typed,
        RuntimeRegionVariableNames variables,
        bool requiresDispatcher,
        bool requiresModuleStateRead,
        bool requiresModuleStateWrite)
    {
        var requiresModuleState = requiresModuleStateRead || requiresModuleStateWrite;
        if (!requiresDispatcher && !requiresModuleState)
            return;
        var hostType = "[" + typed.NamespaceName + "." + PowerShellBinaryCmdletSourceGenerator.GetRuntimeRegionHostTypeName(typed) + "]";
        builder.AppendLine("} catch {");
        builder.Append("    $").Append(variables.InitializationFailed).AppendLine(" = $true");
        AppendRuntimeRegionClear(builder, hostType, variables, requiresDispatcher, requiresModuleStateRead, requiresModuleStateWrite, "    ");
        builder.Append("    $").Append(variables.Module).Append(".OnRemove = $").Append(variables.PreviousOnRemove).AppendLine();
        builder.AppendLine("    throw");
        builder.AppendLine("} finally {");
        builder.Append("    if (-not $").Append(variables.InitializationFailed)
            .Append(" -and -not [object]::ReferenceEquals($").Append(variables.Module).Append(".OnRemove, $")
            .Append(variables.InstalledOnRemove).AppendLine(")) {");
        builder.Append("        $").Append(variables.EffectiveOnRemove).Append(" = $").Append(variables.Module).AppendLine(".OnRemove");
        builder.Append('$').Append(variables.Module).AppendLine(".OnRemove = {");
        builder.AppendLine("            try {");
        builder.Append("                if ($null -ne $").Append(variables.EffectiveOnRemove).Append(") { & $").Append(variables.EffectiveOnRemove).AppendLine(" }");
        builder.AppendLine("            } finally {");
        builder.Append("                & $").Append(variables.Cleanup).AppendLine();
        builder.AppendLine("            }");
        builder.AppendLine("        }.GetNewClosure()");
        builder.AppendLine("    }");
        builder.Append("    [void]$").Append(variables.Module).Append(".SessionState.PSVariable.Remove('").Append(variables.EffectiveOnRemove).AppendLine("')");
        builder.Append("    [void]$").Append(variables.Module).Append(".SessionState.PSVariable.Remove('").Append(variables.InstalledOnRemove).AppendLine("')");
        builder.Append("    [void]$").Append(variables.Module).Append(".SessionState.PSVariable.Remove('").Append(variables.Cleanup).AppendLine("')");
        builder.Append("    [void]$").Append(variables.Module).Append(".SessionState.PSVariable.Remove('").Append(variables.InitializationFailed).AppendLine("')");
        builder.Append("    [void]$").Append(variables.Module).Append(".SessionState.PSVariable.Remove('").Append(variables.RunspaceId).AppendLine("')");
        builder.Append("    [void]$").Append(variables.Module).Append(".SessionState.PSVariable.Remove('").Append(variables.PreviousOnRemove).AppendLine("')");
        builder.Append("    [void]$").Append(variables.Module).Append(".SessionState.PSVariable.Remove('").Append(variables.Module).AppendLine("')");
        builder.AppendLine("}");
    }

    private static void AppendRuntimeRegionClear(
        StringBuilder builder,
        string hostType,
        RuntimeRegionVariableNames variables,
        bool requiresDispatcher,
        bool requiresModuleStateRead,
        bool requiresModuleStateWrite,
        string indentation)
    {
        if (requiresDispatcher)
            builder.Append(indentation).Append(hostType).Append("::ClearDispatcher($").Append(variables.RunspaceId).AppendLine(")");
        if (requiresModuleStateRead)
            builder.Append(indentation).Append(hostType).Append("::ClearModuleVariableReaders($").Append(variables.RunspaceId).AppendLine(")");
        if (requiresModuleStateWrite)
            builder.Append(indentation).Append(hostType).Append("::ClearModuleVariableWriters($").Append(variables.RunspaceId).AppendLine(")");
    }

    internal static string? ComposeDependency(
        string sourcePath,
        PowerShellTypedCompilationResult typed,
        ISet<string> wrappedCompiledMethods)
    {
        var compiled = typed.Methods
            .Where(method => PowerShellCompilationPathSafety.PathEquals(
                string.IsNullOrWhiteSpace(method.SourcePath) ? typed.SourcePath : method.SourcePath,
                sourcePath))
            .Select(method => GetCompiledMethodKey(sourcePath, method.SourceName, method.SourceLine))
            .Where(wrappedCompiledMethods.Contains)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var hasPromotedRegions = typed.PromotedRegions.Any(region =>
            PowerShellCompilationPathSafety.PathEquals(region.SourcePath, sourcePath));
        if (compiled.Count == 0 && !hasPromotedRegions)
            return null;

        Token[] tokens;
        ParseError[] errors;
        var ast = Parser.ParseFile(sourcePath, out tokens, out errors);
        if (errors.Length > 0)
            throw new InvalidOperationException($"Hybrid module dependency '{sourcePath}' could not be parsed while composing fallback code.");
        var source = new StringBuilder(ast.Extent.Text);
        var edits = new List<PowerShellHybridSourceEdit>();
        foreach (var function in ast.FindAll(static node => node is FunctionDefinitionAst, searchNestedScriptBlocks: false)
                     .Cast<FunctionDefinitionAst>()
                     .Where(function => compiled.Contains(GetCompiledMethodKey(sourcePath, function.Name, function.Body.Extent.StartLineNumber)))
                     .OrderByDescending(static function => function.Extent.StartOffset))
        {
            edits.Add(new PowerShellHybridSourceEdit(
                function.Extent.StartOffset,
                function.Extent.EndOffset - function.Extent.StartOffset,
                string.Empty,
                "function:" + function.Name));
        }
        edits.AddRange(PowerShellHybridRegionRewriter.CreateEdits(sourcePath, ast, typed, wrappedCompiledMethods));
        foreach (var edit in edits.OrderByDescending(static edit => edit.Start))
        {
            source.Remove(edit.Start, edit.Length);
            source.Insert(edit.Start, edit.Replacement);
        }
        return source.ToString();
    }

    private static RuntimeRegionVariableNames CreateRuntimeRegionVariableNames(ScriptBlockAst ast)
    {
        var authoredNames = ast.FindAll(static node => node is VariableExpressionAst, searchNestedScriptBlocks: true)
            .OfType<VariableExpressionAst>()
            .Select(static variable => GetUnqualifiedVariableName(variable.VariablePath.UserPath))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        for (var index = 0; ; index++)
        {
            var suffix = index == 0 ? string.Empty : "_" + index.ToString(System.Globalization.CultureInfo.InvariantCulture);
            var names = new RuntimeRegionVariableNames(
                "__powerForgeRunspaceId" + suffix,
                "__powerForgeModule" + suffix,
                "__powerForgePreviousOnRemove" + suffix,
                "__powerForgeRuntimeCleanup" + suffix,
                "__powerForgeInstalledOnRemove" + suffix,
                "__powerForgeEffectiveOnRemove" + suffix,
                "__powerForgeInitializationFailed" + suffix);
            if (!authoredNames.Contains(names.RunspaceId) &&
                !authoredNames.Contains(names.Module) &&
                !authoredNames.Contains(names.PreviousOnRemove) &&
                !authoredNames.Contains(names.Cleanup) &&
                !authoredNames.Contains(names.InstalledOnRemove) &&
                !authoredNames.Contains(names.EffectiveOnRemove) &&
                !authoredNames.Contains(names.InitializationFailed))
                return names;
        }
    }

    private static string GetUnqualifiedVariableName(string userPath)
    {
        var separator = userPath.IndexOf(':');
        return separator < 0 ? userPath : userPath.Substring(separator + 1);
    }

    internal static HashSet<string> GetWrappedCompiledMethodKeys(string sourcePath, PowerShellTypedCompilationResult typed)
    {
        var exportContract = PowerShellModuleExportContract.TryRead(sourcePath);
        var wrappedFunctionNames = exportContract?.SelectFunctions(typed.Methods.Select(static method => method.SourceName))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        return typed.Methods
            .Where(method => wrappedFunctionNames is null || wrappedFunctionNames.Contains(method.SourceName))
            .Select(method => GetCompiledMethodKey(
                string.IsNullOrWhiteSpace(method.SourcePath) ? typed.SourcePath : method.SourcePath,
                method.SourceName,
                method.SourceLine))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    internal static HashSet<string> GetExecutableCompiledMethodKeys(PowerShellTypedCompilationResult typed)
        => typed.Methods
            .Where(static method => method.Lifecycle is null)
            .Select(method => GetCompiledMethodKey(
                string.IsNullOrWhiteSpace(method.SourcePath) ? typed.SourcePath : method.SourcePath,
                method.SourceName,
                method.SourceLine))
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

    private static IEnumerable<ModuleScopeFunction> ReadModuleScopeFunctions(IEnumerable<string> sourcePaths)
    {
        foreach (var sourcePath in sourcePaths.Distinct(PowerShellCompilationPathSafety.PathComparer))
        {
            Token[] tokens;
            ParseError[] errors;
            var ast = Parser.ParseFile(sourcePath, out tokens, out errors);
            if (errors.Length > 0)
                throw new InvalidOperationException($"Module source '{sourcePath}' could not be parsed while composing fallback exports.");
            foreach (var function in ast.FindAll(static node => node is FunctionDefinitionAst, searchNestedScriptBlocks: false).Cast<FunctionDefinitionAst>())
                yield return new ModuleScopeFunction(sourcePath, function.Name, function.Body.Extent.StartLineNumber);
        }
    }

    internal static string GetCompiledMethodKey(string sourcePath, string name, int line)
        => Path.GetFullPath(sourcePath) + "\0" + name + "\0" + line;

    private static string JoinPowerShellNames(IEnumerable<string> names)
        => string.Join(", ", names.Select(name => "'" + EscapePowerShellSingleQuotedString(name) + "'"));

    private static string EscapePowerShellSingleQuotedString(string value)
        => value.Replace("'", "''");

    private sealed class ModuleScopeFunction
    {
        internal ModuleScopeFunction(string path, string name, int line)
        {
            Path = path;
            Name = name;
            Line = line;
        }

        internal string Path { get; }
        internal string Name { get; }
        internal int Line { get; }
    }

    private sealed class RuntimeRegionVariableNames
    {
        internal RuntimeRegionVariableNames(
            string runspaceId,
            string module,
            string previousOnRemove,
            string cleanup,
            string installedOnRemove,
            string effectiveOnRemove,
            string initializationFailed)
        {
            RunspaceId = runspaceId;
            Module = module;
            PreviousOnRemove = previousOnRemove;
            Cleanup = cleanup;
            InstalledOnRemove = installedOnRemove;
            EffectiveOnRemove = effectiveOnRemove;
            InitializationFailed = initializationFailed;
        }

        internal string RunspaceId { get; }
        internal string Module { get; }
        internal string PreviousOnRemove { get; }
        internal string Cleanup { get; }
        internal string InstalledOnRemove { get; }
        internal string EffectiveOnRemove { get; }
        internal string InitializationFailed { get; }
    }
}
