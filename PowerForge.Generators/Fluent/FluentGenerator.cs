using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;
using static PowerForge.Generators.Fluent.FluentModel;

namespace PowerForge.Generators.Fluent {
    /// <summary>
    /// Generates fluent extension methods on a library's own model types from their public properties, so chaining,
    /// object initializers and plain property access all edit the same object. Types opt in with <c>[Fluent]</c> on the
    /// type, a base class or an interface; settings types reachable from their properties are included too.
    /// <para>
    /// Every method returns the same instance, typed as the receiver: methods for a type that can be derived from are
    /// generic over <c>TSelf</c>, so a chain on a derived type keeps that type. No reflection is used.
    /// </para>
    /// <para>
    /// The default naming follows the shared authoring convention: <c>Title(value)</c>, <c>Visible(bool value = true)</c>,
    /// <c>Legend(o =&gt; ...)</c> for a settings object, <c>Series(item)</c>, <c>Series(o =&gt; ...)</c> and
    /// <c>Series(items)</c> for a collection, and a declared verb for a delegate. A collision is a build error.
    /// <c>PowerForgeFluentNaming=Prefixed</c> keeps the earlier ConsoleMonster names (<c>AddX</c>, <c>AddXs</c>,
    /// <c>Configure</c>, <c>X()</c>) and skips a method that already exists, until that library moves to the convention.
    /// </para>
    /// </summary>
    [Generator(LanguageNames.CSharp)]
    public sealed class FluentGenerator : IIncrementalGenerator {
        /// <inheritdoc />
        public void Initialize(IncrementalGeneratorInitializationContext context) {
            context.RegisterPostInitializationOutput(output => output.AddSource("PowerForge.Fluent.Attributes.g.cs", FluentAttributes.Source));
            IncrementalValueProvider<FluentSettings> settings = context.AnalyzerConfigOptionsProvider.Select((options, _) => FluentSettings.Read(options.GlobalOptions));
            context.RegisterSourceOutput(context.CompilationProvider.Combine(settings), (output, input) => new Emitter(input.Left, input.Right, output).Run());
        }

        private sealed class FluentSettings : IEquatable<FluentSettings> {
            internal bool Prefixed { get; private set; }

            internal string? Factory { get; private set; }

            internal static FluentSettings Read(AnalyzerConfigOptions options) {
                options.TryGetValue("build_property.PowerForgeFluentNaming", out string? naming);
                options.TryGetValue("build_property.PowerForgeFluentFactory", out string? factory);
                return new FluentSettings {
                    Prefixed = string.Equals(naming?.Trim(), "Prefixed", StringComparison.OrdinalIgnoreCase),
                    Factory = string.IsNullOrWhiteSpace(factory) ? null : factory!.Trim()
                };
            }

            public bool Equals(FluentSettings? other) => other != null && other.Prefixed == Prefixed && other.Factory == Factory;

            public override bool Equals(object? obj) => Equals(obj as FluentSettings);

            public override int GetHashCode() => (Prefixed ? 1 : 0) ^ (Factory?.GetHashCode() ?? 0);
        }

        private sealed class Emitter {
            private static readonly SymbolDisplayFormat NullableFormat = SymbolDisplayFormat.FullyQualifiedFormat
                .AddMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

            private readonly Compilation _compilation;
            private readonly FluentSettings _settings;
            private readonly SourceProductionContext _output;
            private readonly List<INamedTypeSymbol> _factories;
            private ILookup<string, IMethodSymbol>? _extensions;

            internal Emitter(Compilation compilation, FluentSettings settings, SourceProductionContext output) {
                _compilation = compilation;
                _settings = settings;
                _output = output;
                // One or more static factory classes (ChartForgeX has Chart and Diagram).
                _factories = ListSetting(settings.Factory)
                    .Select(name => compilation.GetTypeByMetadataName(name)).Where(type => type != null).Select(type => type!).ToList();
                // C# 7.3 (the default for netstandard2.0 and .NET Framework) has no nullable annotations or ??=.
                Nullable = compilation is CSharpCompilation csharp && csharp.LanguageVersion >= LanguageVersion.CSharp8;
                Format = Nullable ? NullableFormat : SymbolDisplayFormat.FullyQualifiedFormat;
            }

            internal bool Nullable { get; }

            internal SymbolDisplayFormat Format { get; }

            // Every extension method declared in this assembly, by name, collected once per compilation.
            internal ILookup<string, IMethodSymbol> Extensions => _extensions ??= AllTypes(_compilation.Assembly.GlobalNamespace)
                .Where(t => t.IsStatic && t.MightContainExtensionMethods)
                .SelectMany(t => t.GetMembers().OfType<IMethodSymbol>().Where(m => m.IsExtensionMethod))
                .ToLookup(m => m.Name, StringComparer.Ordinal);

            internal void Run() {
                List<INamedTypeSymbol> types = FluentTypes();
                foreach (INamedTypeSymbol type in types) {
                    string? source = Generate(type, types);
                    if (source != null) {
                        string hint = type.ToDisplayString().Replace('<', '_').Replace('>', '_').Replace(',', '_').Replace(' ', '_');
                        _output.AddSource(hint + ".Fluent.g.cs", source);
                    }
                }
            }

            // Types marked [Fluent] and the concrete types deriving from a marked base or interface, then the settings
            // types reachable from their properties and from their Add/Set method parameters.
            private List<INamedTypeSymbol> FluentTypes() {
                IAssemblySymbol assembly = _compilation.Assembly;
                List<INamedTypeSymbol> roots = AllTypes(assembly.GlobalNamespace)
                    .Where(t => !HasNoFluent(t) && (IsDirectRoot(t) || IsCandidate(t) && DerivesFromMarked(t)))
                    .OrderBy(t => t.ToDisplayString(), StringComparer.Ordinal)
                    .ToList();
                HashSet<INamedTypeSymbol> seen = new HashSet<INamedTypeSymbol>(roots, SymbolEqualityComparer.Default);
                List<INamedTypeSymbol> result = new List<INamedTypeSymbol>(roots);
                List<INamedTypeSymbol> frontier = roots;
                while (frontier.Count > 0) {
                    List<INamedTypeSymbol> next = new List<INamedTypeSymbol>();
                    foreach (INamedTypeSymbol type in frontier) {
                        foreach (IPropertySymbol property in Properties(type)) {
                            foreach (ITypeSymbol? reached in new[] { property.Type, CollectionItem(property.Type) }) {
                                if (reached is INamedTypeSymbol annotated && annotated.OriginalDefinition is INamedTypeSymbol named && IsOptionType(named) && seen.Add(named)) {
                                    next.Add(named);
                                }
                            }
                        }
                    }
                    foreach (INamedTypeSymbol type in frontier) {
                        foreach (IMethodSymbol method in type.GetMembers().OfType<IMethodSymbol>().Where(m => m.MethodKind == MethodKind.Ordinary && !m.IsStatic && m.DeclaredAccessibility == Accessibility.Public
                            && (m.Name.StartsWith("Add", StringComparison.Ordinal) || m.Name.StartsWith("Set", StringComparison.Ordinal)))) {
                            foreach (IParameterSymbol parameter in method.Parameters) {
                                if (parameter.Type.OriginalDefinition is INamedTypeSymbol named && IsOptionType(named) && seen.Add(named)) {
                                    next.Add(named);
                                }
                            }
                        }
                    }
                    next.Sort((a, b) => string.CompareOrdinal(a.ToDisplayString(), b.ToDisplayString()));
                    result.AddRange(next);
                    frontier = next;
                }
                return result;
            }

            // A marked class gets methods itself, even when abstract (its methods are generic over TSelf).
            private static bool IsDirectRoot(INamedTypeSymbol type) =>
                type.TypeKind == TypeKind.Class && !type.IsStatic && (!type.IsGenericType || type.IsSealed)
                && type.DeclaredAccessibility == Accessibility.Public && type.ContainingType == null && HasFluent(type);

            private static bool DerivesFromMarked(INamedTypeSymbol type) {
                for (INamedTypeSymbol? current = type.BaseType; current != null; current = current.BaseType) {
                    if (HasFluent(current.OriginalDefinition)) {
                        return true;
                    }
                }
                return type.AllInterfaces.Any(contract => HasFluent(contract.OriginalDefinition));
            }

            // A public class of this assembly with something to set or add.
            private bool IsOptionType(INamedTypeSymbol type) =>
                SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, _compilation.Assembly) && IsCandidate(type)
                && Properties(type).Any(p => IsSettable(p) || CollectionItem(p.Type) != null);

            // Whether an extension method declared for receiver applies to type. An open TSelf receiver applies when the
            // type satisfies its constraints.
            private static bool Receives(ITypeSymbol receiver, INamedTypeSymbol type) {
                if (receiver is ITypeParameterSymbol parameter) {
                    return SatisfiesKind(parameter, type) && parameter.ConstraintTypes.All(constraint => Receives(constraint, type));
                }
                if (receiver is not INamedTypeSymbol named) {
                    return false;
                }
                // Type arguments must agree position by position. A receiver type parameter binds to one type, consistently
                // across its uses, and only when that type satisfies its constraints: Base<string, T> where T : struct
                // accepts Base<string, int> but neither Base<int, int> nor Base<string, string>.
                foreach (INamedTypeSymbol candidate in BaseTypes(type).Concat(type.AllInterfaces)) {
                    Dictionary<ITypeParameterSymbol, ITypeSymbol> bindings = new Dictionary<ITypeParameterSymbol, ITypeSymbol>(SymbolEqualityComparer.Default);
                    if (Unifies(named, candidate, bindings) && bindings.All(binding => Satisfies(binding.Key, binding.Value, bindings))) { return true; }
                }
                return false;
            }

            private static bool Unifies(ITypeSymbol pattern, ITypeSymbol actual, Dictionary<ITypeParameterSymbol, ITypeSymbol> bindings) {
                switch (pattern) {
                    case ITypeParameterSymbol parameter:
                        if (bindings.TryGetValue(parameter, out ITypeSymbol? bound)) { return SymbolEqualityComparer.Default.Equals(bound, actual); }
                        bindings[parameter] = actual;
                        return true;
                    case IArrayTypeSymbol array:
                        return actual is IArrayTypeSymbol other && array.Rank == other.Rank && Unifies(array.ElementType, other.ElementType, bindings);
                    case INamedTypeSymbol named when named.IsGenericType:
                        if (actual is not INamedTypeSymbol actualNamed || !SymbolEqualityComparer.Default.Equals(named.OriginalDefinition, actualNamed.OriginalDefinition)) { return false; }
                        for (int index = 0; index < named.TypeArguments.Length; index++) {
                            if (!Unifies(named.TypeArguments[index], actualNamed.TypeArguments[index], bindings)) { return false; }
                        }
                        return true;
                    default:
                        return SymbolEqualityComparer.Default.Equals(pattern, actual);
                }
            }

            // Whether a bound type meets a type parameter's constraints. A constraint naming another bound parameter
            // (where U : T) uses that binding; one still mentioning unbound parameters is accepted, so a match is never
            // missed, and the compiler checks the call.
            private static bool Satisfies(ITypeParameterSymbol parameter, ITypeSymbol type, Dictionary<ITypeParameterSymbol, ITypeSymbol> bindings) =>
                SatisfiesKind(parameter, type) && parameter.ConstraintTypes.All(constraint => {
                    ITypeSymbol resolved = constraint is ITypeParameterSymbol other && bindings.TryGetValue(other, out ITypeSymbol? bound) ? bound : constraint;
                    return ContainsTypeParameter(resolved) || Derives(type, resolved);
                });

            private static bool SatisfiesKind(ITypeParameterSymbol parameter, ITypeSymbol type) {
                if (parameter.HasReferenceTypeConstraint && !type.IsReferenceType) { return false; }
                if (parameter.HasValueTypeConstraint && (!type.IsValueType || type.OriginalDefinition.SpecialType == SpecialType.System_Nullable_T)) { return false; }
                if (parameter.HasUnmanagedTypeConstraint && !type.IsUnmanagedType) { return false; }
                return !parameter.HasConstructorConstraint || type.IsValueType || type is INamedTypeSymbol { IsAbstract: false } named
                    && named.InstanceConstructors.Any(c => c.Parameters.Length == 0 && c.DeclaredAccessibility == Accessibility.Public);
            }

            private static bool Derives(ITypeSymbol type, ITypeSymbol target) =>
                SymbolEqualityComparer.Default.Equals(type, target)
                || type is INamedTypeSymbol named && BaseTypes(named).Concat(named.AllInterfaces).Any(candidate => SymbolEqualityComparer.Default.Equals(candidate, target));

            private static bool ContainsTypeParameter(ITypeSymbol type) => type switch {
                ITypeParameterSymbol => true,
                IArrayTypeSymbol array => ContainsTypeParameter(array.ElementType),
                INamedTypeSymbol named => named.TypeArguments.Any(ContainsTypeParameter),
                _ => false
            };

            // [FluentForward] declarations on the type, its bases and its interfaces.
            private IEnumerable<(string Name, IMethodSymbol Target, string Summary, string ValueSummary)> Forwards(INamedTypeSymbol type) {
                IEnumerable<INamedTypeSymbol> declarers = BaseTypes(type).Concat(type.AllInterfaces).Select(t => t.OriginalDefinition);
                foreach (INamedTypeSymbol declarer in declarers) {
                    foreach (AttributeData attribute in declarer.GetAttributes().Where(a => a.AttributeClass?.ToDisplayString() == FluentAttributes.Forward)) {
                        if (attribute.ConstructorArguments.Length != 3 || attribute.ConstructorArguments[0].Value is not string name
                            || attribute.ConstructorArguments[1].Value is not INamedTypeSymbol target || attribute.ConstructorArguments[2].Value is not string method) {
                            continue;
                        }
                        string summary = attribute.NamedArguments.FirstOrDefault(a => a.Key == "Summary").Value.Value as string ?? "Sets " + name + ".";
                        string value = attribute.NamedArguments.FirstOrDefault(a => a.Key == "ValueSummary").Value.Value as string ?? "The value.";
                        List<IMethodSymbol> overloads = target.GetMembers(method).OfType<IMethodSymbol>()
                            .Where(m => m.IsStatic && !m.IsGenericMethod && m.Parameters.Length == 2 && m.Parameters.All(p => p.RefKind == RefKind.None)
                                && Receives(m.Parameters[0].Type, type) && _compilation.IsSymbolAccessibleWithin(m, _compilation.Assembly))
                            .ToList();
                        if (overloads.Count == 0) {
                            _output.ReportDiagnostic(Diagnostic.Create(FluentDiagnostics.ForwardMethodMissing, attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? Location.None, declarer.Name, target.Name, method, type.Name));
                        }
                        foreach (IMethodSymbol overload in overloads) {
                            yield return (name, overload, summary, value);
                        }
                    }
                }
            }

            private string? Generate(INamedTypeSymbol type, IReadOnlyList<INamedTypeSymbol> types) {
                Builder builder = new Builder(this, type, types);
                return builder.Build();
            }

            private sealed class Builder {
                private readonly Emitter _emitter;
                private readonly INamedTypeSymbol _type;
                private readonly IReadOnlyList<INamedTypeSymbol> _types;
                private readonly string _self;
                private readonly string _generic;
                private readonly string _constraint;
                private readonly string _receiver;
                private readonly string _fluentClass;
                private readonly List<(string Name, IMethodSymbol Target, string Summary, string ValueSummary)> _forwards;
                private readonly SymbolDisplayFormat TypeFormat;
                private readonly List<IMethodSymbol> _existing = new List<IMethodSymbol>();
                private readonly HashSet<string> _generated = new HashSet<string>(StringComparer.Ordinal);
                private readonly StringBuilder _body = new StringBuilder();
                private int _methods;

                // Parameter and local names the generated methods use; the receiver parameter must differ from all of them.
                private static readonly string[] Reserved = { "value", "item", "items", "configure" };

                internal Builder(Emitter emitter, INamedTypeSymbol type, IReadOnlyList<INamedTypeSymbol> types) {
                    _emitter = emitter;
                    _type = type;
                    _types = types;
                    _forwards = emitter.Forwards(type).ToList();
                    TypeFormat = emitter.Format;
                    string typeName = type.ToDisplayString(TypeFormat);
                    bool open = !type.IsSealed;
                    // A type that can be derived from gets generic methods (TSelf : Type), so a chain keeps the derived type.
                    _self = open ? "TSelf" : typeName;
                    _generic = open ? "<TSelf>" : type.TypeParameters.Length == 0 ? string.Empty : "<" + string.Join(", ", type.TypeParameters.Select(parameter => parameter.Name)) + ">";
                    _constraint = open ? " where TSelf : " + typeName : Constraints(type);
                    string receiver = Camel(type.Name);
                    string stem = receiver.TrimStart('@');
                    bool Clashes(string name) => Reserved.Contains(name) || _forwards.Any(forward => forward.Target.Parameters[1].Name == name);
                    for (int attempt = 0; Clashes(receiver.TrimStart('@')); attempt++) {
                        receiver = stem + "Instance" + (attempt == 0 ? string.Empty : attempt.ToString(System.Globalization.CultureInfo.InvariantCulture));
                    }
                    _receiver = receiver;
                    // Prefixed naming keeps ConsoleMonster's one partial class per namespace. The convention uses one class
                    // per type, so two derivable types with the same property never emit the same generic signature twice.
                    // Handwritten helpers can live in that partial class beside the generated ones.
                    string prefix = type.ContainingNamespace.IsGlobalNamespace ? "Global" : type.ContainingNamespace.Name;
                    _fluentClass = (emitter._settings.Prefixed ? prefix : type.Name) + "Fluent";
                    // Existing methods: instance methods a caller could reach and the fluent class's members (also from a
                    // referenced assembly). Extension methods of this assembly come from the emitter's index.
                    foreach (INamedTypeSymbol current in BaseTypes(type)) {
                        _existing.AddRange(current.GetMembers().OfType<IMethodSymbol>().Where(m => m.DeclaredAccessibility is Accessibility.Public or Accessibility.Internal or Accessibility.ProtectedOrInternal));
                    }
                    string metadataName = type.ContainingNamespace.IsGlobalNamespace ? _fluentClass : type.ContainingNamespace.ToDisplayString() + "." + _fluentClass;
                    INamedTypeSymbol? shared = emitter._compilation.GetTypeByMetadataName(metadataName);
                    if (shared != null) { _existing.AddRange(shared.GetMembers().OfType<IMethodSymbol>()); }
                }

                private bool Prefixed => _emitter._settings.Prefixed;

                private Compilation Compilation => _emitter._compilation;

                internal string? Build() {
                    if (Prefixed) {
                        AppendConfigure();
                    }
                    foreach ((string name, IMethodSymbol target, string summary, string valueSummary) in _forwards) {
                        IParameterSymbol value = target.Parameters[1];
                        if (!SyntaxFacts.IsValidIdentifier(name)) {
                            _emitter._output.ReportDiagnostic(Diagnostic.Create(FluentDiagnostics.InvalidName, target.Locations.FirstOrDefault() ?? Location.None, name, _type.Name, target.Name));
                            continue;
                        }
                        if (Prefixed ? Taken(name, value.Type) || !_generated.Add(name + "(" + value.Type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ")")
                            : !Claim(target.Locations.FirstOrDefault(), target.Name, name, value.Type)) { continue; }
                        AppendMethod(Doc(summary, valueSummary, value.Name), Escape(name), ", " + value.Type.ToDisplayString(TypeFormat) + " " + Escape(value.Name),
                            "            " + target.ContainingType.ToDisplayString(TypeFormat) + "." + Escape(target.Name) + "(" + _receiver + ", " + Escape(value.Name) + ");\n");
                    }
                    foreach (IPropertySymbol property in Properties(_type)) {
                        if (_forwards.Any(forward => forward.Name == property.Name)) { continue; }
                        // A property first declared by a generated base that can be derived from (overrides included) is covered
                        // by the base's generic methods; generating it again would make calls ambiguous. An abstract base's
                        // methods still get non-generic copies on sealed types, which win overload resolution.
                        IPropertySymbol origin = property;
                        while (origin.OverriddenProperty != null) { origin = origin.OverriddenProperty; }
                        INamedTypeSymbol owner = origin.ContainingType;
                        if (!SymbolEqualityComparer.Default.Equals(owner, _type) && !owner.IsSealed && _types.Contains(owner.OriginalDefinition, SymbolEqualityComparer.Default)
                            && (IsCandidate(owner) || !_type.IsSealed)) {
                            continue;
                        }
                        if (Prefixed) {
                            AppendPrefixed(property);
                        } else {
                            AppendConvention(property);
                        }
                    }
                    if (_methods == 0) {
                        return null;
                    }
                    string docId = _type.GetDocumentationCommentId() ?? _type.Name;
                    string adds = Prefixed ? "<c>Add...</c> adds to a collection" : "a singular-named method adds to a collection";
                    bool global = _type.ContainingNamespace.IsGlobalNamespace;
                    StringBuilder source = new StringBuilder();
                    source.Append("// <auto-generated>\n// Generated by PowerForge.Generators from the public properties of ")
                        .Append(_type.ToDisplayString()).Append(".\n// Do not edit: change the properties instead.\n// </auto-generated>\n")
                        .Append(_emitter.Nullable ? "#nullable enable\n\n" : "\n");
                    if (!global) {
                        source.Append("namespace ").Append(_type.ContainingNamespace.ToDisplayString()).Append(" {\n");
                    }
                    source.Append("    /// <summary>The fluent methods of <see cref=\"").Append(docId).Append("\"/>, generated from its public properties: A property-named method sets a property and ")
                        .Append(adds).Append(", both returning the same instance.</summary>\n")
                        .Append("    public static partial class ").Append(_fluentClass).Append(" {\n")
                        .Append(_body.ToString().TrimEnd('\n')).Append('\n')
                        .Append("    }\n");
                    if (!global) {
                        source.Append("}\n");
                    }
                    return source.ToString();
                }

                // Prefixed naming: one Configure(Action<T>) assigns every delegate-valued property.
                private void AppendConfigure() {
                    bool delegates = Properties(_type).Any(property => IsSettable(property) && property.Type.TypeKind == TypeKind.Delegate);
                    for (INamedTypeSymbol? parent = _type.BaseType; parent != null; parent = parent.BaseType) {
                        if (_types.Contains(parent, SymbolEqualityComparer.Default) && Properties(parent).Any(property => IsSettable(property) && property.Type.TypeKind == TypeKind.Delegate)) { return; }
                    }
                    if (!delegates || Taken("Configure", Action(_type))) {
                        return;
                    }
                    StringBuilder doc = new StringBuilder(Doc("Assigns callbacks and related configuration without invoking delegate-valued properties.", "Assignments applied in order.", "configure"));
                    foreach (IPropertySymbol property in Properties(_type).Where(property => IsSettable(property) && property.Type.TypeKind == TypeKind.Delegate)) {
                        doc.Append(Example(property));
                    }
                    _body.Append(doc).Append("        public static ").Append(_self).Append(" Configure").Append(_generic).Append("(this ").Append(_self).Append(' ').Append(_receiver)
                        .Append(", global::System.Action<").Append(_self).Append("> configure)").Append(_constraint).Append(" {\n")
                        .Append(NullCheck("configure"))
                        .Append("            configure(").Append(_receiver).Append(");\n            return ").Append(_receiver).Append(";\n        }\n\n");
                    _methods++;
                }

                private void AppendPrefixed(IPropertySymbol property) {
                    // C# invokes a delegate property before considering an extension with the same name.
                    if (property.Type.TypeKind == TypeKind.Delegate) { return; }
                    string propertyType = property.Type.ToDisplayString(TypeFormat);
                    string cref = Cref(property);
                    string summary = Summary(property);
                    string with = MethodName(property);
                    ITypeSymbol? item = IsReadable(property) ? CollectionItem(property.Type) : null;
                    if (IsSettable(property) && !Taken(with, property.Type) && _generated.Add(with + "(" + propertyType + ")")) {
                        AppendMethod(Doc("Sets " + cref + summary, "The value.", "value") + Example(property), with, ", " + propertyType + " value", Assign(property, "value"));
                        if (property.Type.SpecialType == SpecialType.System_Boolean && !Taken(with) && _generated.Add(with + "()")) {
                            AppendMethod("        /// <summary>Sets " + cref + " to true.</summary>\n"
                                + "        /// <param name=\"" + _receiver.TrimStart('@') + "\">The instance to configure.</param>\n"
                                + "        /// <returns>The same instance.</returns>\n", with, string.Empty, Assign(property, "true"));
                        }
                    }
                    if (property.Type is INamedTypeSymbol option && item == null && IsReadable(property) && _emitter.IsOptionType(option) && !Taken(with, Action(option)) && _generated.Add(with + "(Action)")) {
                        AppendOption(property, option, with, cref, summary);
                    }
                    if (item != null) {
                        string itemType = item.ToDisplayString(TypeFormat);
                        string add = "Add" + Singular(property);
                        string addAll = "Add" + property.Name;
                        if (!Taken(add, item) && _generated.Add(add + "(" + itemType + ")")) {
                            AppendMethod(Doc("Adds an item to " + cref + summary, "The item.", "item"), add, ", " + itemType + " item", Ensure(property, itemType) + Add(property, item, "item"));
                        }
                        if (!Taken(addAll, Compilation.CreateArrayTypeSymbol(item)) && _generated.Add(addAll + "(params " + itemType + ")")) {
                            AppendMethod(Doc("Adds items to " + cref + summary, "The items, in order.", "items"), addAll, ", params " + itemType + "[] items",
                                NullCheck("items") + Ensure(property, itemType) + "            foreach (var item in items) { " + AddExpression(property, item, "item") + "; }\n");
                        }
                        AppendAddRange(property, item, addAll, cref, summary, conflictIsError: false);
                    }
                }

                // The shared authoring convention: property-named setters, singular-named children, declared delegate verbs.
                private void AppendConvention(IPropertySymbol property) {
                    string propertyType = property.Type.ToDisplayString(TypeFormat);
                    string cref = Cref(property);
                    string summary = Summary(property);
                    if (property.Type.TypeKind == TypeKind.Delegate) {
                        if (!IsSettable(property)) { return; }
                        string? verb = NamedArgument(Attribute(property, FluentAttributes.Fluent));
                        if (verb == null) {
                            Report(FluentDiagnostics.DelegateWithoutVerb, property, _type.Name, property.Name);
                            return;
                        }
                        if (ValidName(property, verb) && Claim(property, verb, property.Type)) {
                            AppendMethod(Doc("Sets " + cref + summary, "The callback.", "value") + Example(property), verb, ", " + propertyType + " value", Assign(property, "value"));
                        }
                        return;
                    }
                    string name = MethodName(property);
                    if (!ValidName(property, name)) { return; }
                    ITypeSymbol? item = CollectionItem(property.Type);
                    if (item != null) {
                        if (!IsReadable(property)) { return; }
                        string itemType = item.ToDisplayString(TypeFormat);
                        string singular = NamedArgument(Attribute(property, FluentAttributes.Child)) ?? Singular(property);
                        if (!ValidName(property, singular)) { return; }
                        if (Claim(property, singular, item)) {
                            AppendMethod(Doc("Adds an item to " + cref + summary, "The item.", "item"), singular, ", " + itemType + " item", Ensure(property, itemType) + Add(property, item, "item"));
                        }
                        if (item is INamedTypeSymbol created && Creatable(created) && Claim(property, singular, Action(created))) {
                            string createdType = created.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString(TypeFormat);
                            AppendMethod(Doc("Creates an item, configures it and adds it to " + cref + summary, "Configures the new item before it is added.", "configure"), singular,
                                ", global::System.Action<" + itemType + "> configure",
                                NullCheck("configure") + "            " + itemType + " item = new " + createdType + "();\n            configure(item);\n" + Ensure(property, itemType) + Add(property, item, "item"));
                        }
                        AppendAddRange(property, item, name, cref, summary, conflictIsError: true);
                        return;
                    }
                    if (IsSettable(property)) {
                        if (property.Type.SpecialType == SpecialType.System_Boolean) {
                            if (Claim(property, name, property.Type)) {
                                AppendMethod(Doc("Sets " + cref + summary, "The value; true when omitted.", "value") + Example(property), name, ", " + propertyType + " value = true", Assign(property, "value"));
                            }
                        } else if (Claim(property, name, property.Type)) {
                            AppendMethod(Doc("Sets " + cref + summary, "The value.", "value") + Example(property), name, ", " + propertyType + " value", Assign(property, "value"));
                        }
                    }
                    if (property.Type is INamedTypeSymbol option && IsReadable(property) && _emitter.IsOptionType(option) && Claim(property, name, Action(option))) {
                        AppendOption(property, option, name, cref, summary);
                    }
                }

                private void AppendAddRange(IPropertySymbol property, ITypeSymbol item, string name, string cref, string summary, bool conflictIsError) {
                    INamedTypeSymbol enumerable = Compilation.GetTypeByMetadataName("System.Collections.Generic.IEnumerable`1")!.Construct(item);
                    bool free = conflictIsError ? Claim(property, name, enumerable) : !Taken(name, enumerable) && _generated.Add(name + "(IEnumerable<" + item.ToDisplayString(TypeFormat) + ">)");
                    if (!free) { return; }
                    string itemType = item.ToDisplayString(TypeFormat);
                    AppendMethod(Doc("Adds items to " + cref + summary, "The items, in order.", "items"), name, ", global::System.Collections.Generic.IEnumerable<" + itemType + "> items",
                        NullCheck("items") + Ensure(property, itemType)
                        + "            foreach (" + itemType + " item in items) {\n                " + AddExpression(property, item, "item") + ";\n            }\n");
                }

                // X(Action<T>): configures the settings object in place, creating it first when it is not set and can be.
                private void AppendOption(IPropertySymbol property, INamedTypeSymbol option, string name, string cref, string summary) {
                    string optionType = option.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString(TypeFormat);
                    bool canCreate = IsSettable(property) && Creatable(option);
                    string statements = NullCheck("configure")
                        + "            " + optionType + " value = " + Member(property) + " ?? "
                        + (canCreate ? "new " + optionType + "()" : "throw new global::System.InvalidOperationException(\"" + property.Name + " is not set.\")") + ";\n"
                        + "            configure(value);\n"
                        + (IsSettable(property) ? Assign(property, "value") : string.Empty);
                    AppendMethod(Doc("Configures " + cref + " in place" + (canCreate ? " (creating it when not set)" : string.Empty) + summary, "Configures the value.", "configure"),
                        name, ", global::System.Action<" + optionType + "> configure", statements);
                }

                private void AppendMethod(string doc, string name, string parameters, string statements) {
                    _body.Append(doc).Append("        public static ").Append(_self).Append(' ').Append(Escape(name)).Append(_generic).Append("(this ").Append(_self).Append(' ').Append(_receiver)
                        .Append(parameters).Append(')').Append(_constraint).Append(" {\n")
                        .Append(statements)
                        .Append("            return ").Append(_receiver).Append(";\n        }\n\n");
                    _methods++;
                }

                private bool ValidName(IPropertySymbol property, string name) {
                    if (SyntaxFacts.IsValidIdentifier(name)) { return true; }
                    Report(FluentDiagnostics.InvalidName, property, name, _type.Name, property.Name);
                    return false;
                }

                private bool Claim(IPropertySymbol property, string name, ITypeSymbol parameter) => Claim(property.Locations.FirstOrDefault(), property.Name, name, parameter);

                // Convention mode: a name taken by an existing or already generated method, or one a delegate-valued member
                // of the type would capture (x.Name(...) invokes the delegate), is a build error.
                private bool Claim(Location? location, string member, string name, ITypeSymbol parameter) {
                    bool captured = BaseTypes(_type).SelectMany(t => t.GetMembers(name)).Any(m => m.DeclaredAccessibility != Accessibility.Private
                        && (m is IPropertySymbol { Type.TypeKind: TypeKind.Delegate } || m is IFieldSymbol { Type.TypeKind: TypeKind.Delegate } || m is IEventSymbol));
                    if (captured || Taken(name, parameter)) {
                        _emitter._output.ReportDiagnostic(Diagnostic.Create(FluentDiagnostics.Collision, location ?? Location.None, name, _type.Name, member));
                        return false;
                    }
                    // Nullable annotations do not distinguish C# signatures.
                    if (!_generated.Add(name + "(" + parameter.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat) + ")")) {
                        _emitter._output.ReportDiagnostic(Diagnostic.Create(FluentDiagnostics.Duplicate, location ?? Location.None, name, _type.Name, member));
                        return false;
                    }
                    return true;
                }

                private void Report(DiagnosticDescriptor descriptor, IPropertySymbol property, params object[] arguments) =>
                    _emitter._output.ReportDiagnostic(Diagnostic.Create(descriptor, property.Locations.FirstOrDefault() ?? Location.None, arguments));

                // Compares complete value-parameter signatures. A handwritten extension for another receiver leaves the name free.
                private bool Taken(string name, params ITypeSymbol[] parameters) => _existing.Where(m => m.Name == name).Concat(_emitter.Extensions[name]).Any(m => {
                    int offset = m.IsExtensionMethod ? 1 : 0;
                    if (m.IsExtensionMethod && m.Parameters.Length > 0 && !Receives(m.Parameters[0].Type, _type)) { return false; }
                    return m.Name == name && m.Parameters.Length == parameters.Length + offset
                        && parameters.Select((parameter, index) => SymbolEqualityComparer.Default.Equals(parameter, m.Parameters[index + offset].Type)).All(equal => equal);
                });

                private INamedTypeSymbol Action(ITypeSymbol argument) => Compilation.GetTypeByMetadataName("System.Action`1")!.Construct(argument);

                private string Member(IPropertySymbol property) => _receiver + "." + Escape(property.Name);

                private string Assign(IPropertySymbol property, string value) => "            " + Member(property) + " = " + value + ";\n";

                private string Add(IPropertySymbol property, ITypeSymbol item, string value) => "            " + AddExpression(property, item, value) + ";\n";

                // Calls Add through ICollection<T> when the collection type implements it explicitly.
                private string AddExpression(IPropertySymbol property, ITypeSymbol item, string value) {
                    bool direct = property.Type.TypeKind == TypeKind.Interface || property.Type is INamedTypeSymbol named && BaseTypes(named).SelectMany(t => t.GetMembers("Add")).OfType<IMethodSymbol>()
                        .Any(m => !m.IsStatic && m.DeclaredAccessibility == Accessibility.Public && m.Parameters.Length == 1 && SymbolEqualityComparer.Default.Equals(m.Parameters[0].Type, item));
                    string target = direct ? Member(property) : "((global::System.Collections.Generic.ICollection<" + item.ToDisplayString(TypeFormat) + ">)" + Member(property) + ")";
                    return target + ".Add(" + value + ")";
                }

                private static string NullCheck(string parameter) => "            if (" + parameter + " == null) { throw new global::System.ArgumentNullException(nameof(" + parameter + ")); }\n";

                // A nullable collection property is created on the first Add when a List<T> fits it; otherwise Add needs it set.
                private string Ensure(IPropertySymbol property, string itemType) {
                    if (property.Type.NullableAnnotation != NullableAnnotation.Annotated) {
                        return string.Empty;
                    }
                    string definition = property.Type.OriginalDefinition.ToDisplayString();
                    bool fits = definition == "System.Collections.Generic.IList<T>" || definition == "System.Collections.Generic.ICollection<T>" || definition == "System.Collections.Generic.List<T>";
                    if (IsSettable(property) && fits) {
                        string create = "new global::System.Collections.Generic.List<" + itemType + ">()";
                        return _emitter.Nullable ? "            " + Member(property) + " ??= " + create + ";\n"
                            : "            if (" + Member(property) + " == null) { " + Member(property) + " = " + create + "; }\n";
                    }
                    return "            if (" + Member(property) + " == null) { throw new global::System.InvalidOperationException(\"" + property.Name + " is not set.\"); }\n";
                }

                // The type's own constraints, repeated on methods of a sealed generic model.
                private string Constraints(INamedTypeSymbol type) {
                    StringBuilder text = new StringBuilder();
                    foreach (ITypeParameterSymbol parameter in type.TypeParameters) {
                        List<string> parts = new List<string>();
                        if (parameter.HasReferenceTypeConstraint) { parts.Add(_emitter.Nullable && parameter.ReferenceTypeConstraintNullableAnnotation == NullableAnnotation.Annotated ? "class?" : "class"); }
                        if (parameter.HasUnmanagedTypeConstraint) { parts.Add("unmanaged"); } else if (parameter.HasValueTypeConstraint) { parts.Add("struct"); }
                        if (parameter.HasNotNullConstraint) { parts.Add("notnull"); }
                        parts.AddRange(parameter.ConstraintTypes.Select(constraint => constraint.ToDisplayString(TypeFormat)));
                        if (parameter.HasConstructorConstraint) { parts.Add("new()"); }
                        if (parts.Count > 0) { text.Append(" where ").Append(parameter.Name).Append(" : ").Append(string.Join(", ", parts)); }
                    }
                    return text.ToString();
                }

                private string Doc(string summary, string parameter, string parameterName) =>
                    "        /// <summary>" + summary + "</summary>\n"
                    + "        /// <param name=\"" + _receiver.TrimStart('@') + "\">The instance.</param>\n"
                    + "        /// <param name=\"" + parameterName + "\">" + parameter + "</param>\n"
                    + "        /// <returns>The same instance.</returns>\n";

                /// <summary>Shows both configuration forms in the generated API reference, without assuming a parameterless constructor.</summary>
                private string Example(IPropertySymbol property) {
                    bool constructible = _type.InstanceConstructors.Any(ctor => ctor.DeclaredAccessibility == Accessibility.Public && ctor.Parameters.All(parameter => parameter.IsOptional));
                    string initializer = constructible ? "new " + _type.ToDisplayString(TypeFormat) + " { " + property.Name + " = value }" : _receiver + "." + property.Name + " = value;";
                    IMethodSymbol? factory = _emitter._factories.SelectMany(holder => holder.GetMembers().OfType<IMethodSymbol>()).FirstOrDefault(method => method.IsStatic && !method.IsGenericMethod
                        && method.Parameters.All(parameter => parameter.IsOptional) && SymbolEqualityComparer.Default.Equals(method.ReturnType, _type));
                    string entry = factory == null ? _receiver : factory.ContainingType.Name + "." + factory.Name + "()";
                    string call;
                    if (property.Type.TypeKind == TypeKind.Delegate) {
                        call = Prefixed ? ".Configure(configured => configured." + property.Name + " = value)" : "." + NamedArgument(Attribute(property, FluentAttributes.Fluent)) + "(value)";
                    } else {
                        call = "." + MethodName(property) + "(value)";
                    }
                    if (!initializer.EndsWith(";", StringComparison.Ordinal)) { initializer += ";"; }
                    const string leftHeading = "// C# initializer or property";
                    int column = Math.Max(leftHeading.Length, initializer.Length) + 4;
                    return "        /// <example><code>\n"
                        + "        /// " + leftHeading.PadRight(column) + "// C# fluent\n"
                        + "        /// " + new XText(initializer.PadRight(column) + entry + call + ";") + "\n"
                        + "        /// </code></example>\n";
                }

                private static string Cref(IPropertySymbol property) => "<see cref=\"" + (property.GetDocumentationCommentId() ?? property.Name) + "\"/>";

                private static string Camel(string name) {
                    string camel = char.ToLowerInvariant(name[0]) + name.Substring(1);
                    return SyntaxFacts.GetKeywordKind(camel) != SyntaxKind.None || SyntaxFacts.GetContextualKeywordKind(camel) != SyntaxKind.None ? "@" + camel : camel;
                }
            }

        }
    }
}
