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
            // type from a referenced library has none here: it is configured in place, since its copy method usually is not
            // visible outside that library and the consumer could not fix it.
            internal IMethodSymbol? CopyMethod(INamedTypeSymbol type) {
                type = (INamedTypeSymbol)type.WithNullableAnnotation(NullableAnnotation.NotAnnotated);
                if (_copies.TryGetValue(type, out IMethodSymbol? cached)) {
                    return cached;
                }
                IMethodSymbol? result = null;
                if (!SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, _compilation.Assembly)) {
                    _copies[type] = null;
                    return null;
                }
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

            private sealed partial class Builder {
                // [FluentInline]: the owner gets every method the property's type gets, applied to the property.
                private void AppendInline(IPropertySymbol property) {
                    if (Prefixed) { return; }
                    if (!IsReadable(property) || property.Type is not INamedTypeSymbol option || property.Type.NullableAnnotation == NullableAnnotation.Annotated
                        || CollectionItem(property.Type) != null || !IsSettingsType(option)) {
                        Report(FluentDiagnostics.InvalidInline, property, _type.Name, property.Name);
                        return;
                    }
                    // A [FluentForward] name replaces the property of that name on the inlined type too.
                    HashSet<string> forwarded = new HashSet<string>(BaseTypes(option).Concat(option.AllInterfaces).Select(t => t.OriginalDefinition)
                        .SelectMany(t => t.GetAttributes()).Where(a => a.AttributeClass?.ToDisplayString() == FluentAttributes.Forward && a.ConstructorArguments.Length == 3)
                        .Select(a => a.ConstructorArguments[0].Value as string).Where(name => name != null).Select(name => name!), StringComparer.Ordinal);
                    _via = property;
                    try {
                        foreach ((string name, IMethodSymbol target, string summary, string valueSummary) in _emitter.Forwards(option, report: false)) {
                            AppendForward(name, target, summary, valueSummary);
                        }
                        foreach (IPropertySymbol member in Properties(option)) {
                            if (!forwarded.Contains(member.Name)) {
                                AppendConvention(member);
                            }
                        }
                    } finally {
                        _via = null;
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
                        // An integral constant that fits converts implicitly (byte size = 1), and is emitted with the target's type.
                        object? fitted = FitConstant(constant.Value, target);
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
                        if (!((CSharpCompilation)Compilation).ClassifyConversion(valueType, target).IsImplicit) { continue; }
                        string suffix = call ? "()" : string.Empty;
                        // Qualified by the type that declares the accepted member, so a hiding member of the requested type is not read.
                        INamedTypeSymbol declaring = candidate.ContainingType;
                        return (declaring.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + "." + Escape(name) + suffix, declaring.Name + "." + name + suffix);
                    }
                    failure = "names '" + type.Name + "." + name + "', which is not an accessible static method without parameters, property or field whose value can be assigned to " + target.ToDisplayString();
                    return null;
                }

                // The C# implicit constant expression conversions: an int constant to sbyte, byte, short, ushort, uint or ulong,
                // and a long constant to ulong, when the value is in range (also for the nullable form of the target).
                private static object? FitConstant(object? value, ITypeSymbol target) {
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
