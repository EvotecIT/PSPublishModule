using System.Management.Automation;
using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>Qualifies document-owned argument attributes kept on the native PowerShell declaration path.</summary>
/// <remarks>This does not resolve arbitrary authored types or compile class bodies.</remarks>
internal static class PowerShellHostedAttributeDeclarationPolicy
{
    internal static bool IsQualified(ParsedSourceDocument document, string? targetFramework,
        PowerShellCompilationCapability capabilities)
    {
        if (!capabilities.HasFlag(PowerShellCompilationCapability.NativeFunctionBinding) ||
            document.Errors.Length != 0 || document.TypeClosure.Declarations.Length == 0)
            return false;
        var definitions = document.SyntaxRoot.FindAll(static node => node is TypeDefinitionAst,
            searchNestedScriptBlocks: false).OfType<TypeDefinitionAst>().ToArray();
        if (definitions.Length != document.TypeClosure.Declarations.Length ||
            document.TypeClosure.Declarations.Any(static declaration => !declaration.IsDocumentScope || declaration.IsEnum) ||
            definitions.GroupBy(static declaration => declaration.Name, StringComparer.OrdinalIgnoreCase).Any(static group => group.Count() != 1))
            return false;
        foreach (var definition in definitions)
            if (!QualifiesDefinition(definition, targetFramework)) return false;

        var functions = document.SyntaxRoot.FindAll(static node => node is FunctionDefinitionAst,
            searchNestedScriptBlocks: false).OfType<FunctionDefinitionAst>()
            .Where(function => ReferenceEquals(function.Parent, document.SyntaxRoot.EndBlock)).ToArray();
        foreach (var reference in document.TypeClosure.References.Where(static reference => reference.DeclarationCandidates.Length != 0))
        {
            // No class values, constraints, member dependencies, nested consumers or ambiguous suffix lookup.
            if (reference.Kind != PowerShellSourceTypeReferenceKind.Attribute || reference.DeclarationCandidates.Length != 1)
                return false;
            var function = functions.SingleOrDefault(candidate =>
                PowerShellSourceParser.GetSpan(document, candidate.Extent).Equals(reference.ConsumerSpan));
            if (function is null || PowerShellNativeFunctionBindingPolicy.Select(function, capabilities, targetFramework) is not { HasClean: false })
                return false;
            var attribute = PowerShellParameterSyntax.GetParameters(function.Body)
                .SelectMany(static parameter => parameter.Attributes).OfType<AttributeAst>()
                .SingleOrDefault(candidate => PowerShellSourceParser.GetSpan(document, candidate.TypeName.Extent).Equals(reference.Span));
            if (attribute is null || attribute.PositionalArguments.Count != 0 || attribute.NamedArguments.Count != 0)
                return false;
        }
        return true;
    }

    private static bool QualifiesDefinition(TypeDefinitionAst definition, string? targetFramework)
    {
        if (!definition.IsClass || definition.Attributes.Count != 0 || definition.BaseTypes.Count != 1 ||
            definition.Members.Count != 1 || definition.Members[0] is not FunctionMemberAst method ||
            method.IsConstructor || method.IsStatic || !method.IsPublic || method.Attributes.Count != 0 || method.Parameters.Count != 2)
            return false;
        var baseName = definition.BaseTypes[0].TypeName.FullName;
        if (!string.IsNullOrEmpty(definition.BaseTypes[0].TypeName.AssemblyName)) return false;
        var transformation = baseName.Equals(typeof(ArgumentTransformationAttribute).FullName, StringComparison.OrdinalIgnoreCase);
        var validation = baseName.Equals(typeof(ValidateArgumentsAttribute).FullName, StringComparison.OrdinalIgnoreCase);
        if (!transformation && !validation ||
            definition.BaseTypes[0].TypeName.GetReflectionType() != (transformation ? typeof(ArgumentTransformationAttribute) : typeof(ValidateArgumentsAttribute)) ||
            !method.Name.Equals(transformation ? "Transform" : "Validate", StringComparison.OrdinalIgnoreCase) ||
            method.ReturnType?.TypeName.GetReflectionType() != (transformation ? typeof(object) : typeof(void)))
            return false;
        var types = new[] { transformation ? typeof(EngineIntrinsics) : typeof(object), transformation ? typeof(object) : typeof(EngineIntrinsics) };
        for (var index = 0; index < types.Length; index++)
        {
            var parameter = method.Parameters[index];
            if (parameter.DefaultValue != null || parameter.Attributes.Count != 1 ||
                parameter.Attributes[0] is not TypeConstraintAst constraint || constraint.TypeName.GetReflectionType() != types[index])
                return false;
        }
        // Read CLR metadata only. Authored dependencies remain candidates and cannot become a CLR guess.
        foreach (var node in method.Body.FindAll(static node => node is TypeConstraintAst or TypeExpressionAst or AttributeAst,
                     searchNestedScriptBlocks: true))
        {
            var type = node switch
            {
                TypeConstraintAst constraint => constraint.TypeName.GetReflectionType(),
                TypeExpressionAst expression => expression.TypeName.GetReflectionType(),
                _ => null
            };
            if (type is null || !PowerShellGeneratedTypePolicy.IsSupported(type, targetFramework)) return false;
        }
        return true;
    }
}
