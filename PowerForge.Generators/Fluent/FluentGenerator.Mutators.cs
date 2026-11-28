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
                foreach (IParameterSymbol parameter in method.Parameters) {
                    if (parameter.Type is IPointerTypeSymbol or IFunctionPointerTypeSymbol) {
                        return "'" + parameter.Name + "' is a pointer, which needs an unsafe context the generated method doesn't have";
                    }
                    if (!IsPublicType(parameter.Type)) {
                        return "'" + parameter.Name + "' is a '" + parameter.Type.ToDisplayString() + "', which the public fluent method can't expose; make that type public";
                    }
                    if (parameter.HasExplicitDefaultValue && DefaultLiteral(parameter) == null) {
                        return "the default value of '" + parameter.Name + "' cannot be repeated in generated code";
                    }
                    // An omitted [Optional] COM interface parameter binds a wrapper object the generated method can't repeat.
                    if (BareOptional(parameter) && parameter.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == "System.Runtime.InteropServices.MarshalAsAttribute"
                        && a.ConstructorArguments.Length == 1 && Convert.ToInt32(a.ConstructorArguments[0].Value, CultureInfo.InvariantCulture) is 25 or 26 or 28)) {
                        return "'" + parameter.Name + "' is an [Optional] COM interface parameter, whose omitted value can't be repeated in generated code";
                    }
                }
                return null;
            }

            // Whether a type can appear in a public signature: public all the way out, with public type arguments.
            private static bool IsPublicType(ITypeSymbol type) {
                switch (type) {
                    case ITypeParameterSymbol:
                        return true;
                    case IArrayTypeSymbol array:
                        return IsPublicType(array.ElementType);
                    case INamedTypeSymbol named:
                        for (INamedTypeSymbol? current = named; current != null; current = current.ContainingType) {
                            if (current.DeclaredAccessibility != Accessibility.Public) { return false; }
                        }
                        return named.TypeArguments.All(IsPublicType);
                    default:
                        return true;
                }
            }

            // [Optional] without a default value: an omitted argument is Type.Missing for object and default otherwise, which only
            // the same attribute on the generated parameter reproduces.
            private static bool BareOptional(IParameterSymbol parameter) => parameter.IsOptional && !parameter.HasExplicitDefaultValue && !parameter.IsParams;

            // A parameter's default as C#: null, default, an enum member or a literal of the parameter's type.
            internal static string? DefaultLiteral(IParameterSymbol parameter) {
                if (!parameter.HasExplicitDefaultValue) {
                    return null;
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
                        if (_via == null && CoveredByBase(method, name)) {
                            // The base's method is the write path here too, so this type's own setter must not take it.
                            _mutated.Add(SignatureKey(name, method.Parameters.Select(p => p.Type)));
                            continue;
                        }
                        AppendMutator(method, name);
                    }
                }

                // The method, or a base declaration it overrides, is a mutator of a generated base that can be derived from, under
                // the same name: the base's generic method covers this type too, and its call still dispatches to the override. An
                // override that asks for another name gets its own method.
                private bool CoveredByBase(IMethodSymbol method, string name) {
                    for (IMethodSymbol? declared = method; declared != null; declared = declared.OverriddenMethod) {
                        // A referenced base must have generated that very method: one built with Prefixed naming has none.
                        INamedTypeSymbol owner = declared.ContainingType;
                        bool local = SymbolEqualityComparer.Default.Equals(owner.ContainingAssembly, Compilation.Assembly);
                        if (CoveredByBase(owner) && _emitter.Mutators(owner)
                            .Any(m => m.Name == name && SymbolEqualityComparer.Default.Equals(m.Method.OriginalDefinition, declared.OriginalDefinition))
                            && (local || _emitter.ConventionGenerated(owner, SignatureKey(name, declared.Parameters.Select(p => p.Type))))) {
                            return true;
                        }
                    }
                    return false;
                }

                // A method declared by a generated base that can be derived from is covered by the base's generic methods.
                private bool CoveredByBase(INamedTypeSymbol owner) =>
                    !SymbolEqualityComparer.Default.Equals(owner, _type) && !owner.IsSealed && owner.TypeKind == TypeKind.Class
                    && (_types.Contains(owner.OriginalDefinition, SymbolEqualityComparer.Default) || _emitter.GeneratedSignatures(owner.OriginalDefinition) != null)
                    && (IsCandidate(owner) || IsNestedFluent(owner) || !_type.IsSealed);

                private void AppendMutator(IMethodSymbol method, string name) {
                    Location? location = _via?.Locations.FirstOrDefault() ?? method.Locations.FirstOrDefault();
                    string member = _via == null ? method.Name : _via.Name + "." + method.Name;
                    // A sealed generic type's methods declare its type parameters, which a value parameter can't share a name with.
                    if (_type.IsSealed && method.Parameters.FirstOrDefault(p => _type.TypeParameters.Any(t => t.Name == p.Name)) is IParameterSymbol clash) {
                        if (_emitter._reportedMutators.Add(method.OriginalDefinition)) {
                            _emitter._output.ReportDiagnostic(Diagnostic.Create(FluentDiagnostics.InvalidMutator, location ?? Location.None, method.ContainingType.Name, method.Name,
                                "'" + clash.Name + "' is also a type parameter of '" + _type.Name + "', which the generated method declares; rename the parameter"));
                        }
                        return;
                    }
                    if (name == _fluentClass) {
                        if (_emitter._reportedMutators.Add(method.OriginalDefinition)) {
                            _emitter._output.ReportDiagnostic(Diagnostic.Create(FluentDiagnostics.InvalidMutator, location ?? Location.None, method.ContainingType.Name, method.Name,
                                "'" + name + "' is the name of the generated class, which a method in it can't have; choose another name"));
                        }
                        return;
                    }
                    if (!Claim(location, member, name, method.Parameters.Select(p => p.Type).ToArray())) { return; }
                    _mutated.Add(SignatureKey(name, method.Parameters.Select(p => p.Type)));
                    // A single bool without a default reads as a switch: Resolved() sets it. Not when an existing method already
                    // answers Resolved(): an instance method or an exact Resolved() takes that call first. An extension answering it
                    // through defaults would make it ambiguous, so the first parameter is then required too.
                    bool argless = !AnswersArgless(name);
                    bool ambiguous = AnswersThroughDefaults(name);
                    bool switchLike = argless && method.Parameters.Length == 1 && IsBool(method.Parameters[0].Type) && !method.Parameters[0].IsOptional;
                    bool shortForm = argless && method.Parameters.Length > 0 && (switchLike || method.Parameters.All(p => p.IsOptional || p.IsParams));
                    if (shortForm && !_generated.Add(name + "()")) {
                        _emitter._output.ReportDiagnostic(Diagnostic.Create(FluentDiagnostics.Duplicate, location ?? Location.None, name, _type.Name, member));
                        return;
                    }
                    List<string> parameters = new List<string>();
                    System.Text.StringBuilder doc = new System.Text.StringBuilder();
                    string summary = DocText(method, "summary", null) ?? "Sets " + new XText(name) + ".";
                    doc.Append("        /// <summary>").Append(summary).Append("</summary>\n")
                        .Append("        /// <param name=\"").Append(_receiver.TrimStart('@')).Append("\">The instance.</param>\n");
                    // A params array that must not answer name() is an array here, plus an overload taking at least one item below.
                    bool splitParams = ambiguous && method.Parameters.Length == 1 && method.Parameters[0].IsParams;
                    foreach (IParameterSymbol parameter in method.Parameters) {
                        bool required = ambiguous && parameter.Ordinal == 0;
                        string? fallback = required ? null : switchLike ? "true" : Emitter.DefaultLiteral(parameter);
                        // string label = null! keeps its non-nullable type, so the call to the method doesn't warn.
                        if (fallback is "null" or "default" && _emitter.Nullable && parameter.Type.IsReferenceType && parameter.Type.NullableAnnotation == NullableAnnotation.NotAnnotated) {
                            fallback = "null!";
                        }
                        string optional = (!required && BareOptional(parameter) ? "[global::System.Runtime.InteropServices.Optional] " : string.Empty)
                            + (required ? string.Empty : CallerInfo(parameter));
                        parameters.Add(optional + (parameter.IsParams && !splitParams ? "params " : string.Empty) + ParameterType(parameter, fallback) + " " + Escape(parameter.Name)
                            + (fallback == null || parameter.IsParams ? string.Empty : " = " + fallback));
                        string text = DocText(method, "param", parameter.Name) ?? "The value.";
                        doc.Append("        /// <param name=\"").Append(parameter.Name).Append("\">").Append(text).Append(switchLike ? " True when omitted." : string.Empty).Append("</param>\n");
                    }
                    doc.Append("        /// <returns>The same instance.</returns>\n");
                    string target = _via == null ? _receiver : _receiver + "." + Escape(_via.Name);
                    // Through the declaring type, so a method of the same name a derived type declares with new can't take the call.
                    ITypeSymbol receiverType = _via == null ? _type : _via.Type;
                    if (!SymbolEqualityComparer.Default.Equals(method.ContainingType.OriginalDefinition, receiverType.OriginalDefinition)) {
                        target = "((" + method.ContainingType.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString(TypeFormat) + ")" + target + ")";
                    }
                    // A dynamic argument would bind at run time, possibly to another overload; as object it binds to this method.
                    string arguments = string.Join(", ", method.Parameters.Select(p => (p.Type.TypeKind != TypeKind.Dynamic ? string.Empty
                        : _emitter.Nullable && p.Type.NullableAnnotation == NullableAnnotation.Annotated ? "(object?)" : "(object)") + Escape(p.Name)));
                    AppendMethod(doc.ToString(), name, string.Concat(parameters.Select(p => ", " + p)),
                        "            " + target + "." + Escape(method.Name) + "(" + arguments + ");\n");
                    if (splitParams && method.Parameters[0].Type is IArrayTypeSymbol array && Claim(location, member, name, array.ElementType, array)) {
                        string element = array.ElementType.ToDisplayString(TypeFormat);
                        // The array type as Roslyn writes it: element + "[]" would turn int[][,] into int[,][].
                        string arrayType = array.ToDisplayString(TypeFormat);
                        // new int[n][] for jagged items: the length goes before the element type's own ranks.
                        string ranks = string.Empty;
                        ITypeSymbol innermost = array.ElementType;
                        while (innermost is IArrayTypeSymbol nested) {
                            ranks += "[" + new string(',', nested.Rank - 1) + "]";
                            innermost = nested.ElementType;
                        }
                        string items = "            " + arrayType + " value = new " + innermost.ToDisplayString(TypeFormat) + "[(items == null ? 0 : items.Length) + 1]" + ranks + ";\n"
                            + "            value[0] = item;\n"
                            + "            if (items != null) { global::System.Array.Copy(items, 0, value, 1, items.Length); }\n";
                        string itemsDoc = "        /// <summary>" + summary + "</summary>\n"
                            + "        /// <param name=\"" + _receiver.TrimStart('@') + "\">The instance.</param>\n"
                            + "        /// <param name=\"item\">The first item.</param>\n"
                            + "        /// <param name=\"items\">The other items.</param>\n"
                            + "        /// <returns>The same instance.</returns>\n";
                        AppendMethod(itemsDoc, name, ", " + element + " item, params " + arrayType + " items",
                            items + "            " + target + "." + Escape(method.Name) + "(value);\n");
                    } else if (splitParams && CollectionElement(method.Parameters[0].Type) is ITypeSymbol item
                        && Claim(location, member, name, item, Compilation.CreateArrayTypeSymbol(item))) {
                        // A params collection (List<T>, IEnumerable<T>, a span) gets the same at-least-one-item overload, built with
                        // a collection expression, which any params collection type accepts.
                        string element = item.ToDisplayString(TypeFormat);
                        string itemsDoc = "        /// <summary>" + summary + "</summary>\n"
                            + "        /// <param name=\"" + _receiver.TrimStart('@') + "\">The instance.</param>\n"
                            + "        /// <param name=\"item\">The first item.</param>\n"
                            + "        /// <param name=\"items\">The other items.</param>\n"
                            + "        /// <returns>The same instance.</returns>\n";
                        AppendMethod(itemsDoc, name, ", " + element + " item, params " + element + "[] items",
                            "            " + target + "." + Escape(method.Name) + "([item, .. (items ?? global::System.Array.Empty<" + element + ">())]);\n");
                    }
                }

                // The item type of a params collection: the type argument of a span, or of the IEnumerable<T> it implements.
                private static ITypeSymbol? CollectionElement(ITypeSymbol type) {
                    if (type is INamedTypeSymbol { TypeArguments.Length: 1 } span && span.ContainingNamespace?.ToDisplayString() == "System"
                        && span.Name is "Span" or "ReadOnlySpan") {
                        return span.TypeArguments[0];
                    }
                    return new[] { type }.Concat(type.AllInterfaces).OfType<INamedTypeSymbol>()
                        .FirstOrDefault(t => t.OriginalDefinition.SpecialType == SpecialType.System_Collections_Generic_IEnumerable_T)?.TypeArguments[0];
                }

                // [CallerMemberName] and the other caller-info attributes, so the compiler fills them in for the wrapper's caller as it
                // would for a direct call.
                private static string CallerInfo(IParameterSymbol parameter) => string.Concat(parameter.GetAttributes()
                    .Where(a => a.AttributeClass is { ContainingNamespace: { } ns } type && ns.ToDisplayString() == "System.Runtime.CompilerServices"
                        && type.Name is "CallerMemberNameAttribute" or "CallerFilePathAttribute" or "CallerLineNumberAttribute" or "CallerArgumentExpressionAttribute")
                    .Select(a => "[" + a.AttributeClass!.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                        + (a.ConstructorArguments.Length == 1 && a.ConstructorArguments[0].Value is string target ? "(" + Builder.LiteralOf(target) + ")" : string.Empty) + "] "));

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
                private bool Mutated(string name, ITypeSymbol type) => _mutated.Contains(SignatureKey(name, new[] { type }));
            }
        }
    }
}
