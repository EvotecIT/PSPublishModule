using System.Management.Automation.Language;

namespace PowerForge;

/// <summary>Parser-derived declaration and consumer identities, without executing or resolving authored types.</summary>
/// <remarks>These facts describe dependencies, not permission to emit a dependent function or declaration.</remarks>
internal sealed class PowerShellSourceTypeClosure
{
    private PowerShellSourceTypeClosure(PowerShellSourceTypeDeclaration[] declarations, PowerShellSourceTypeReference[] references)
    {
        Declarations = declarations;
        References = references;
    }

    internal PowerShellSourceTypeDeclaration[] Declarations { get; }
    internal PowerShellSourceTypeReference[] References { get; }

    internal static PowerShellSourceTypeClosure Discover(ParsedSourceDocument document)
    {
        var syntax = document.SyntaxRoot.FindAll(static node => node is TypeDefinitionAst, searchNestedScriptBlocks: false)
            .OfType<TypeDefinitionAst>().ToArray();
        var declarations = syntax.Select(definition => new PowerShellSourceTypeDeclaration(
            definition.Name, definition.IsEnum, ReferenceEquals(definition.Parent, document.SyntaxRoot.EndBlock),
            PowerShellSourceParser.GetSpan(document, definition.Extent),
            definition.BaseTypes.Select(static item => item.TypeName.FullName).ToArray())).ToArray();
        if (declarations.Length == 0)
            return new PowerShellSourceTypeClosure(declarations, Array.Empty<PowerShellSourceTypeReference>());
        var references = new List<PowerShellSourceTypeReference>();
        foreach (var definition in syntax)
            foreach (var baseType in definition.BaseTypes)
                AddReference(baseType.TypeName, definition.Name, PowerShellSourceTypeReferenceKind.BaseType);
        foreach (var function in document.SyntaxRoot.FindAll(static node => node is FunctionDefinitionAst,
                     searchNestedScriptBlocks: false).OfType<FunctionDefinitionAst>()
                     .Where(function => function.Parent is NamedBlockAst && ReferenceEquals(function.Parent.Parent, document.SyntaxRoot)))
            foreach (var node in function.FindAll(static node => node is TypeConstraintAst or TypeExpressionAst or AttributeAst,
                         searchNestedScriptBlocks: true))
                switch (node)
                {
                    case AttributeAst attribute:
                        AddReference(attribute.TypeName, function.Name, PowerShellSourceTypeReferenceKind.Attribute);
                        break;
                    case TypeConstraintAst constraint:
                        AddReference(constraint.TypeName, function.Name, PowerShellSourceTypeReferenceKind.Constraint);
                        break;
                    case TypeExpressionAst expression:
                        AddReference(expression.TypeName, function.Name, PowerShellSourceTypeReferenceKind.Value);
                        break;
                }
        return new PowerShellSourceTypeClosure(declarations, references.ToArray());

        void AddReference(ITypeName typeName, string consumer, PowerShellSourceTypeReferenceKind kind)
        {
            // Keep all candidates: case-insensitive duplicate declarations and attribute suffix
            // ambiguities must not silently resolve to whichever declaration was encountered first.
            var matches = string.IsNullOrEmpty(typeName.AssemblyName)
                ? declarations.Where(declaration => declaration.Name.Equals(typeName.FullName, StringComparison.OrdinalIgnoreCase) ||
                    kind == PowerShellSourceTypeReferenceKind.Attribute &&
                    declaration.Name.Equals(typeName.FullName + "Attribute", StringComparison.OrdinalIgnoreCase))
                    .Select(static declaration => declaration.Span).ToArray()
                : Array.Empty<SourceSpan>();
            references.Add(new PowerShellSourceTypeReference(typeName.FullName, consumer, kind,
                PowerShellSourceParser.GetSpan(document, typeName.Extent), matches));
            if (typeName is ArrayTypeName array)
                AddReference(array.ElementType, consumer, kind);
            else if (typeName is GenericTypeName generic)
            {
                AddReference(generic.TypeName, consumer, kind);
                foreach (var argument in generic.GenericArguments) AddReference(argument, consumer, kind);
            }
        }
    }
}

/// <summary>One authored declaration identity; its source extent distinguishes same-name declarations.</summary>
internal sealed class PowerShellSourceTypeDeclaration
{
    internal PowerShellSourceTypeDeclaration(string name, bool isEnum, bool isDocumentScope, SourceSpan span, string[] baseTypeNames)
    {
        Name = name;
        IsEnum = isEnum;
        IsDocumentScope = isDocumentScope;
        Span = span;
        BaseTypeNames = baseTypeNames;
    }

    internal string Name { get; }
    internal bool IsEnum { get; }
    internal bool IsDocumentScope { get; }
    internal SourceSpan Span { get; }
    internal string[] BaseTypeNames { get; }
}

/// <summary>One authored type reference and every matching declaration candidate in the same source document.</summary>
internal sealed class PowerShellSourceTypeReference
{
    internal PowerShellSourceTypeReference(string typeName, string consumer, PowerShellSourceTypeReferenceKind kind,
        SourceSpan span, SourceSpan[] declarationCandidates)
    {
        TypeName = typeName;
        Consumer = consumer;
        Kind = kind;
        Span = span;
        DeclarationCandidates = declarationCandidates;
    }

    internal string TypeName { get; }
    internal string Consumer { get; }
    internal PowerShellSourceTypeReferenceKind Kind { get; }
    internal SourceSpan Span { get; }
    internal SourceSpan[] DeclarationCandidates { get; }
}

internal enum PowerShellSourceTypeReferenceKind { BaseType, Attribute, Constraint, Value }
