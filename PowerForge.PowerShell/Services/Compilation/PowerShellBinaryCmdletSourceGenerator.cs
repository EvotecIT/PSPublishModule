using System.Management.Automation;
using System.Management.Automation.Language;
using System.Reflection;
using System.Text;

namespace PowerForge;

internal static partial class PowerShellBinaryCmdletSourceGenerator
{
    private const string RemainingArgumentsMemberName = "__PowerForgeRemainingArguments";
    private const string InvariantParameterAttributeName = "__PowerForgeInvariantParameterAttribute";
    private static readonly HashSet<string> CommandRegionMemberNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "InvokePowerShellRegion",
        "CapturePowerShellRegion",
        "NormalizeCapturedPowerShellValue"
    };
    private static readonly HashSet<string> HostedLifecycleMemberNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "__powerForgePipeline",
        "__powerForgeLifecycleGate",
        "__powerForgeCleaned",
        "__powerForgeStopRequested",
        "__powerForgeStopCompletionStarted",
        "__powerForgeCleanupTask",
        "__powerForgeCleanupCompletion",
        "__powerForgeRunspace",
        "__powerForgeAvailabilityChanged",
        "__powerForgeRunspaceStateChanged",
        "__powerForgePipelineInputExplicitlyBound",
        "__powerForgeCurrentPipelineObjectProperty",
        "__powerForgeCurrentPipelineObjectField",
        "GetCurrentPipelineObject",
        "GetLifecyclePipeline",
        "StopLifecycle",
        "CompleteStoppedLifecycle",
        "DetachStoppedLifecycleHandlers",
        "DisposeStoppedLifecycle",
        "InvokeLifecycleClean",
        "WriteLifecycleOutput",
        "CleanLifecycle",
        "Dispose"
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
        string? targetFramework,
        string semanticProfileId = PowerShellCompilationSemanticOracleCatalog.PowerShell76ProfileId,
        PowerShellCompilationCapability capabilities = PowerShellCompilationCapabilities.BinaryModule)
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
        var filtered = new PowerShellTypedCompilationTranspiler(Array.Empty<PowerShellCompilationCommandProviderContract>(), semanticProfileId).TranspileExcluding(
            typed.SourcePaths,
            typed.NamespaceName,
            typed.TypeName,
            targetFramework,
            invalid,
            capabilities);
        var result = new PowerShellTypedCompilationResult(
            filtered.SourcePath,
            filtered.NamespaceName,
            filtered.TypeName,
            filtered.SourceCode,
            filtered.Methods,
            filtered.Diagnostics.Concat(diagnostics)
                .OrderBy(static diagnostic => diagnostic.Line)
                .ThenBy(static diagnostic => diagnostic.Column)
                .ToArray(),
            filtered.SourcePaths,
            lifecycleSources: null,
            optimization: filtered.Optimization,
            irSnapshots: filtered.IrSnapshots);
        result.PromotedRegions = filtered.PromotedRegions;
        result.RegionCandidates = filtered.RegionCandidates;
        result.RegionOpportunities = filtered.RegionOpportunities;
        return result;
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
        AppendRuntimeHost(builder, typed, cmdlets);
        foreach (var cmdlet in cmdlets)
            AppendCmdlet(builder, typed, cmdlet, targetFramework);
        return builder.ToString();
    }

    internal static string GetRuntimeRegionHostTypeName(PowerShellTypedCompilationResult typed)
        => PowerShellCSharpSymbolRenderer.Identifier(typed.TypeName + "PowerShellRegionHost");

    private static CmdletDescriptor CreateDescriptor(PowerShellCompiledMethod method)
    {
        var separator = method.SourceName.IndexOf('-');
        if (separator < 1 || separator == method.SourceName.Length - 1)
            throw new InvalidOperationException($"Function '{method.SourceName}' cannot be exported as a binary cmdlet because it does not use Verb-Noun naming.");
        var verb = method.SourceName.Substring(0, separator);
        var noun = method.SourceName.Substring(separator + 1);
        return new CmdletDescriptor(method, verb, noun, PowerShellCSharpSymbolRenderer.Identifier(verb + noun + "Command"));
    }

    private static void ValidateDescriptor(CmdletDescriptor cmdlet, string? targetFramework)
    {
        if (!Compilation.Build.PowerShellCommandMetadataNames.CanRepresent(cmdlet.Method.SourceName))
            throw new InvalidOperationException($"Function '{cmdlet.Method.SourceName}' cannot preserve its command error identity as a CLR metadata name.");
        var renamedParameter = cmdlet.Method.Parameters.FirstOrDefault(parameter =>
        {
            var memberName = PowerShellCSharpSymbolRenderer.Identifier(parameter.Name);
            return !memberName.TrimStart('@').Equals(parameter.Name, StringComparison.OrdinalIgnoreCase);
        });
        if (renamedParameter is not null)
            throw new InvalidOperationException($"Function '{cmdlet.Method.SourceName}' parameter '${renamedParameter.Name}' cannot preserve its PowerShell name as binary-cmdlet metadata after CLR identifier normalization.");
        var commonParameter = cmdlet.Method.Parameters.FirstOrDefault(parameter => CommonParameterNames.Contains(parameter.Name));
        if (commonParameter is not null)
            throw new InvalidOperationException($"Function '{cmdlet.Method.SourceName}' parameter '${commonParameter.Name}' collides with a PowerShell common parameter and cannot be exported as a binary cmdlet.");
        var reservedParameter = cmdlet.Method.Parameters.FirstOrDefault(parameter =>
        {
            var memberName = PowerShellCSharpSymbolRenderer.Identifier(parameter.Name);
            return ReservedMemberNames.Contains(memberName) ||
                   (!cmdlet.Method.IsAdvancedFunction && memberName.Equals(RemainingArgumentsMemberName, StringComparison.OrdinalIgnoreCase)) ||
                   memberName.Equals(cmdlet.ClassName, StringComparison.OrdinalIgnoreCase) ||
                   cmdlet.Method.RequiresProviderCancellation && ProviderCancellationMemberNames.Contains(memberName) ||
                   cmdlet.Method.RequiresPowerShellCommandRegions && CommandRegionMemberNames.Contains(memberName) ||
                   cmdlet.Method.RequiresPowerShellRuntimeState && memberName.Equals("CaptureRuntimeState", StringComparison.OrdinalIgnoreCase) ||
                   cmdlet.Method.RequiresPowerShellModuleStateRead && memberName.Equals("ReadPowerShellModuleVariable", StringComparison.OrdinalIgnoreCase) ||
                   cmdlet.Method.RequiresPowerShellModuleStateWrite && memberName.Equals("WritePowerShellModuleVariable", StringComparison.OrdinalIgnoreCase) ||
                   cmdlet.Method.Lifecycle?.Execution == PowerShellCompilationLifecycleExecution.HostedSteppablePipeline && HostedLifecycleMemberNames.Contains(memberName);
        });
        if (reservedParameter is not null)
            throw new InvalidOperationException($"Function '{cmdlet.Method.SourceName}' parameter '${reservedParameter.Name}' collides with generated or inherited binary-cmdlet member '{PowerShellCSharpSymbolRenderer.Identifier(reservedParameter.Name)}'.");
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
            var effective = CloneBinding(binding, binding.ParameterSetName, position);
            return effective;
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
            return PowerShellCSharpSymbolRenderer.TypeName(resolved);
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
