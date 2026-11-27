using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace PowerForge.Generators.Fluent {
    // What a property's getter hands out: the object the owner keeps, or a new one each read. Methods that edit the value
    // in place, or add to it, only make sense for the first.
    internal static partial class FluentModel {
        // Methods whose result is a new object by convention: copies and materialized sequences.
        private static readonly string[] FreshMethods = { "Clone", "Copy", "DeepClone", "DeepCopy", "ToList", "ToArray", "ToHashSet", "ToDictionary", "ToLookup" };

        /// <summary>
        /// Why the object a property's getter returns is not the one its owner keeps (a new instance, a copy or a collection
        /// built on every read), or null when that cannot be shown. Only getters with source in this compilation are read;
        /// auto-properties, fields and cached values (<c>_x ??= new X()</c>) are kept objects.
        /// </summary>
        internal static string? FreshGetterReason(IPropertySymbol property, Compilation compilation) {
            foreach (SyntaxReference reference in property.DeclaringSyntaxReferences) {
                if (reference.GetSyntax() is not PropertyDeclarationSyntax declaration) {
                    continue;
                }
                SemanticModel model = compilation.GetSemanticModel(declaration.SyntaxTree);
                if (declaration.ExpressionBody != null) {
                    return Fresh(declaration.ExpressionBody.Expression, model, 0);
                }
                AccessorDeclarationSyntax? getter = declaration.AccessorList?.Accessors.FirstOrDefault(a => a.IsKind(SyntaxKind.GetAccessorDeclaration));
                if (getter?.ExpressionBody != null) {
                    return Fresh(getter.ExpressionBody.Expression, model, 0);
                }
                if (getter?.Body != null) {
                    return Returns(getter.Body, model, 0);
                }
            }
            return null;
        }

        /// <summary>Why a collection type cannot take added items whatever its getter does (a read-only or immutable collection), or null.</summary>
        internal static string? ReadOnlyCollectionReason(ITypeSymbol type) {
            string definition = type.OriginalDefinition.ToDisplayString();
            if (definition.StartsWith("System.Collections.ObjectModel.ReadOnly", StringComparison.Ordinal)
                || definition.StartsWith("System.Collections.Immutable.", StringComparison.Ordinal)
                || definition.StartsWith("System.Collections.Frozen.", StringComparison.Ordinal)) {
                return "'" + type.OriginalDefinition.Name + "' is read-only";
            }
            return null;
        }

        private static string? Returns(SyntaxNode body, SemanticModel model, int depth) {
            // Return statements of this body only, not of lambdas or local functions inside it.
            IEnumerable<ReturnStatementSyntax> returns = body.DescendantNodes(node => node == body || node is not (AnonymousFunctionExpressionSyntax or LocalFunctionStatementSyntax))
                .OfType<ReturnStatementSyntax>();
            foreach (ReturnStatementSyntax statement in returns) {
                if (statement.Expression != null && Fresh(statement.Expression, model, depth) is string reason) {
                    return reason;
                }
            }
            return null;
        }

        private static string? Fresh(ExpressionSyntax expression, SemanticModel model, int depth) {
            switch (expression) {
                case ParenthesizedExpressionSyntax parenthesized:
                    return Fresh(parenthesized.Expression, model, depth);
                case CastExpressionSyntax cast:
                    return Fresh(cast.Expression, model, depth);
                case PostfixUnaryExpressionSyntax suppressed when suppressed.IsKind(SyntaxKind.SuppressNullableWarningExpression):
                    return Fresh(suppressed.Operand, model, depth);
                case BinaryExpressionSyntax binary when binary.IsKind(SyntaxKind.AsExpression):
                    return Fresh(binary.Left, model, depth);
                case BaseObjectCreationExpressionSyntax or ArrayCreationExpressionSyntax or ImplicitArrayCreationExpressionSyntax or AnonymousObjectCreationExpressionSyntax
                    or WithExpressionSyntax:
                    return "its getter returns a new object on every read";
                case ConditionalExpressionSyntax conditional:
                    // Only when every branch builds one: owner != null ? owner.Shared : new X() often hands out the kept object.
                    return Fresh(conditional.WhenTrue, model, depth) is string whenTrue && Fresh(conditional.WhenFalse, model, depth) != null ? whenTrue : null;
                case BinaryExpressionSyntax coalesce when coalesce.IsKind(SyntaxKind.CoalesceExpression):
                    // _x ?? new X() and _x ?? (_x = new X()) hand out the kept object whenever there is one.
                    return Fresh(coalesce.Left, model, depth) is string left && Fresh(coalesce.Right, model, depth) != null ? left : null;
                case InvocationExpressionSyntax invocation:
                    return FreshCall(invocation, model, depth);
                case IdentifierNameSyntax identifier when model.GetSymbolInfo(identifier).Symbol is ILocalSymbol local:
                    // A local the getter fills and returns (var list = new List<T>(); ...; return list;), unless it is also
                    // stored somewhere (_cache = list, _map[key] = list, Register(list)), which makes it the kept object.
                    if (Stored(local, model)) {
                        return null;
                    }
                    return local.DeclaringSyntaxReferences.Select(r => r.GetSyntax()).OfType<VariableDeclaratorSyntax>()
                        .Select(declarator => declarator.Initializer == null ? null : Fresh(declarator.Initializer.Value, model, depth)).FirstOrDefault(reason => reason != null);
                default:
                    if (expression.IsKind(SyntaxKind.CollectionExpression)) {
                        return "its getter returns a new collection on every read";
                    }
                    return null;
            }
        }

        // Whether the local is assigned to something or passed to a method anywhere in the member that declares it.
        private static bool Stored(ILocalSymbol local, SemanticModel model) {
            SyntaxNode? body = local.DeclaringSyntaxReferences.FirstOrDefault()?.GetSyntax()
                .Ancestors().FirstOrDefault(node => node is AccessorDeclarationSyntax or MethodDeclarationSyntax or PropertyDeclarationSyntax or LocalFunctionStatementSyntax);
            if (body == null) {
                return false;
            }
            foreach (IdentifierNameSyntax use in body.DescendantNodes().OfType<IdentifierNameSyntax>().Where(n => n.Identifier.ValueText == local.Name)) {
                SyntaxNode parent = use.Parent is ParenthesizedExpressionSyntax or CastExpressionSyntax ? use.Parent.Parent! : use.Parent!;
                bool escapes = parent is AssignmentExpressionSyntax assignment && assignment.Right.DescendantNodesAndSelf().Contains(use)
                    || parent is ArgumentSyntax || parent is EqualsValueClauseSyntax { Parent: not VariableDeclaratorSyntax }
                    || parent is InitializerExpressionSyntax;
                if (escapes && SymbolEqualityComparer.Default.Equals(model.GetSymbolInfo(use).Symbol, local)) {
                    return true;
                }
            }
            return false;
        }

        // A copy or materializing call, or a method of this compilation whose own returns are new objects (one level deep).
        private static string? FreshCall(InvocationExpressionSyntax invocation, SemanticModel model, int depth) {
            if (model.GetSymbolInfo(invocation).Symbol is not IMethodSymbol method) {
                return null;
            }
            if (FreshMethods.Contains(method.Name, StringComparer.Ordinal)) {
                return "its getter returns the result of " + method.Name + "(), a new object on every read";
            }
            if (depth > 0) {
                return null;
            }
            foreach (SyntaxReference reference in method.DeclaringSyntaxReferences) {
                SyntaxNode syntax = reference.GetSyntax();
                SemanticModel methodModel = model.Compilation.GetSemanticModel(syntax.SyntaxTree);
                string? reason = syntax switch {
                    MethodDeclarationSyntax { ExpressionBody: not null } arrow => Fresh(arrow.ExpressionBody.Expression, methodModel, depth + 1),
                    MethodDeclarationSyntax { Body: not null } block => Returns(block.Body, methodModel, depth + 1),
                    _ => null
                };
                if (reason != null) {
                    return "its getter returns the result of " + method.Name + "(), which builds a new object on every call";
                }
            }
            return null;
        }
    }
}
