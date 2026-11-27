using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using static PowerForge.Generators.Fluent.FluentModel;

namespace PowerForge.Generators.Fluent {
    // [FluentMutator]: fluent methods that write through an instance method (a handle mutator, a setter that is not public,
    // or several values at once) instead of a property setter.
    public sealed partial class FluentGenerator {
        private sealed partial class Emitter {
            private readonly Dictionary<INamedTypeSymbol, List<(IMethodSymbol Method, string Name)>> _mutators = new Dictionary<INamedTypeSymbol, List<(IMethodSymbol, string)>>(SymbolEqualityComparer.Default);
            private readonly HashSet<IMethodSymbol> _reportedMutators = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);

            // The [FluentMutator] methods of a type, declared or inherited (the most derived override of each), with their
            // fluent names. A method with the wrong shape is reported once, where it is declared, and left out; a method of a
            // referenced library this assembly cannot call is left out silently.
            internal List<(IMethodSymbol Method, string Name)> Mutators(INamedTypeSymbol type) {
                type = (INamedTypeSymbol)type.WithNullableAnnotation(NullableAnnotation.NotAnnotated);
                if (_mutators.TryGetValue(type, out List<(IMethodSymbol, string)>? cached)) {
                    return cached;
                }
                List<(IMethodSymbol, string)> result = new List<(IMethodSymbol, string)>();
                HashSet<IMethodSymbol> overridden = new HashSet<IMethodSymbol>(SymbolEqualityComparer.Default);
                foreach (IMethodSymbol method in BaseTypes(type).SelectMany(t => t.GetMembers()).OfType<IMethodSymbol>()) {
                    if (overridden.Contains(method.OriginalDefinition)) { continue; }
                    for (IMethodSymbol? origin = method.OverriddenMethod; origin != null; origin = origin.OverriddenMethod) {
                        overridden.Add(origin.OriginalDefinition);
                    }
                    // The attribute may sit on the method or on the base declaration it overrides.
                    AttributeData? attribute = null;
                    for (IMethodSymbol? declared = method; declared != null && attribute == null; declared = declared.OverriddenMethod) {
                        attribute = Attribute(declared, FluentAttributes.Mutator);
                    }
                    if (attribute == null) { continue; }
                    bool local = SymbolEqualityComparer.Default.Equals(method.ContainingAssembly, _compilation.Assembly);
                    if (!local && !_compilation.IsSymbolAccessibleWithin(method, _compilation.Assembly)) { continue; }
                    string name = NamedArgument(attribute) ?? StripSet(method.Name);
                    string? problem = MutatorProblem(method, name);
                    if (problem != null) {
                        if (local && _reportedMutators.Add(method.OriginalDefinition)) {
                            Location location = attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? method.Locations.FirstOrDefault() ?? Location.None;
                            _output.ReportDiagnostic(Diagnostic.Create(FluentDiagnostics.InvalidMutator, location, method.ContainingType.Name, method.Name, problem));
                        }
                        continue;
                    }
                    result.Add((method, name));
                }
                _mutators[type] = result;
                return result;
            }

            private static string StripSet(string name) =>
                name.Length > 3 && name.StartsWith("Set", StringComparison.Ordinal) && char.IsUpper(name[3]) ? name.Substring(3) : name;

            private static string? MutatorProblem(IMethodSymbol method, string name) {
                if (method.MethodKind != MethodKind.Ordinary || method.IsStatic) {
                    return "it must be an ordinary instance method";
                }
                if (method.IsGenericMethod) {
                    return "it must not be generic";
                }
                if (method.DeclaredAccessibility is not (Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal)) {
                    return "it must be public or internal";
                }
                if (method.Parameters.Any(p => p.RefKind != RefKind.None)) {
                    return "its parameters must be passed by value (no ref, out or in)";
                }
                if (!SyntaxFacts.IsValidIdentifier(name)) {
                    return "'" + name + "' is not a valid C# method name";
                }
                IParameterSymbol? unsupported = method.Parameters.FirstOrDefault(p => p.HasExplicitDefaultValue && DefaultLiteral(p) == null);
                return unsupported == null ? null : "the default value of '" + unsupported.Name + "' cannot be repeated in generated code";
            }

            // A parameter's default as C#: null, default, an enum member or a literal of the parameter's type.
            internal static string? DefaultLiteral(IParameterSymbol parameter) {
                if (!parameter.HasExplicitDefaultValue) {
                    return parameter.IsOptional ? "default" : null;
                }
                object? value = parameter.ExplicitDefaultValue;
                ITypeSymbol type = parameter.Type;
                if (value == null) {
                    return type.IsReferenceType || type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T ? "null" : "default";
                }
                ITypeSymbol underlying = type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable ? nullable.TypeArguments[0] : type;
                if (underlying is INamedTypeSymbol { TypeKind: TypeKind.Enum } enumType) {
                    string qualified = enumType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                    IFieldSymbol? member = enumType.GetMembers().OfType<IFieldSymbol>().FirstOrDefault(f => f.HasConstantValue && Equals(f.ConstantValue, value));
                    string? number = Builder.LiteralOf(value);
                    return member != null ? qualified + "." + Escape(member.Name) : number == null ? null : "((" + qualified + ")(" + number + "))";
                }
                return value is decimal amount ? amount.ToString(CultureInfo.InvariantCulture) + "M" : Builder.LiteralOf(value);
            }

            private sealed partial class Builder {
                // Value-parameter signatures [FluentMutator] methods generated; a property method with the same one gives way.
                private readonly HashSet<string> _mutated = new HashSet<string>(StringComparer.Ordinal);

                internal static string? LiteralOf(object? value) => Literal(value);

                // The mutators of the type being generated, or, while a [FluentInline] property is expanded, of its type.
                private void AppendMutators(INamedTypeSymbol type) {
                    foreach ((IMethodSymbol method, string name) in _emitter.Mutators(type)) {
                        if (_via == null && CoveredByBase(method.ContainingType)) { continue; }
                        AppendMutator(method, name);
                    }
                }

                // A method declared by a generated base that can be derived from is covered by the base's generic methods.
                private bool CoveredByBase(INamedTypeSymbol owner) =>
                    !SymbolEqualityComparer.Default.Equals(owner, _type) && !owner.IsSealed && owner.TypeKind == TypeKind.Class
                    && (_types.Contains(owner.OriginalDefinition, SymbolEqualityComparer.Default) || _emitter.GeneratedSignatures(owner.OriginalDefinition) != null)
                    && (IsCandidate(owner) || IsNestedFluent(owner) || !_type.IsSealed);

                private void AppendMutator(IMethodSymbol method, string name) {
                    Location? location = _via?.Locations.FirstOrDefault() ?? method.Locations.FirstOrDefault();
                    string member = _via == null ? method.Name : _via.Name + "." + method.Name;
                    if (!Claim(location, member, name, method.Parameters.Select(p => p.Type).ToArray())) { return; }
                    _mutated.Add(Signature(name, method.Parameters.Select(p => p.Type)));
                    // A single bool without a default reads as a switch: Resolved() sets it.
                    bool switchLike = method.Parameters.Length == 1 && IsBool(method.Parameters[0].Type) && !method.Parameters[0].IsOptional;
                    bool shortForm = method.Parameters.Length > 0 && (switchLike || method.Parameters.All(p => p.IsOptional || p.IsParams));
                    if (shortForm && !_generated.Add(name + "()")) {
                        _emitter._output.ReportDiagnostic(Diagnostic.Create(FluentDiagnostics.Duplicate, location ?? Location.None, name, _type.Name, member));
                        return;
                    }
                    List<string> parameters = new List<string>();
                    System.Text.StringBuilder doc = new System.Text.StringBuilder();
                    string summary = DocText(method, "summary", null) ?? "Sets " + new XText(name) + ".";
                    doc.Append("        /// <summary>").Append(summary).Append("</summary>\n")
                        .Append("        /// <param name=\"").Append(_receiver.TrimStart('@')).Append("\">The instance.</param>\n");
                    foreach (IParameterSymbol parameter in method.Parameters) {
                        string? fallback = switchLike ? "true" : Emitter.DefaultLiteral(parameter);
                        parameters.Add((parameter.IsParams ? "params " : string.Empty) + ParameterType(parameter, fallback) + " " + Escape(parameter.Name)
                            + (fallback == null || parameter.IsParams ? string.Empty : " = " + fallback));
                        string text = DocText(method, "param", parameter.Name) ?? "The value.";
                        doc.Append("        /// <param name=\"").Append(parameter.Name).Append("\">").Append(text).Append(switchLike ? " True when omitted." : string.Empty).Append("</param>\n");
                    }
                    doc.Append("        /// <returns>The same instance.</returns>\n");
                    string target = _via == null ? _receiver : _receiver + "." + Escape(_via.Name);
                    string arguments = string.Join(", ", method.Parameters.Select(p => Escape(p.Name)));
                    AppendMethod(doc.ToString(), name, string.Concat(parameters.Select(p => ", " + p)),
                        "            " + target + "." + Escape(method.Name) + "(" + arguments + ");\n");
                }

                // A reference type defaulting to null is annotated nullable where the generated file enables nullable context, so
                // a project with nullable warnings off (or a method declared with a plain `string s = null`) still compiles cleanly.
                private string ParameterType(IParameterSymbol parameter, string? fallback) {
                    ITypeSymbol type = parameter.Type;
                    bool nullDefault = fallback == "null" || fallback == "default" && (type.IsReferenceType || type is ITypeParameterSymbol { HasReferenceTypeConstraint: true });
                    if (_emitter.Nullable && nullDefault && type.IsReferenceType) {
                        type = type.WithNullableAnnotation(NullableAnnotation.Annotated);
                    }
                    return type.ToDisplayString(TypeFormat);
                }

                // Whether a [FluentMutator] already generated name(type), so the property's own setter method steps aside.
                private bool Mutated(string name, ITypeSymbol type) => _mutated.Contains(Signature(name, new[] { type }));

                private static string Signature(string name, IEnumerable<ITypeSymbol> parameters) =>
                    name + "(" + string.Join(", ", parameters.Select(parameter => parameter.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat))) + ")";
            }
        }
    }
}
