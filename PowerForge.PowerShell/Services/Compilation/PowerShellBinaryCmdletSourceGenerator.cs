using System.Management.Automation;
using System.Management.Automation.Language;
using System.Reflection;
using System.Text;

namespace PowerForge;

internal static class PowerShellBinaryCmdletSourceGenerator
{
    private const string RemainingArgumentsMemberName = "__PowerForgeRemainingArguments";
    private const string InvariantParameterAttributeName = "__PowerForgeInvariantParameterAttribute";
    private static readonly HashSet<string> CommandRegionMemberNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "InvokePowerShellRegion",
        "CapturePowerShellRegion",
        "NormalizeCapturedPowerShellValue"
    };
    private static readonly HashSet<string> CommonParameterNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "Verbose", "Debug", "ErrorAction", "WarningAction", "InformationAction", "ProgressAction",
        "ErrorVariable", "WarningVariable", "InformationVariable", "OutVariable", "OutBuffer", "PipelineVariable",
        "WhatIf", "Confirm", "UseTransaction"
    };

    private static readonly HashSet<string> ReservedMemberNames = typeof(PSCmdlet)
        .GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static)
        .Select(static member => member.Name)
        .Append("ProcessRecord")
        .ToHashSet(StringComparer.OrdinalIgnoreCase);

    internal static PowerShellTypedCompilationResult PrepareForBinaryModule(
        PowerShellTypedCompilationResult typed,
        string[]? exportedFunctions,
        string? targetFramework)
    {
        var selected = exportedFunctions?.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var invalid = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var diagnostics = new List<PowerShellCompilationDiagnostic>();
        var descriptors = new List<CmdletDescriptor>();
        foreach (var method in typed.Methods.Where(method => selected is null || selected.Contains(method.SourceName)))
        {
            try
            {
                var descriptor = CreateDescriptor(method);
                ValidateDescriptor(descriptor, targetFramework);
                descriptors.Add(descriptor);
            }
            catch (InvalidOperationException ex)
            {
                invalid.Add(GetMethodKey(method));
                diagnostics.Add(CreateDiagnostic(typed, method, ex.Message));
            }
        }

        foreach (var duplicateClass in descriptors
                     .Where(descriptor => !invalid.Contains(GetMethodKey(descriptor.Method)))
                     .GroupBy(static descriptor => descriptor.ClassName, StringComparer.OrdinalIgnoreCase)
                     .Where(static group => group.Count() > 1))
        {
            var message = $"Functions {string.Join(", ", duplicateClass.Select(static cmdlet => $"'{cmdlet.Method.SourceName}'"))} generate duplicate binary-cmdlet class '{duplicateClass.Key}'.";
            foreach (var descriptor in duplicateClass)
            {
                invalid.Add(GetMethodKey(descriptor.Method));
                diagnostics.Add(CreateDiagnostic(typed, descriptor.Method, message));
            }
        }

        foreach (var descriptor in descriptors.Where(descriptor =>
                     !invalid.Contains(GetMethodKey(descriptor.Method)) &&
                     descriptor.ClassName.Equals(typed.TypeName, StringComparison.OrdinalIgnoreCase)))
        {
            invalid.Add(GetMethodKey(descriptor.Method));
            diagnostics.Add(CreateDiagnostic(
                typed,
                descriptor.Method,
                $"Function '{descriptor.Method.SourceName}' generates binary-cmdlet class '{descriptor.ClassName}', which collides with compiled method container '{typed.TypeName}'."));
        }

        if (invalid.Count == 0)
            return typed;
        var filtered = new PowerShellTypedCompilationTranspiler().TranspileExcluding(
            typed.SourcePaths,
            typed.NamespaceName,
            typed.TypeName,
            targetFramework,
            invalid,
            PowerShellCompilationCapabilities.BinaryModule);
        return new PowerShellTypedCompilationResult(
            filtered.SourcePath,
            filtered.NamespaceName,
            filtered.TypeName,
            filtered.SourceCode,
            filtered.Methods,
            filtered.Diagnostics.Concat(diagnostics)
                .OrderBy(static diagnostic => diagnostic.Line)
                .ThenBy(static diagnostic => diagnostic.Column)
                .ToArray(),
            filtered.SourcePaths);
    }

    internal static string Generate(
        PowerShellTypedCompilationResult typed,
        string[]? exportedFunctions = null,
        string? targetFramework = null)
    {
        var selected = exportedFunctions?.ToHashSet(StringComparer.OrdinalIgnoreCase);
        var cmdlets = typed.Methods
            .Where(method => selected is null || selected.Contains(method.SourceName))
            .Select(CreateDescriptor)
            .ToArray();

        var duplicateClass = cmdlets
            .GroupBy(static cmdlet => cmdlet.ClassName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicateClass is not null)
            throw new InvalidOperationException($"Functions {string.Join(", ", duplicateClass.Select(static cmdlet => $"'{cmdlet.Method.SourceName}'"))} generate duplicate binary-cmdlet class '{duplicateClass.Key}'.");
        var typeCollision = cmdlets.FirstOrDefault(cmdlet => cmdlet.ClassName.Equals(typed.TypeName, StringComparison.OrdinalIgnoreCase));
        if (typeCollision is not null)
            throw new InvalidOperationException($"Function '{typeCollision.Method.SourceName}' generates binary-cmdlet class '{typeCollision.ClassName}', which collides with compiled method container '{typed.TypeName}'.");

        var builder = new StringBuilder();
        builder.AppendLine("// <auto-generated />");
        builder.AppendLine("#nullable enable");
        builder.AppendLine("using System.Management.Automation;");
        builder.AppendLine();
        builder.AppendLine($"namespace {typed.NamespaceName};");
        builder.AppendLine();
        if (cmdlets.SelectMany(static cmdlet => cmdlet.Method.Parameters).Any(RequiresInvariantParameterConversion))
            AppendInvariantParameterAttribute(builder);
        if (cmdlets.Any(static cmdlet => cmdlet.Method.RequiresPowerShellCommandRegions))
        {
            builder.AppendLine($"public static class {GetRuntimeRegionHostTypeName(typed)}");
            builder.AppendLine("{");
            builder.AppendLine("    private static readonly global::System.Collections.Concurrent.ConcurrentDictionary<global::System.Guid, ScriptBlock> Dispatchers = new();");
            builder.AppendLine("    public static void SetDispatcher(global::System.Guid runspaceId, ScriptBlock dispatcher) => Dispatchers[runspaceId] = dispatcher;");
            builder.AppendLine("    public static ScriptBlock? GetDispatcher(global::System.Guid runspaceId) => Dispatchers.TryGetValue(runspaceId, out var dispatcher) ? dispatcher : null;");
            builder.AppendLine("    public static void ClearDispatcher(global::System.Guid runspaceId) => Dispatchers.TryRemove(runspaceId, out _);");
            builder.AppendLine("}");
            builder.AppendLine();
        }
        foreach (var cmdlet in cmdlets)
            AppendCmdlet(builder, typed, cmdlet, targetFramework);
        return builder.ToString();
    }

    internal static string GetRuntimeRegionHostTypeName(PowerShellTypedCompilationResult typed)
        => PowerShellCSharpMethodEmitter.SanitizeIdentifier(typed.TypeName + "PowerShellRegionHost");

    private static CmdletDescriptor CreateDescriptor(PowerShellCompiledMethod method)
    {
        var separator = method.SourceName.IndexOf('-');
        if (separator < 1 || separator == method.SourceName.Length - 1)
            throw new InvalidOperationException($"Function '{method.SourceName}' cannot be exported as a binary cmdlet because it does not use Verb-Noun naming.");
        var verb = method.SourceName.Substring(0, separator);
        var noun = method.SourceName.Substring(separator + 1);
        return new CmdletDescriptor(method, verb, noun, PowerShellCSharpMethodEmitter.SanitizeIdentifier(verb + noun + "Command"));
    }

    private static void ValidateDescriptor(CmdletDescriptor cmdlet, string? targetFramework)
    {
        var renamedParameter = cmdlet.Method.Parameters.FirstOrDefault(parameter =>
        {
            var memberName = PowerShellCSharpMethodEmitter.SanitizeIdentifier(parameter.Name);
            return !memberName.TrimStart('@').Equals(parameter.Name, StringComparison.OrdinalIgnoreCase);
        });
        if (renamedParameter is not null)
            throw new InvalidOperationException($"Function '{cmdlet.Method.SourceName}' parameter '${renamedParameter.Name}' cannot preserve its PowerShell name as binary-cmdlet metadata after CLR identifier normalization.");
        var commonParameter = cmdlet.Method.Parameters.FirstOrDefault(parameter => CommonParameterNames.Contains(parameter.Name));
        if (commonParameter is not null)
            throw new InvalidOperationException($"Function '{cmdlet.Method.SourceName}' parameter '${commonParameter.Name}' collides with a PowerShell common parameter and cannot be exported as a binary cmdlet.");
        var reservedParameter = cmdlet.Method.Parameters.FirstOrDefault(parameter =>
        {
            var memberName = PowerShellCSharpMethodEmitter.SanitizeIdentifier(parameter.Name);
            return ReservedMemberNames.Contains(memberName) ||
                   (!cmdlet.Method.IsAdvancedFunction && memberName.Equals(RemainingArgumentsMemberName, StringComparison.OrdinalIgnoreCase)) ||
                   memberName.Equals(cmdlet.ClassName, StringComparison.OrdinalIgnoreCase) ||
                   cmdlet.Method.RequiresPowerShellCommandRegions && CommandRegionMemberNames.Contains(memberName);
        });
        if (reservedParameter is not null)
            throw new InvalidOperationException($"Function '{cmdlet.Method.SourceName}' parameter '${reservedParameter.Name}' collides with generated or inherited binary-cmdlet member '{PowerShellCSharpMethodEmitter.SanitizeIdentifier(reservedParameter.Name)}'.");
        if (!cmdlet.Method.IsAdvancedFunction)
        {
            var generatedCommonNames = PowerShellCommonParameterPolicy
                .GetStandard(isAdvanced: true, targetFramework)
                .SelectMany(static parameter => new[] { parameter.Name, parameter.Alias })
                .ToArray();
            var abbreviation = FindNewCommonParameterAbbreviation(cmdlet.Method.Parameters, generatedCommonNames);
            if (abbreviation is not null)
                throw new InvalidOperationException(
                    $"Function '{cmdlet.Method.SourceName}' parameter abbreviation '-{abbreviation}' becomes ambiguous with generated binary-cmdlet common parameters.");
        }

        foreach (var parameter in cmdlet.Method.Parameters)
        {
            var duplicateSet = parameter.Bindings
                .GroupBy(static binding => binding.ParameterSetName, StringComparer.OrdinalIgnoreCase)
                .FirstOrDefault(static group => group.Count() > 1);
            if (duplicateSet is not null)
                throw new InvalidOperationException(
                    $"Function '{cmdlet.Method.SourceName}' parameter '${parameter.Name}' declares duplicate metadata for parameter set '{(string.IsNullOrWhiteSpace(duplicateSet.Key) ? "__AllParameterSets" : duplicateSet.Key)}'.");
        }

        var namedSets = cmdlet.Method.Parameters
            .SelectMany(static parameter => parameter.Bindings)
            .Select(static binding => binding.ParameterSetName)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Append(cmdlet.Method.CommandBinding.DefaultParameterSetName)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        if (namedSets.Length > 32)
            throw new InvalidOperationException($"Function '{cmdlet.Method.SourceName}' declares {namedSets.Length} parameter sets; binary cmdlets support at most 32.");

        var effective = cmdlet.Method.Parameters.SelectMany((parameter, index) =>
            GetEffectiveBindings(cmdlet.Method, parameter, index).Select(binding => new { parameter.Name, Binding = binding })).ToArray();
        var duplicatePosition = effective
            .Where(static item => item.Binding.Position.HasValue)
            .GroupBy(item => item.Binding.ParameterSetName + "\0" + item.Binding.Position!.Value, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(static group => group.Select(item => item.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1);
        if (duplicatePosition is not null)
            throw new InvalidOperationException(
                $"Function '{cmdlet.Method.SourceName}' assigns more than one parameter to the same position in parameter set '{(string.IsNullOrWhiteSpace(duplicatePosition.First().Binding.ParameterSetName) ? "__AllParameterSets" : duplicatePosition.First().Binding.ParameterSetName)}'.");
        var duplicateRemaining = effective
            .Where(static item => item.Binding.ValueFromRemainingArguments)
            .GroupBy(static item => item.Binding.ParameterSetName, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(static group => group.Select(item => item.Name).Distinct(StringComparer.OrdinalIgnoreCase).Count() > 1);
        if (duplicateRemaining is not null)
            throw new InvalidOperationException(
                $"Function '{cmdlet.Method.SourceName}' assigns ValueFromRemainingArguments to more than one parameter in parameter set '{(string.IsNullOrWhiteSpace(duplicateRemaining.Key) ? "__AllParameterSets" : duplicateRemaining.Key)}'.");
    }

    private static string? FindNewCommonParameterAbbreviation(
        IReadOnlyList<PowerShellCompilationParameter> parameters,
        IReadOnlyList<string> generatedCommonNames)
    {
        foreach (var parameter in parameters)
        {
            foreach (var bindingName in new[] { parameter.Name }.Concat(parameter.Aliases))
            {
                for (var length = 1; length < bindingName.Length; length++)
                {
                    var prefix = bindingName.Substring(0, length);
                    var authoredOwners = parameters.Count(candidate =>
                        new[] { candidate.Name }.Concat(candidate.Aliases)
                            .Any(name => name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)));
                    if (authoredOwners == 1 && generatedCommonNames.Any(name =>
                            name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)))
                        return prefix;
                }
            }
        }
        return null;
    }

    private static void AppendCmdlet(
        StringBuilder builder,
        PowerShellTypedCompilationResult typed,
        CmdletDescriptor cmdlet,
        string? targetFramework)
    {
        ValidateDescriptor(cmdlet, targetFramework);

        if (cmdlet.Method.Aliases.Length > 0)
            builder.AppendLine($"[Alias({string.Join(", ", cmdlet.Method.Aliases.Select(PowerShellCSharpLiteral.QuoteString))})]");
        builder.AppendLine(GenerateCmdletAttribute(cmdlet));
        var outputType = string.IsNullOrWhiteSpace(cmdlet.Method.DeclaredOutputType)
            ? GetCmdletOutputTypeName(cmdlet.Method.ReturnType)
            : cmdlet.Method.DeclaredOutputType;
        if (outputType is not null)
            builder.AppendLine($"[OutputType(typeof({GetGeneratedTypeName(outputType)}))]");
        builder.AppendLine($"public sealed class {cmdlet.ClassName} : PSCmdlet");
        builder.AppendLine("{");
        for (var index = 0; index < cmdlet.Method.Parameters.Length; index++)
        {
            var parameter = cmdlet.Method.Parameters[index];
            foreach (var binding in GetEffectiveBindings(cmdlet.Method, parameter, index))
                builder.AppendLine("    " + GenerateParameterAttribute(binding));
            if (parameter.Aliases.Length > 0)
                builder.AppendLine($"    [Alias({string.Join(", ", parameter.Aliases.Select(PowerShellCSharpLiteral.QuoteString))})]");
            var propertyType = parameter.IsSwitch ? "SwitchParameter" : GetGeneratedTypeName(parameter.TypeName);
            if (RequiresInvariantParameterConversion(parameter))
                builder.AppendLine($"    [{InvariantParameterAttributeName}(typeof({propertyType}))]");
            if (parameter.AllowNull)
                builder.AppendLine("    [AllowNull]");
            if (parameter.AllowEmptyString)
                builder.AppendLine("    [AllowEmptyString]");
            if (parameter.AllowEmptyCollection)
                builder.AppendLine("    [AllowEmptyCollection]");
            if (parameter.SupportsWildcards)
                builder.AppendLine("    [SupportsWildcards]");
            foreach (var validation in parameter.Validations.Where(validation => ShouldGenerateValidationAttribute(parameter, validation)))
                builder.AppendLine("    " + GenerateValidationAttribute(validation));
            var initializer = parameter.IsSwitch || IsGeneratedValueType(parameter.TypeName)
                ? string.Empty
                : parameter.TypeName == typeof(string).FullName
                    ? " = string.Empty;"
                    : " = default!;";
            builder.AppendLine($"    public {propertyType} {PowerShellCSharpMethodEmitter.SanitizeIdentifier(parameter.Name)} {{ get; set; }}{initializer}");
            builder.AppendLine();
        }
        if (!cmdlet.Method.IsAdvancedFunction)
        {
            builder.AppendLine("    [Parameter(ValueFromRemainingArguments = true, DontShow = true)]");
            builder.AppendLine($"    public object[] {RemainingArgumentsMemberName} {{ get; set; }} = global::System.Array.Empty<object>();");
            builder.AppendLine();
        }
        if (cmdlet.Method.RequiresPowerShellCommandRegions)
        {
            builder.AppendLine("    private void InvokePowerShellRegion(string script, object?[] arguments)");
            builder.AppendLine("    {");
            builder.AppendLine("        var runspaceId = global::System.Management.Automation.Runspaces.Runspace.DefaultRunspace?.InstanceId ?? global::System.Guid.Empty;");
            builder.AppendLine($"        var dispatcher = {GetRuntimeRegionHostTypeName(typed)}.GetDispatcher(runspaceId);");
            builder.AppendLine("        var values = dispatcher is null");
            builder.AppendLine("            ? InvokeCommand.InvokeScript(SessionState, ScriptBlock.Create(script), arguments)");
            builder.AppendLine("            : dispatcher.Invoke(script, arguments);");
            builder.AppendLine("        foreach (var value in values)");
            builder.AppendLine("            WriteObject(value, enumerateCollection: false);");
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.AppendLine("    private object? CapturePowerShellRegion(string script, object?[] arguments)");
            builder.AppendLine("    {");
            builder.AppendLine("        var runspaceId = global::System.Management.Automation.Runspaces.Runspace.DefaultRunspace?.InstanceId ?? global::System.Guid.Empty;");
            builder.AppendLine($"        var dispatcher = {GetRuntimeRegionHostTypeName(typed)}.GetDispatcher(runspaceId);");
            builder.AppendLine("        var values = dispatcher is null");
            builder.AppendLine("            ? InvokeCommand.InvokeScript(SessionState, ScriptBlock.Create(script), arguments)");
            builder.AppendLine("            : dispatcher.Invoke(script, arguments);");
            builder.AppendLine("        if (values.Count == 0) return null;");
            builder.AppendLine("        if (values.Count == 1) return NormalizeCapturedPowerShellValue(values[0]);");
            builder.AppendLine("        var captured = new object?[values.Count];");
            builder.AppendLine("        for (var index = 0; index < values.Count; index++)");
            builder.AppendLine("            captured[index] = NormalizeCapturedPowerShellValue(values[index]);");
            builder.AppendLine("        return captured;");
            builder.AppendLine("    }");
            builder.AppendLine();
            builder.AppendLine("    private static object? NormalizeCapturedPowerShellValue(global::System.Management.Automation.PSObject? value)");
            builder.AppendLine("    {");
            builder.AppendLine("        if (value is null) return null;");
            builder.AppendLine("        var baseObject = value.BaseObject;");
            builder.AppendLine("        if (baseObject is null) return value;");
            builder.AppendLine("        if (baseObject is global::System.Management.Automation.PSCustomObject) return value;");
            builder.AppendLine("        var baseTypeName = baseObject.GetType().FullName;");
            builder.AppendLine("        if (value.TypeNames.Count > 0 && !global::System.String.Equals(value.TypeNames[0], baseTypeName, global::System.StringComparison.Ordinal)) return value;");
            builder.AppendLine("        foreach (var member in value.Members)");
            builder.AppendLine("        {");
            builder.AppendLine("            if (!member.IsInstance) continue;");
            builder.AppendLine("            if (member.MemberType != global::System.Management.Automation.PSMemberTypes.Property &&");
            builder.AppendLine("                member.MemberType != global::System.Management.Automation.PSMemberTypes.Method &&");
            builder.AppendLine("                member.MemberType != global::System.Management.Automation.PSMemberTypes.ParameterizedProperty &&");
            builder.AppendLine("                member.MemberType != global::System.Management.Automation.PSMemberTypes.Event)");
            builder.AppendLine("                return value;");
            builder.AppendLine("        }");
            builder.AppendLine("        return baseObject;");
            builder.AppendLine("    }");
            builder.AppendLine();
        }
        var lifecycleMethod = cmdlet.Method.Parameters.Any(static parameter => parameter.AcceptsPipelineInput)
            ? "EndProcessing"
            : "ProcessRecord";
        builder.AppendLine($"    protected override void {lifecycleMethod}()");
        builder.AppendLine("    {");
        var arguments = cmdlet.Method.Parameters.Select(parameter =>
            PowerShellCSharpMethodEmitter.SanitizeIdentifier(parameter.Name) + (parameter.IsSwitch ? ".IsPresent" : string.Empty));
        if (cmdlet.Method.RequiresPowerShellStreams)
            arguments = arguments.Concat(new[] { "WriteVerbose", "WriteDebug", "WriteWarning" });
        if (cmdlet.Method.RequiresPowerShellCommandRegions)
            arguments = arguments.Concat(new[] { "InvokePowerShellRegion", "CapturePowerShellRegion" });
        if (cmdlet.Method.RequiresPowerShellRuntimeState)
        {
            arguments = arguments.Concat(new[]
            {
                "target => ShouldProcess(target)",
                "(target, action) => ShouldProcess(target, action)",
                "((global::System.Collections.IDictionary)SessionState.PSVariable.GetValue(\"PSVersionTable\"))[\"PSVersion\"]!",
                "MyInvocation.BoundParameters.ContainsKey(\"WhatIf\") ? global::System.Management.Automation.LanguagePrimitives.IsTrue(MyInvocation.BoundParameters[\"WhatIf\"]) : global::System.Management.Automation.LanguagePrimitives.IsTrue(SessionState.PSVariable.GetValue(\"WhatIfPreference\"))"
            });
        }
        if (cmdlet.Method.RequiresPowerShellBoundParameters)
            arguments = arguments.Append("new global::System.Collections.Generic.HashSet<string>(MyInvocation.BoundParameters.Keys, global::System.StringComparer.OrdinalIgnoreCase)");
        var invocation = $"{typed.TypeName}.{cmdlet.Method.GeneratedName}({string.Join(", ", arguments)})";
        if (cmdlet.Method.ReturnType.Equals(typeof(void).FullName, StringComparison.Ordinal))
            builder.AppendLine($"        {invocation};");
        else
            builder.AppendLine($"        WriteObject({invocation}, enumerateCollection: true);");
        builder.AppendLine("    }");
        builder.AppendLine("}");
        builder.AppendLine();
    }

    private static void AppendInvariantParameterAttribute(StringBuilder builder)
    {
        builder.AppendLine("[global::System.AttributeUsage(global::System.AttributeTargets.Property)]");
        builder.AppendLine($"public sealed class {InvariantParameterAttributeName} : ArgumentTransformationAttribute");
        builder.AppendLine("{");
        builder.AppendLine("    private readonly global::System.Type _targetType;");
        builder.AppendLine($"    public {InvariantParameterAttributeName}(global::System.Type targetType) => _targetType = targetType;");
        builder.AppendLine("    public override object Transform(EngineIntrinsics engineIntrinsics, object inputData)");
        builder.AppendLine("        => LanguagePrimitives.ConvertTo(inputData, _targetType, global::System.Globalization.CultureInfo.InvariantCulture);");
        builder.AppendLine("}");
        builder.AppendLine();
    }

    private static bool RequiresInvariantParameterConversion(PowerShellCompilationParameter parameter)
    {
        if (parameter.IsSwitch)
            return false;
        var type = Type.GetType(parameter.TypeName, throwOnError: false);
        if (type is null)
            return false;
        if (type.IsArray)
            type = type.GetElementType()!;
        type = Nullable.GetUnderlyingType(type) ?? type;
        return type == typeof(byte) || type == typeof(sbyte) ||
               type == typeof(short) || type == typeof(ushort) ||
               type == typeof(int) || type == typeof(uint) ||
               type == typeof(long) || type == typeof(ulong) ||
               type == typeof(float) || type == typeof(double) ||
               type == typeof(decimal) || type == typeof(DateTime) ||
               type == typeof(DateTimeOffset) || type == typeof(TimeSpan);
    }

    private static string GenerateCmdletAttribute(CmdletDescriptor cmdlet)
    {
        var arguments = new List<string>
        {
            PowerShellCSharpLiteral.QuoteString(cmdlet.Verb),
            PowerShellCSharpLiteral.QuoteString(cmdlet.Noun)
        };
        var binding = cmdlet.Method.CommandBinding;
        if (!string.IsNullOrWhiteSpace(binding.DefaultParameterSetName))
            arguments.Add("DefaultParameterSetName = " + PowerShellCSharpLiteral.QuoteString(binding.DefaultParameterSetName));
        if (binding.SupportsShouldProcess)
            arguments.Add("SupportsShouldProcess = true");
        if (!string.IsNullOrWhiteSpace(binding.ConfirmImpact))
        {
            if (!Enum.TryParse<ConfirmImpact>(binding.ConfirmImpact, ignoreCase: true, out var impact))
                throw new InvalidOperationException($"Function '{cmdlet.Method.SourceName}' declares unsupported ConfirmImpact '{binding.ConfirmImpact}'.");
            arguments.Add("ConfirmImpact = ConfirmImpact." + impact);
        }
        return "[Cmdlet(" + string.Join(", ", arguments) + ")]";
    }

    private static PowerShellCompilationParameterBinding[] GetEffectiveBindings(
        PowerShellCompiledMethod method,
        PowerShellCompilationParameter parameter,
        int parameterIndex)
    {
        var namedSets = method.Parameters
            .SelectMany(static candidate => candidate.Bindings)
            .Select(static binding => binding.ParameterSetName)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Append(method.CommandBinding.DefaultParameterSetName)
            .Where(static name => !string.IsNullOrWhiteSpace(name))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var sets = namedSets.Length == 0 ? new[] { string.Empty } : namedSets;
        var expanded = parameter.Bindings.SelectMany(binding =>
            string.IsNullOrWhiteSpace(binding.ParameterSetName)
                ? sets.Select(setName => CloneBinding(binding, setName))
                : new[] { binding });
        return expanded.Select(binding =>
        {
            var hasExplicitPosition = method.Parameters
                .SelectMany(candidate => candidate.Bindings)
                .Any(candidate => BindingAppliesToSet(candidate, binding.ParameterSetName) && candidate.Position.HasValue);
            var position = binding.Position;
            if (!position.HasValue && method.CommandBinding.PositionalBinding && !hasExplicitPosition)
                position = GetImplicitPosition(method, parameterIndex, binding.ParameterSetName);
            return CloneBinding(binding, binding.ParameterSetName, position);
        }).ToArray();
    }

    private static int GetImplicitPosition(PowerShellCompiledMethod method, int parameterIndex, string setName)
        => method.Parameters
            .Take(parameterIndex)
            .Count(parameter => parameter.Bindings.Any(binding => BindingAppliesToSet(binding, setName)));

    private static bool BindingAppliesToSet(PowerShellCompilationParameterBinding binding, string setName)
        => string.IsNullOrWhiteSpace(binding.ParameterSetName) ||
           binding.ParameterSetName.Equals(setName, StringComparison.OrdinalIgnoreCase);

    private static PowerShellCompilationParameterBinding CloneBinding(
        PowerShellCompilationParameterBinding binding,
        string parameterSetName,
        int? position = null)
        => new(
            parameterSetName,
            binding.Mandatory,
            position ?? binding.Position,
            binding.ValueFromPipeline,
            binding.ValueFromPipelineByPropertyName,
            binding.ValueFromRemainingArguments,
            binding.DontShow,
            binding.HelpMessage);

    private static string GenerateParameterAttribute(PowerShellCompilationParameterBinding binding)
    {
        var arguments = new List<string>();
        if (!string.IsNullOrWhiteSpace(binding.ParameterSetName))
            arguments.Add("ParameterSetName = " + PowerShellCSharpLiteral.QuoteString(binding.ParameterSetName));
        if (binding.Mandatory)
            arguments.Add("Mandatory = true");
        if (binding.Position.HasValue)
            arguments.Add("Position = " + binding.Position.Value.ToString(System.Globalization.CultureInfo.InvariantCulture));
        if (binding.ValueFromPipeline)
            arguments.Add("ValueFromPipeline = true");
        if (binding.ValueFromPipelineByPropertyName)
            arguments.Add("ValueFromPipelineByPropertyName = true");
        if (binding.ValueFromRemainingArguments)
            arguments.Add("ValueFromRemainingArguments = true");
        if (binding.DontShow)
            arguments.Add("DontShow = true");
        if (!string.IsNullOrWhiteSpace(binding.HelpMessage))
            arguments.Add("HelpMessage = " + PowerShellCSharpLiteral.QuoteString(binding.HelpMessage));
        return "[Parameter(" + string.Join(", ", arguments) + ")]";
    }

    private static string GenerateValidationAttribute(PowerShellCompilationValidation validation)
        => validation.Kind switch
        {
            PowerShellCompilationValidationKind.NotNull => "[ValidateNotNull]",
            PowerShellCompilationValidationKind.NotNullOrEmpty => "[ValidateNotNullOrEmpty]",
            PowerShellCompilationValidationKind.Set => $"[ValidateSet({string.Join(", ", validation.Arguments.Select(PowerShellCSharpLiteral.QuoteString))})]",
            PowerShellCompilationValidationKind.Pattern => $"[ValidatePattern({PowerShellCSharpLiteral.QuoteString(validation.Arguments.Single())})]",
            PowerShellCompilationValidationKind.Range => $"[ValidateRange({string.Join(", ", validation.Arguments)})]",
            _ => throw new InvalidOperationException($"Unsupported generated validation kind '{validation.Kind}'.")
        };

    private static bool ShouldGenerateValidationAttribute(
        PowerShellCompilationParameter parameter,
        PowerShellCompilationValidation validation)
        => validation.Kind != PowerShellCompilationValidationKind.NotNull ||
           parameter.TypeName != typeof(string[]).FullName;

    private static string GetGeneratedTypeName(string fullName)
    {
        var resolved = Type.GetType(fullName, throwOnError: false);
        if (resolved is not null)
            return PowerShellCSharpMethodEmitter.GetTypeName(resolved);
        if (fullName.EndsWith("[]", StringComparison.Ordinal))
            return GetGeneratedTypeName(fullName.Substring(0, fullName.Length - 2)) + "[]";
        if (fullName == typeof(void).FullName) return "void";
        if (fullName == typeof(bool).FullName) return "bool";
        if (fullName == typeof(byte).FullName) return "byte";
        if (fullName == typeof(sbyte).FullName) return "sbyte";
        if (fullName == typeof(short).FullName) return "short";
        if (fullName == typeof(ushort).FullName) return "ushort";
        if (fullName == typeof(int).FullName) return "int";
        if (fullName == typeof(uint).FullName) return "uint";
        if (fullName == typeof(long).FullName) return "long";
        if (fullName == typeof(ulong).FullName) return "ulong";
        if (fullName == typeof(float).FullName) return "float";
        if (fullName == typeof(double).FullName) return "double";
        if (fullName == typeof(decimal).FullName) return "decimal";
        if (fullName == typeof(char).FullName) return "char";
        if (fullName == typeof(string).FullName) return "string";
        return "global::" + fullName.Replace('+', '.');
    }

    private static bool IsGeneratedValueType(string fullName)
    {
        var resolved = Type.GetType(fullName, throwOnError: false);
        return resolved?.IsValueType == true || fullName == typeof(bool).FullName;
    }

    private static string? GetCmdletOutputTypeName(string returnType)
    {
        if (returnType.Equals(typeof(void).FullName, StringComparison.Ordinal))
            return null;
        if (returnType.EndsWith("[]", StringComparison.Ordinal))
            return returnType.Substring(0, returnType.Length - 2);
        var type = Type.GetType(returnType, throwOnError: false);
        if (type is null)
            return typeof(object).FullName;
        if (typeof(System.Collections.IDictionary).IsAssignableFrom(type))
            return returnType;
        return type != typeof(string) && typeof(System.Collections.IEnumerable).IsAssignableFrom(type)
            ? typeof(object).FullName
            : returnType;
    }

    private static PowerShellCompilationDiagnostic CreateDiagnostic(
        PowerShellTypedCompilationResult typed,
        PowerShellCompiledMethod method,
        string message)
    {
        var sourcePath = string.IsNullOrWhiteSpace(method.SourcePath) ? typed.SourcePath : method.SourcePath;
        return new PowerShellCompilationDiagnostic(
            PowerShellCompilationDiagnosticCode.UnsupportedSyntax,
            message,
            sourcePath,
            method.SourceLine,
            GetFunctionSourceColumn(sourcePath, method),
            PowerShellCompilationFeatureIds.BinaryCmdletShape);
    }

    private static int GetFunctionSourceColumn(string sourcePath, PowerShellCompiledMethod method)
    {
        if (!File.Exists(sourcePath))
            return 1;
        try
        {
            return Parser.ParseFile(sourcePath, out _, out _)
                       .FindAll(static node => node is FunctionDefinitionAst, searchNestedScriptBlocks: false)
                       .OfType<FunctionDefinitionAst>()
                       .FirstOrDefault(function =>
                           function.Name.Equals(method.SourceName, StringComparison.OrdinalIgnoreCase) &&
                           function.Body.Extent.StartLineNumber == method.SourceLine)
                       ?.Body.Extent.StartColumnNumber ?? 1;
        }
        catch (IOException)
        {
            return 1;
        }
        catch (UnauthorizedAccessException)
        {
            return 1;
        }
    }

    private static string GetMethodKey(PowerShellCompiledMethod method)
        => method.SourcePath + "\0" + method.SourceName + "\0" + method.SourceLine;

    private sealed class CmdletDescriptor
    {
        internal CmdletDescriptor(PowerShellCompiledMethod method, string verb, string noun, string className)
        {
            Method = method;
            Verb = verb;
            Noun = noun;
            ClassName = className;
        }

        internal PowerShellCompiledMethod Method { get; }
        internal string Verb { get; }
        internal string Noun { get; }
        internal string ClassName { get; }
    }
}
