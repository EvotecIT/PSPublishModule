using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using static PowerForge.Generators.Fluent.FluentModel;

namespace PowerForge.Generators.Fluent {
    // Convention-mode shortcuts: [FluentInline] settings, [FluentPreset] values and copy-then-commit configure methods.
    public sealed partial class FluentGenerator {
        private sealed partial class Emitter {
            private readonly Dictionary<INamedTypeSymbol, IMethodSymbol?> _copies = new Dictionary<INamedTypeSymbol, IMethodSymbol?>(SymbolEqualityComparer.Default);

            // The copy method a settings type declares, or inherits, with [Fluent(Copy = "Name")]; null when it has none or
            // the named method does not fit (reported once per type, when this assembly declares the attribute). A settings
            // type from a referenced library uses its copy method only when that method is visible here; otherwise it is
            // configured in place, since the consumer could not fix it.
            internal IMethodSymbol? CopyMethod(INamedTypeSymbol type) {
                type = (INamedTypeSymbol)type.WithNullableAnnotation(NullableAnnotation.NotAnnotated);
                if (_copies.TryGetValue(type, out IMethodSymbol? cached)) {
                    return cached;
                }
                IMethodSymbol? result = null;
                // The most derived declaration wins.
                AttributeData? attribute = BaseTypes(type).Select(t => Attribute(t.OriginalDefinition, FluentAttributes.Fluent))
                    .FirstOrDefault(a => a != null && a.NamedArguments.Any(n => n.Key == "Copy" && n.Value.Value is string));
                if (attribute != null) {
                    string name = (string)attribute.NamedArguments.First(n => n.Key == "Copy").Value.Value!;
                    result = BaseTypes(type).SelectMany(t => t.GetMembers(name)).OfType<IMethodSymbol>()
                        .FirstOrDefault(m => m.MethodKind == MethodKind.Ordinary && !m.IsStatic && !m.IsGenericMethod && m.Parameters.Length == 0
                            && !m.ReturnsVoid && m.RefKind == RefKind.None && _compilation.IsSymbolAccessibleWithin(m, _compilation.Assembly)
                            && (_compilation.ClassifyCommonConversion(m.ReturnType, type) is { Exists: true, IsUserDefined: false } conversion
                                && (conversion.IsIdentity || conversion.IsReference)));
                    // An attribute inherited from a referenced base has no syntax here: this project cannot change it.
                    if (result == null && attribute.ApplicationSyntaxReference is SyntaxReference reference) {
                        _output.ReportDiagnostic(Diagnostic.Create(FluentDiagnostics.InvalidCopy, reference.GetSyntax().GetLocation(), name, type.Name));
                    }
                }
                _copies[type] = result;
                return result;
            }

            // The forwards an owner inlining a settings type emits. A helper overload this assembly cannot call (an internal
            // overload of a referenced library, which metadata does not even show here) is reached through the public extension
            // that library generated for it: each such extension whose value type no callable overload covers is added. A name
            // with no overload left is left out, and the settings type's own property method stays on the owner.
            internal List<(string Name, IMethodSymbol Target, string Summary, string ValueSummary)> InlineForwards(INamedTypeSymbol option) {
                List<(string Name, IMethodSymbol Target, string Summary, string ValueSummary)> result = Forwards(option, report: false).ToList();
                if (SymbolEqualityComparer.Default.Equals(option.ContainingAssembly, _compilation.Assembly)) {
                    return result;
                }
                List<INamedTypeSymbol> declarers = BaseTypes(option).Concat(option.AllInterfaces).Select(t => t.OriginalDefinition).ToList();
                List<INamedTypeSymbol> fluentClasses = FluentClasses(declarers);
                foreach (AttributeData attribute in declarers.SelectMany(t => t.GetAttributes()).Where(a => a.AttributeClass?.ToDisplayString() == FluentAttributes.Forward)) {
                    if (attribute.ConstructorArguments.Length != 3 || attribute.ConstructorArguments[0].Value is not string name) {
                        continue;
                    }
                    string summary = attribute.NamedArguments.FirstOrDefault(a => a.Key == "Summary").Value.Value as string ?? "Sets " + name + ".";
                    string value = attribute.NamedArguments.FirstOrDefault(a => a.Key == "ValueSummary").Value.Value as string ?? "The value.";
                    IEnumerable<IMethodSymbol> extensions = fluentClasses.SelectMany(type => type.GetMembers(name)).OfType<IMethodSymbol>()
                        .Where(m => m.IsStatic && m.IsExtensionMethod && m.Parameters.Length == 2 && m.Parameters.All(p => p.RefKind == RefKind.None)
                            && Receives(m.Parameters[0].Type, option) && _compilation.IsSymbolAccessibleWithin(m, _compilation.Assembly));
                    foreach (IMethodSymbol extension in extensions) {
                        if (!result.Any(forward => forward.Name == name && SymbolEqualityComparer.Default.Equals(forward.Target.Parameters[1].Type, extension.Parameters[1].Type))) {
                            result.Add((name, extension, summary, value));
                        }
                    }
                }
                return result;
            }

            // The fluent classes a library generates for these types: one per type, or one per namespace in Prefixed naming.
            private List<INamedTypeSymbol> FluentClasses(IEnumerable<INamedTypeSymbol> types) => types
                .SelectMany(t => new[] { FluentStem(t), t.ContainingNamespace.IsGlobalNamespace ? "Global" : t.ContainingNamespace.Name }
                    .Select(stem => t.ContainingNamespace.IsGlobalNamespace ? stem + "Fluent" : t.ContainingNamespace.ToDisplayString() + "." + stem + "Fluent"))
                .Distinct(StringComparer.Ordinal).Select(name => _compilation.GetTypeByMetadataName(name)).Where(type => type != null).Select(type => type!).ToList();

            private readonly Dictionary<INamedTypeSymbol, HashSet<string>?> _referencedSignatures = new Dictionary<INamedTypeSymbol, HashSet<string>?>(SymbolEqualityComparer.Default);

            // The value-parameter signatures generated for a type that can be derived from: by this run for a type of this
            // assembly, or read from the generic (TSelf : type) extensions a referenced library generated for its own type.
            // Null when nothing was generated for it.
            internal HashSet<string>? GeneratedSignatures(INamedTypeSymbol type) {
                if (_signatures.TryGetValue(type, out HashSet<string>? local)) {
                    return local;
                }
                if (SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, _compilation.Assembly)) {
                    return null;
                }
                if (_referencedSignatures.TryGetValue(type, out HashSet<string>? cached)) {
                    return cached;
                }
                HashSet<string>? result = null;
                IEnumerable<IMethodSymbol> methods = FluentClasses(new[] { type }).SelectMany(t => t.GetMembers()).OfType<IMethodSymbol>()
                    .Where(m => m.IsExtensionMethod && m.DeclaredAccessibility == Accessibility.Public && m.TypeParameters.Length == 1
                        && m.Parameters[0].Type is ITypeParameterSymbol self && SymbolEqualityComparer.Default.Equals(self, m.TypeParameters[0])
                        && self.ConstraintTypes.Any(constraint => SymbolEqualityComparer.Default.Equals(constraint.OriginalDefinition, type)));
                foreach (IMethodSymbol method in methods) {
                    List<IParameterSymbol> values = method.Parameters.Skip(1).ToList();
                    result ??= new HashSet<string>(StringComparer.Ordinal);
                    result.Add(SignatureKey(method.Name, values.Select(parameter => parameter.Type)));
                    if (values.Count > 0 && values.All(parameter => parameter.IsOptional)) {
                        result.Add(method.Name + "()");
                    }
                }
                _referencedSignatures[type] = result;
                return result;
            }

            // Whether a referenced library's convention fluent class for the type (<Type>Fluent, not Prefixed naming's
            // <Namespace>Fluent) has a public extension with this value-parameter key. In that class a property method gives way
            // to a mutator of the same signature, so the method is the mutator's.
            internal bool ConventionGenerated(INamedTypeSymbol type, string key) {
                type = type.OriginalDefinition;
                string stem = FluentStem(type);
                if (stem == (type.ContainingNamespace.IsGlobalNamespace ? "Global" : type.ContainingNamespace.Name)) {
                    return false;
                }
                INamedTypeSymbol? fluent = _compilation.GetTypeByMetadataName(type.ContainingNamespace.IsGlobalNamespace ? stem + "Fluent" : type.ContainingNamespace.ToDisplayString() + "." + stem + "Fluent");
                return fluent != null && fluent.GetMembers().OfType<IMethodSymbol>().Any(m => m.IsExtensionMethod && m.DeclaredAccessibility == Accessibility.Public
                    && m.Parameters.Length > 0 && Receives(m.Parameters[0].Type, type) && SignatureKey(m.Name, m.Parameters.Skip(1).Select(p => p.Type)) == key);
            }

            // The public preset extension (name(this T)) a referenced library generated for its settings type, or null.
            internal IMethodSymbol? ReferencedPreset(INamedTypeSymbol option, string name) =>
                SymbolEqualityComparer.Default.Equals(option.ContainingAssembly, _compilation.Assembly) ? null
                : FluentClasses(BaseTypes(option).Select(t => t.OriginalDefinition)).SelectMany(t => t.GetMembers(name)).OfType<IMethodSymbol>()
                    .FirstOrDefault(m => m.IsExtensionMethod && m.Parameters.Length == 1 && m.Parameters[0].RefKind == RefKind.None
                        && Receives(m.Parameters[0].Type, option) && _compilation.IsSymbolAccessibleWithin(m, _compilation.Assembly));

            private sealed partial class Builder {
                // [FluentInline]: the owner gets every method the property's type gets, applied to the property.
                private void AppendInline(IPropertySymbol property) {
                    if (Prefixed) { return; }
                    INamedTypeSymbol? option = InlineType(property);
                    if (option == null) {
                        Report(FluentDiagnostics.InvalidInline, property, _type.Name, property.Name);
                        return;
                    }
                    // Every inlined method would edit an object the getter builds and then drops.
                    if (!Forced(property) && FreshGetterReason(property, Compilation) is string skipped) {
                        ReportFresh(property, "inlined methods", skipped);
                        return;
                    }
                    // A [FluentForward] name the owner can call replaces the property of that name on the inlined type too.
                    List<(string Name, IMethodSymbol Target, string Summary, string ValueSummary)> forwards = _emitter.InlineForwards(option);
                    HashSet<string> forwarded = new HashSet<string>(forwards.Select(forward => forward.Name), StringComparer.Ordinal);
                    _via = property;
                    try {
                        foreach ((string name, IMethodSymbol target, string summary, string valueSummary) in forwards) {
                            AppendForward(name, target, summary, valueSummary);
                        }
                        AppendMutators(option);
                        foreach (IPropertySymbol member in Properties(option)) {
                            if (!forwarded.Contains(member.Name)) {
                                AppendConvention(member);
                            }
                        }
                    } finally {
                        _via = null;
                    }
                }

                // The settings type a [FluentInline] property expands, or null when it can't be inlined.
                private INamedTypeSymbol? InlineType(IPropertySymbol property) =>
                    IsReadable(property) && property.Type is INamedTypeSymbol option && property.Type.NullableAnnotation != NullableAnnotation.Annotated
                        && CollectionItem(property.Type) == null && _emitter.IsSettingsType(option) ? option : null;

                // The mutators inlined properties bring are the canonical write path whatever the declaration order, so their
                // signatures are reserved before any property setter can take them. That includes a property a generated base
                // covers: its methods come from the base, and this type's setter must not win over them.
                private void ReserveInlineMutators(IEnumerable<IPropertySymbol> properties) {
                    foreach (IPropertySymbol property in properties) {
                        // A covered property's methods are the ones its generated base declaration emits, so that declaration
                        // decides whether it is inlined, not an override's own attributes.
                        IPropertySymbol declaring = property;
                        bool covered = CoveredByBase(property);
                        if (covered) {
                            while (declaring.OverriddenProperty != null) { declaring = declaring.OverriddenProperty; }
                        }
                        if (Attribute(declaring, FluentAttributes.Inline) == null || InlineType(declaring) is not INamedTypeSymbol option
                            || !Forced(declaring) && FreshGetterReason(declaring, Compilation) != null) {
                            continue;
                        }
                        // A referenced base reserves only what it generated: one built with Prefixed naming has no inline methods.
                        bool local = SymbolEqualityComparer.Default.Equals(declaring.ContainingType.ContainingAssembly, Compilation.Assembly);
                        foreach ((IMethodSymbol method, string name) in _emitter.Mutators(option)) {
                            string key = SignatureKey(name, method.Parameters.Select(p => p.Type));
                            if (!covered || local || _emitter.ConventionGenerated(declaring.ContainingType, key)) {
                                _mutated.Add(key);
                            }
                        }
                    }
                }

                // [FluentForward]: name(value) calls the static target with the receiver, or with the inlined property's value.
                private void AppendForward(string name, IMethodSymbol target, string summary, string valueSummary) {
                    IParameterSymbol value = target.Parameters[1];
                    if (!SyntaxFacts.IsValidIdentifier(name)) {
                        if (_via == null) {
                            _emitter._output.ReportDiagnostic(Diagnostic.Create(FluentDiagnostics.InvalidName, target.Locations.FirstOrDefault() ?? Location.None, name, _type.Name, target.Name));
                        }
                        return;
                    }
                    if (Prefixed ? Taken(name, value.Type) || !_generated.Add(name + "(" + value.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ")")
                        : !Claim(_via?.Locations.FirstOrDefault() ?? target.Locations.FirstOrDefault(), _via == null ? target.Name : _via.Name + "." + name, name, value.Type)) { return; }
                    string owner = _via == null ? _receiver : _receiver + "." + Escape(_via.Name);
                    AppendMethod(Doc(summary, valueSummary, value.Name), Escape(name), ", " + value.Type.ToDisplayString(TypeFormat) + " " + Escape(value.Name),
                        "            " + target.ContainingType.ToDisplayString(TypeFormat) + "." + Escape(target.Name) + "(" + owner + ", " + Escape(value.Name) + ");\n");
                }

                // [FluentPreset]: name() assigns a fixed value.
                private void AppendPresets(IPropertySymbol property) {
                    if (Prefixed) { return; }
                    foreach (AttributeData attribute in property.GetAttributes().Where(a => a.AttributeClass?.ToDisplayString() == FluentAttributes.Preset)) {
                        if (attribute.ConstructorArguments.Length < 2 || attribute.ConstructorArguments[0].Value is not string name) {
                            continue;
                        }
                        if (!ValidName(property, name)) { continue; }
                        string? failure = null;
                        (string Expression, string Display)? value = null;
                        if (!IsSettable(property)) {
                            failure = "needs a property with a public setter";
                        } else if (attribute.ConstructorArguments.Length == 2) {
                            value = Constant(attribute.ConstructorArguments[1], property.Type, out failure);
                        } else {
                            value = StaticMember(attribute.ConstructorArguments[1], attribute.ConstructorArguments[2], property.Type, out failure);
                        }
                        if (value == null && _via?.Type is INamedTypeSymbol inlined && _emitter.ReferencedPreset(inlined, name) is IMethodSymbol preset) {
                            // A referenced library's preset backed by a member only it can read: call the extension it generated.
                            if (Claim(property, name)) {
                                AppendMethod("        /// <summary>" + (attribute.NamedArguments.FirstOrDefault(a => a.Key == "Summary").Value.Value as string ?? "Applies the " + new XText(name) + " preset of " + Cref(property) + ".") + "</summary>\n"
                                    + "        /// <param name=\"" + _receiver.TrimStart('@') + "\">The instance.</param>\n"
                                    + "        /// <returns>The same instance.</returns>\n", name, string.Empty,
                                    "            " + preset.ContainingType.ToDisplayString(TypeFormat) + "." + Escape(preset.Name) + "(" + _receiver + "." + Escape(_via.Name) + ");\n");
                            }
                            continue;
                        }
                        if (value == null) {
                            if (_via == null) {
                                Location location = attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? property.Locations.FirstOrDefault() ?? Location.None;
                                _emitter._output.ReportDiagnostic(Diagnostic.Create(FluentDiagnostics.InvalidPreset, location, name, _type.Name, property.Name, failure ?? "has an unsupported value"));
                            }
                            continue;
                        }
                        if (!Claim(property, name)) { continue; }
                        string summary = attribute.NamedArguments.FirstOrDefault(a => a.Key == "Summary").Value.Value as string
                            ?? "Sets " + Cref(property) + " to <c>" + new XText(value.Value.Display) + "</c>.";
                        AppendMethod("        /// <summary>" + summary + "</summary>\n"
                            + "        /// <param name=\"" + _receiver.TrimStart('@') + "\">The instance.</param>\n"
                            + "        /// <returns>The same instance.</returns>\n", name, string.Empty, Assign(property, value.Value.Expression));
                    }
                }

                // Visible(bool value = true) is also called as Visible(); reserve that form so a preset cannot shadow it.
                private bool ClaimShortForm(IPropertySymbol property, string name) {
                    if (_generated.Add(name + "()")) { return true; }
                    _emitter._output.ReportDiagnostic(Diagnostic.Create(FluentDiagnostics.Duplicate, (_via ?? property).Locations.FirstOrDefault() ?? Location.None, name, _type.Name, Label(property)));
                    return false;
                }

                // X(Action<T>) on a settable property of a copyable type: the callback edits a copy, which is copied again and
                // assigned only when the callback returns, so the owner never shares an instance the caller still holds.
                private void AppendCopyOption(IPropertySymbol property, INamedTypeSymbol option, IMethodSymbol copy, string name, string cref, string summary, string optionType, bool canCreate) {
                    string current = "(" + Member(property) + " ?? "
                        + (canCreate ? "new " + optionType + "()" : "throw new global::System.InvalidOperationException(\"" + property.Name + " is not set.\")") + ")";
                    string statements = NullCheck("configure")
                        + "            " + optionType + " value = " + Copy(current, option, copy, optionType) + ";\n"
                        + "            configure(value);\n"
                        + Assign(property, Copy("value", option, copy, optionType));
                    AppendMethod(Doc("Configures a copy of " + cref + (canCreate ? " (a new one when not set)" : string.Empty)
                        + " and assigns a copy of it once the callback returns, leaving the property unchanged when the callback throws" + summary, "Configures the copy.", "configure"),
                        name, ", global::System.Action<" + optionType + "> configure", statements);
                }

                // Calls the validated method through its declaring type, so a member of the same name that a derived type
                // declares with new cannot take the call.
                private string Copy(string target, INamedTypeSymbol option, IMethodSymbol copy, string optionType) {
                    string receiver = SymbolEqualityComparer.Default.Equals(copy.ContainingType, option) ? target
                        : "((" + copy.ContainingType.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString(TypeFormat) + ")" + target + ")";
                    string call = receiver + "." + Escape(copy.Name) + "()";
                    bool cast = !Compilation.ClassifyCommonConversion(copy.ReturnType, option).IsImplicit;
                    string suppress = _emitter.Nullable && copy.ReturnType.NullableAnnotation == NullableAnnotation.Annotated ? "!" : string.Empty;
                    return cast ? "(" + optionType + ")" + call + suppress : call + suppress;
                }

                // A constant preset value as C#: enum members by name, literals with the suffix their type needs.
                private (string Expression, string Display)? Constant(TypedConstant constant, ITypeSymbol target, out string? failure) {
                    failure = null;
                    if (constant.IsNull) {
                        if (target.IsValueType && target.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T) {
                            failure = "assigns null to a non-nullable value type";
                            return null;
                        }
                        return ("null", "null");
                    }
                    if (constant.Type == null || constant.Kind == TypedConstantKind.Array || constant.Kind == TypedConstantKind.Error) {
                        failure = "has an unsupported value";
                        return null;
                    }
                    Conversion conversion = ((CSharpCompilation)Compilation).ClassifyConversion(constant.Type, target);
                    if (!conversion.IsImplicit) {
                        // The constant 0 converts to any enum (C# implicit enumeration conversion), also to its nullable form.
                        if (constant.Type.SpecialType == SpecialType.System_Int32 && constant.Value is 0 && EnumOf(target) is INamedTypeSymbol zeroEnum) {
                            return ("((" + zeroEnum.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ")0)", "0");
                        }
                        // An integral constant that fits converts implicitly (byte size = 1), and is emitted with the target's type.
                        object? fitted = FitConstant(constant.Type, constant.Value, target);
                        string? converted = fitted == null ? null : Literal(fitted);
                        if (converted == null) {
                            failure = "has a " + constant.Type.ToDisplayString() + " value, which cannot be assigned to " + target.ToDisplayString();
                            return null;
                        }
                        return (converted, Convert.ToString(constant.Value, CultureInfo.InvariantCulture));
                    }
                    if (constant.Kind == TypedConstantKind.Type && constant.Value is ITypeSymbol type) {
                        return ("typeof(" + type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ")", "typeof(" + type.ToDisplayString() + ")");
                    }
                    if (constant.Kind == TypedConstantKind.Enum && constant.Type is INamedTypeSymbol enumType) {
                        IFieldSymbol? member = enumType.GetMembers().OfType<IFieldSymbol>().FirstOrDefault(f => f.HasConstantValue && Equals(f.ConstantValue, constant.Value));
                        string qualified = enumType.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat);
                        if (member != null) {
                            return (qualified + "." + Escape(member.Name), enumType.Name + "." + member.Name);
                        }
                        string? underlying = Literal(constant.Value);
                        return underlying == null ? null : ("((" + qualified + ")(" + underlying + "))", "(" + enumType.Name + ")" + underlying);
                    }
                    string? literal = Literal(constant.Value);
                    if (literal == null) {
                        failure = "has an unsupported value";
                        return null;
                    }
                    return (literal, literal);
                }

                // type.member: a parameterless static method is called, a static property or field is read.
                private (string Expression, string Display)? StaticMember(TypedConstant typeArgument, TypedConstant memberArgument, ITypeSymbol target, out string? failure) {
                    failure = null;
                    if (typeArgument.Value is not INamedTypeSymbol type || memberArgument.Value is not string name) {
                        failure = "needs a type and a member name";
                        return null;
                    }
                    foreach (ISymbol candidate in BaseTypes(type).SelectMany(t => t.GetMembers(name))) {
                        if (!candidate.IsStatic || !Compilation.IsSymbolAccessibleWithin(candidate, Compilation.Assembly)) { continue; }
                        ITypeSymbol? valueType;
                        bool call;
                        switch (candidate) {
                            case IMethodSymbol method when method.MethodKind == MethodKind.Ordinary && method.Parameters.Length == 0 && !method.IsGenericMethod && !method.ReturnsVoid && method.RefKind == RefKind.None:
                                valueType = method.ReturnType;
                                call = true;
                                break;
                            case IPropertySymbol property when !property.IsIndexer && property.GetMethod != null && Compilation.IsSymbolAccessibleWithin(property.GetMethod, Compilation.Assembly):
                                valueType = property.Type;
                                call = false;
                                break;
                            case IFieldSymbol field:
                                valueType = field.Type;
                                call = false;
                                break;
                            default:
                                continue;
                        }
                        // Qualified by the type that declares the accepted member, so a hiding member of the requested type is not read.
                        INamedTypeSymbol declaring = candidate.ContainingType;
                        string suffix = call ? "()" : string.Empty;
                        string expression = declaring.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "." + Escape(name) + suffix;
                        string display = declaring.Name + "." + name + suffix;
                        if (((CSharpCompilation)Compilation).ClassifyConversion(valueType, target).IsImplicit) {
                            return (expression, display);
                        }
                        // A const field is a constant expression, so it converts like a literal: an integral constant that fits
                        // (byte size = Sizes.Small) and a zero to an enum. Other static members keep the type's own conversions.
                        if (candidate is IFieldSymbol { HasConstantValue: true } constant) {
                            if (FitConstant(valueType, constant.ConstantValue, target) != null) {
                                return (expression, display);
                            }
                            if (IsZero(valueType, constant.ConstantValue) && EnumOf(target) is INamedTypeSymbol zeroEnum) {
                                return ("((" + zeroEnum.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ")" + expression + ")", display);
                            }
                        }
                    }
                    failure = "names '" + type.Name + "." + name + "', which is not an accessible static method without parameters, property or field whose value can be assigned to " + target.ToDisplayString();
                    return null;
                }

                // An integral (not enum) constant with the value zero, which C# converts to any enum.
                private static bool IsZero(ITypeSymbol type, object? value) => type.TypeKind != TypeKind.Enum && type.SpecialType switch {
                    SpecialType.System_SByte or SpecialType.System_Byte or SpecialType.System_Int16 or SpecialType.System_UInt16 or SpecialType.System_Int32
                        or SpecialType.System_UInt32 or SpecialType.System_Int64 or SpecialType.System_UInt64 => Convert.ToDecimal(value, CultureInfo.InvariantCulture) == 0,
                    _ => false
                };

                private static INamedTypeSymbol? EnumOf(ITypeSymbol target) {
                    if (target.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T && target is INamedTypeSymbol nullable) {
                        target = nullable.TypeArguments[0];
                    }
                    return target.TypeKind == TypeKind.Enum ? target as INamedTypeSymbol : null;
                }

                // The C# implicit constant expression conversions: an int constant to sbyte, byte, short, ushort, uint or ulong,
                // and a long constant to ulong, when the value is in range (also for the nullable form of the target). An enum
                // constant has none of these conversions, even when its underlying value would fit.
                private static object? FitConstant(ITypeSymbol source, object? value, ITypeSymbol target) {
                    if (source.SpecialType != SpecialType.System_Int32 && source.SpecialType != SpecialType.System_Int64) {
                        return null;
                    }
                    if (target.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T && target is INamedTypeSymbol nullable) {
                        target = nullable.TypeArguments[0];
                    }
                    switch (value) {
                        case int number:
                            switch (target.SpecialType) {
                                case SpecialType.System_SByte: return number >= sbyte.MinValue && number <= sbyte.MaxValue ? (sbyte)number : null;
                                case SpecialType.System_Byte: return number >= byte.MinValue && number <= byte.MaxValue ? (byte)number : null;
                                case SpecialType.System_Int16: return number >= short.MinValue && number <= short.MaxValue ? (short)number : null;
                                case SpecialType.System_UInt16: return number >= ushort.MinValue && number <= ushort.MaxValue ? (ushort)number : null;
                                case SpecialType.System_UInt32: return number >= 0 ? (uint)number : null;
                                case SpecialType.System_UInt64: return number >= 0 ? (ulong)number : null;
                                default: return null;
                            }
                        case long number:
                            return target.SpecialType == SpecialType.System_UInt64 && number >= 0 ? (ulong)number : null;
                        default:
                            return null;
                    }
                }

                private static string? Literal(object? value) {
                    CultureInfo invariant = CultureInfo.InvariantCulture;
                    switch (value) {
                        case bool flag: return flag ? "true" : "false";
                        case string text: return SymbolDisplay.FormatLiteral(text, quote: true);
                        case char character: return SymbolDisplay.FormatLiteral(character, quote: true);
                        case int number: return number.ToString(invariant);
                        case long number: return number.ToString(invariant) + "L";
                        case uint number: return number.ToString(invariant) + "U";
                        case ulong number: return number.ToString(invariant) + "UL";
                        case byte number: return "((global::System.Byte)" + number.ToString(invariant) + ")";
                        case sbyte number: return "((global::System.SByte)(" + number.ToString(invariant) + "))";
                        case short number: return "((global::System.Int16)(" + number.ToString(invariant) + "))";
                        case ushort number: return "((global::System.UInt16)" + number.ToString(invariant) + ")";
                        case float number:
                            return float.IsNaN(number) ? "global::System.Single.NaN" : float.IsPositiveInfinity(number) ? "global::System.Single.PositiveInfinity"
                                : float.IsNegativeInfinity(number) ? "global::System.Single.NegativeInfinity" : number.ToString("R", invariant) + "F";
                        case double number:
                            return double.IsNaN(number) ? "global::System.Double.NaN" : double.IsPositiveInfinity(number) ? "global::System.Double.PositiveInfinity"
                                : double.IsNegativeInfinity(number) ? "global::System.Double.NegativeInfinity" : number.ToString("R", invariant) + "D";
                        default: return null;
                    }
                }
            }
        }
    }
}
