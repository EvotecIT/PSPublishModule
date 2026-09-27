namespace PowerForge.Generated.Runtime
{
    using System;
    using System.Collections.Generic;
    using System.Linq;
    using System.Management.Automation;
    using System.Management.Automation.Language;
    using System.Reflection;
    using System.Runtime.CompilerServices;

    public static partial class PowerShellNativeFunctionHost
    {
        /// <summary>Shares the bounded enum declaration shape between semantic admission and delivered native frames.</summary>
        internal static bool IsSupportedFunctionEnum(TypeDefinitionAst definition)
            => definition.IsEnum && definition.BaseTypes.Count == 0 &&
               definition.Attributes.All(attribute => attribute.TypeName.GetReflectionAttributeType() == typeof(FlagsAttribute) &&
                   attribute.PositionalArguments.Count == 0 && attribute.NamedArguments.Count == 0) &&
               definition.Members.All(member => member is PropertyMemberAst property && property.Attributes.Count == 0 &&
                   (property.InitialValue == null || property.InitialValue is ConstantExpressionAst { Value: int }));

        private static string FunctionEnumSource(string[]? declarations)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var source = string.Empty;
            foreach (var declaration in declarations ?? Array.Empty<string>())
            {
                var ast = Parse(declaration, null);
                if (ast.ParamBlock != null || ast.BeginBlock != null || ast.ProcessBlock != null || ast.DynamicParamBlock != null ||
                    HasUsingStatements(ast) || ast.ScriptRequirements != null || ast.EndBlock?.Statements.Count != 1 ||
                    ast.EndBlock.Statements[0] is not TypeDefinitionAst definition || definition.Extent.Text != declaration ||
                    !IsSupportedFunctionEnum(definition) || !names.Add(definition.Name))
                    throw new ArgumentException("The native frame accepts only distinct bounded enum declarations.", nameof(declarations));
                source += declaration + "\n";
            }
            return source;
        }

        private static readonly Lazy<MethodInfo> AddFunctionTypesToScope = new(() => typeof(PSObject).Assembly
            .GetType("System.Management.Automation.TypeOps", true)!
            .GetMethod("AddPowerShellTypesToTheScope", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new NotSupportedException("PowerShell's lexical type-scope owner is unavailable."));

        // Cloned native blocks share compilation data. Associate only explicitly validated
        // compiler-owned frames with that data, without retaining unloaded functions globally.
        private static readonly ConditionalWeakTable<object, FunctionEnumScope> FunctionEnumScopes = new();

        private sealed class FunctionEnumScope
        {
            internal FunctionEnumScope(Dictionary<string, TypeDefinitionAst> types) => Types = types;
            internal Dictionary<string, TypeDefinitionAst> Types { get; }
        }

        private static void RegisterFunctionEnumScope(ScriptBlock script, object data)
        {
            var root = script.Ast is FunctionDefinitionAst function ? function.Body : script.Ast as ScriptBlockAst;
            var definitions = root?.EndBlock?.Statements.OfType<TypeDefinitionAst>().ToArray() ?? Array.Empty<TypeDefinitionAst>();
            if (definitions.Length == 0) return;
            if (definitions.Any(definition => !IsSupportedFunctionEnum(definition)))
                throw new NotSupportedException("The native function's enum declarations are outside its qualified contract.");
            var types = definitions.ToDictionary(definition => definition.Name);
            FunctionEnumScopes.GetValue(data, _ => new FunctionEnumScope(types));
        }

        internal static void InitializeFunctionEnums(ScriptBlock script, object executionContext)
        {
            if (!FunctionEnumScopes.TryGetValue(NativeContract.Shared.Data.GetValue(script)!, out var scope)) return;
            // Mirror the SDK's lexical initialization only for the new validated stub.
            // Existing authored declaration and script-block callback contracts remain untouched.
            Invoke(AddFunctionTypesToScope.Value, null, new object[] { scope.Types, executionContext });
        }
    }
}
