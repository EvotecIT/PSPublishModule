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

        internal static bool IsSupportedFunctionType(TypeDefinitionAst definition)
            => IsSupportedFunctionEnum(definition) ||
               definition.IsClass && definition.BaseTypes.Count == 0 && definition.Attributes.Count == 0 &&
               definition.Members.All(member => member is PropertyMemberAst property && property.IsPublic &&
                   property.PropertyType?.TypeName.GetReflectionType() is { } type && IsSupportedDataPropertyType(type) &&
                   (property.InitialValue is null or ConstantExpressionAst) &&
                   property.Attributes.All(attribute =>
                       attribute.TypeName.GetReflectionAttributeType() == typeof(ValidateSetAttribute) &&
                       attribute.NamedArguments.Count == 0 && attribute.PositionalArguments.Count > 0 &&
                       attribute.PositionalArguments.All(argument => argument is StringConstantExpressionAst)));

        private static bool IsSupportedDataPropertyType(Type type)
            => type == typeof(string) || type == typeof(bool) || type == typeof(int) || type == typeof(long) ||
               type == typeof(ScriptBlock) || type == typeof(Uri) ||
               type.IsArray && type.GetArrayRank() == 1 && type.GetElementType() is { IsArray: false } element &&
               IsSupportedDataPropertyType(element);

        private static string FunctionTypeSource(string[]? declarations)
        {
            var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            var source = string.Empty;
            foreach (var declaration in declarations ?? Array.Empty<string>())
            {
                var ast = Parse(declaration, null);
                if (ast.ParamBlock != null || ast.BeginBlock != null || ast.ProcessBlock != null || ast.DynamicParamBlock != null ||
                    HasUsingStatements(ast) || ast.ScriptRequirements != null || ast.EndBlock?.Statements.Count != 1 ||
                    ast.EndBlock.Statements[0] is not TypeDefinitionAst definition || definition.Extent.Text != declaration ||
                    !IsSupportedFunctionType(definition) || !names.Add(definition.Name))
                    throw new ArgumentException("The native frame accepts only distinct qualified type declarations.", nameof(declarations));
                source += declaration + "\n";
            }
            return source;
        }

        private static readonly Lazy<MethodInfo> AddFunctionTypesToScope = new(() => typeof(PSObject).Assembly
            .GetType("System.Management.Automation.TypeOps", true)!
            .GetMethod("AddPowerShellTypesToTheScope", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new NotSupportedException("PowerShell's lexical type-scope owner is unavailable."));
        private static readonly Lazy<MethodInfo> InitializeFunctionClasses = new(() => typeof(PSObject).Assembly
            .GetType("System.Management.Automation.TypeOps", true)!
            .GetMethod("InitPowerShellTypesAtRuntime", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new NotSupportedException("PowerShell's runtime class initialization is unavailable."));
        private static readonly Lazy<PropertyInfo> RuntimeDeclaredType = new(() => typeof(TypeDefinitionAst)
            .GetProperty("Type", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
            ?? throw new NotSupportedException("PowerShell's authored type identity is unavailable."));

        internal static void AttachModuleTypeMetadata(ScriptBlockAst document, NamedBlockAst owner)
        {
            foreach (var definition in document.EndBlock?.Statements.OfType<TypeDefinitionAst>() ?? Enumerable.Empty<TypeDefinitionAst>())
            {
                var retained = owner.Statements.OfType<TypeDefinitionAst>().SingleOrDefault(candidate =>
                    candidate.Name.Equals(definition.Name, StringComparison.OrdinalIgnoreCase) &&
                    candidate.Extent.Text.Replace("\r\n", "\n") == definition.Extent.Text.Replace("\r\n", "\n"));
                if (retained is not null && RuntimeDeclaredType.Value.GetValue(retained, null) is Type type)
                    RuntimeDeclaredType.Value.SetValue(definition, type, null);
            }
        }

        internal static NamedBlockAst? FindModuleTypeOwner(Ast declaration)
        {
            var root = declaration;
            while (root.Parent is { } parent) root = parent;
            return root is ScriptBlockAst { EndBlock: { } body } && body.Statements.Any(static statement => statement is TypeDefinitionAst)
                ? body : null;
        }

        internal static Type? FindModuleDeclaredType(NamedBlockAst owner, string name)
        {
            var elementName = name.EndsWith("[]", StringComparison.Ordinal) ? name.Substring(0, name.Length - 2) : name;
            var declaration = owner.Statements.OfType<TypeDefinitionAst>().SingleOrDefault(type =>
                type.Name.Equals(elementName, StringComparison.OrdinalIgnoreCase));
            if (declaration is null || RuntimeDeclaredType.Value.GetValue(declaration, null) is not Type declaredType) return null;
            return elementName.Length == name.Length ? declaredType : declaredType.MakeArrayType();
        }

        // Cloned native blocks share compilation data. Associate only explicitly validated
        // compiler-owned frames with that data, without retaining unloaded functions globally.
        private static readonly ConditionalWeakTable<object, FunctionTypeScope> FunctionTypeScopes = new();
        private static readonly ConditionalWeakTable<object, NamedBlockAst> ModuleTypeOwners = new();

        internal static void RegisterModuleTypeOwner(ScriptBlock script, NamedBlockAst? owner)
        {
            if (owner is not null) ModuleTypeOwners.Add(NativeContract.Shared.Data.GetValue(script)!, owner);
        }

        internal static NamedBlockAst? FindModuleTypeOwner(ScriptBlock script)
            => ModuleTypeOwners.TryGetValue(NativeContract.Shared.Data.GetValue(script)!, out var owner)
                ? owner : FindModuleTypeOwner(script.Ast);

        internal sealed class FunctionTypeScope
        {
            internal FunctionTypeScope(Dictionary<string, TypeDefinitionAst> types)
            {
                Types = types;
                Classes = types.Values.Where(static type => type.IsClass).ToArray();
            }
            internal Dictionary<string, TypeDefinitionAst> Types { get; }
            internal TypeDefinitionAst[] Classes { get; }

            internal void AttachClassMetadata(Ast target)
            {
                if (Classes.Length == 0) return;
                var parent = target.Parent;
                while (parent is not null && parent is not FunctionDefinitionAst) parent = parent.Parent;
                if (parent is not FunctionDefinitionAst function || function.Body.EndBlock is not { } body) return;
                foreach (var definition in body.Statements.OfType<TypeDefinitionAst>())
                {
                    if (!definition.IsClass || !Types.TryGetValue(definition.Name, out var owned) ||
                        !string.Equals(definition.Extent.Text.Replace("\r\n", "\n"),
                            owned.Extent.Text.Replace("\r\n", "\n"), StringComparison.Ordinal)) continue;
                    var type = (Type?)RuntimeDeclaredType.Value.GetValue(owned, null)
                        ?? throw new NotSupportedException("The native compiler did not define the owned class.");
                    RuntimeDeclaredType.Value.SetValue(definition, type, null);
                }
            }
        }

        private static void RegisterFunctionTypeScope(ScriptBlock script, object data)
        {
            var root = script.Ast is FunctionDefinitionAst function ? function.Body : script.Ast as ScriptBlockAst;
            var definitions = root?.EndBlock?.Statements.OfType<TypeDefinitionAst>().ToArray() ?? Array.Empty<TypeDefinitionAst>();
            if (definitions.Length == 0) return;
            if (definitions.Any(definition => !IsSupportedFunctionType(definition)))
                throw new NotSupportedException("The native function's type declarations are outside its qualified contract.");
            var types = definitions.ToDictionary(definition => definition.Name, StringComparer.OrdinalIgnoreCase);
            FunctionTypeScopes.GetValue(data, _ => new FunctionTypeScope(types));
        }

        internal static FunctionTypeScope? InitializeFunctionTypes(ScriptBlock script, object executionContext)
        {
            if (!FunctionTypeScopes.TryGetValue(NativeContract.Shared.Data.GetValue(script)!, out var scope)) return null;
            // Mirror the SDK's lexical initialization only for the new validated stub.
            // Existing authored declaration and script-block callback contracts remain untouched.
            if (scope.Classes.Length != 0)
                Invoke(InitializeFunctionClasses.Value, null, new object[] { scope.Classes });
            Invoke(AddFunctionTypesToScope.Value, null, new object[] { scope.Types, executionContext });
            return scope;
        }
    }
}
