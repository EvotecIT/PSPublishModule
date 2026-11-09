using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

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
            private static readonly SymbolDisplayFormat TypeFormat = SymbolDisplayFormat.FullyQualifiedFormat
                .AddMiscellaneousOptions(SymbolDisplayMiscellaneousOptions.IncludeNullableReferenceTypeModifier);

            private readonly Compilation _compilation;
            private readonly FluentSettings _settings;
            private readonly SourceProductionContext _output;
            private readonly INamedTypeSymbol? _factory;

            internal Emitter(Compilation compilation, FluentSettings settings, SourceProductionContext output) {
                _compilation = compilation;
                _settings = settings;
                _output = output;
                _factory = settings.Factory == null ? null : compilation.GetTypeByMetadataName(settings.Factory);
            }

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

            private static IEnumerable<INamedTypeSymbol> AllTypes(INamespaceSymbol ns) {
                foreach (INamespaceOrTypeSymbol member in ns.GetMembers()) {
                    if (member is INamespaceSymbol child) {
                        foreach (INamedTypeSymbol type in AllTypes(child)) {
                            yield return type;
                        }
                    } else if (member is INamedTypeSymbol type) {
                        yield return type;
                    }
                }
            }

            private static bool IsCandidate(INamedTypeSymbol type) =>
                type.TypeKind == TypeKind.Class && !type.IsAbstract && !type.IsStatic && (!type.IsGenericType || type.IsSealed)
                && type.DeclaredAccessibility == Accessibility.Public && type.ContainingType == null && !HasNoFluent(type);

            // A public class of this assembly with something to set or add.
            private bool IsOptionType(INamedTypeSymbol type) =>
                SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, _compilation.Assembly) && IsCandidate(type)
                && Properties(type).Any(p => IsSettable(p) || CollectionItem(p.Type) != null);

            /// <summary>Public instance properties, declared or inherited, without indexers, obsolete or [NoFluent] members.</summary>
            private static IEnumerable<IPropertySymbol> Properties(INamedTypeSymbol type) {
                HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
                for (INamedTypeSymbol? current = type; current != null && current.SpecialType != SpecialType.System_Object; current = current.BaseType) {
                    foreach (IPropertySymbol property in current.GetMembers().OfType<IPropertySymbol>()) {
                        if (property.IsStatic || property.IsIndexer || property.DeclaredAccessibility != Accessibility.Public || HasNoFluent(property)
                            || property.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == "System.ObsoleteAttribute") || !names.Add(property.Name)) {
                            continue;
                        }
                        yield return property;
                    }
                }
            }

            private static bool IsSettable(IPropertySymbol property) =>
                property.SetMethod != null && property.SetMethod.DeclaredAccessibility == Accessibility.Public && !property.SetMethod.IsInitOnly;

            /// <summary>The item type of a mutable collection (ICollection&lt;T&gt;, not a dictionary, array or string), or null.</summary>
            private static ITypeSymbol? CollectionItem(ITypeSymbol type) {
                if (type.SpecialType == SpecialType.System_String || type is IArrayTypeSymbol) {
                    return null;
                }
                IEnumerable<INamedTypeSymbol> interfaces = type is INamedTypeSymbol named && named.TypeKind == TypeKind.Interface ? new[] { named }.Concat(type.AllInterfaces) : type.AllInterfaces;
                INamedTypeSymbol? collection = null;
                foreach (INamedTypeSymbol candidate in interfaces) {
                    string definition = candidate.OriginalDefinition.ToDisplayString();
                    if (definition == "System.Collections.Generic.IDictionary<TKey, TValue>") {
                        return null;
                    }
                    if (definition == "System.Collections.Generic.ICollection<T>") {
                        collection = candidate;
                    }
                }
                return collection?.TypeArguments[0];
            }

            // Whether an extension method declared for receiver applies to type. An open TSelf receiver applies when the
            // type satisfies its constraints.
            private static bool Receives(ITypeSymbol receiver, INamedTypeSymbol type) {
                if (receiver is ITypeParameterSymbol parameter) {
                    return parameter.ConstraintTypes.All(constraint => Receives(constraint, type));
                }
                if (receiver is not INamedTypeSymbol named) {
                    return false;
                }
                for (INamedTypeSymbol? current = type; current != null; current = current.BaseType) {
                    if (SymbolEqualityComparer.Default.Equals(current.OriginalDefinition, named.OriginalDefinition)) { return true; }
                }
                return type.AllInterfaces.Any(contract => SymbolEqualityComparer.Default.Equals(contract.OriginalDefinition, named.OriginalDefinition));
            }

            private static AttributeData? Attribute(ISymbol symbol, string name) => symbol.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == name);

            private static bool HasFluent(ISymbol symbol) => Attribute(symbol, FluentAttributes.Fluent) != null;

            private static bool HasNoFluent(ISymbol symbol) => Attribute(symbol, FluentAttributes.NoFluent) != null;

            private static string? NamedArgument(AttributeData? attribute) =>
                attribute != null && attribute.ConstructorArguments.Length == 1 ? attribute.ConstructorArguments[0].Value as string : null;

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
                            .Where(m => m.IsStatic && m.Parameters.Length == 2 && Receives(m.Parameters[0].Type, type) && _compilation.IsSymbolAccessibleWithin(m, _compilation.Assembly))
                            .ToList();
                        if (overloads.Count == 0) {
                            _output.ReportDiagnostic(Diagnostic.Create(FluentDiagnostics.ForwardMethodMissing, attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? Location.None, declarer.Name, target.Name, method));
                        }
                        foreach (IMethodSymbol overload in overloads) {
                            yield return (name, overload, summary, value);
                        }
                    }
                }
            }

            private static IEnumerable<INamedTypeSymbol> BaseTypes(INamedTypeSymbol type) {
                for (INamedTypeSymbol? current = type; current != null; current = current.BaseType) {
                    yield return current;
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
                private readonly List<IMethodSymbol> _existing = new List<IMethodSymbol>();
                private readonly HashSet<string> _generated = new HashSet<string>(StringComparer.Ordinal);
                private readonly StringBuilder _body = new StringBuilder();
                private int _methods;

                internal Builder(Emitter emitter, INamedTypeSymbol type, IReadOnlyList<INamedTypeSymbol> types) {
                    _emitter = emitter;
                    _type = type;
                    _types = types;
                    string typeName = type.ToDisplayString(TypeFormat);
                    bool open = !type.IsSealed;
                    // A type that can be derived from gets generic methods (TSelf : Type), so a chain keeps the derived type.
                    _self = open ? "TSelf" : typeName;
                    _generic = open ? "<TSelf>" : type.TypeParameters.Length == 0 ? string.Empty : "<" + string.Join(", ", type.TypeParameters.Select(parameter => parameter.Name)) + ">";
                    _constraint = open ? " where TSelf : " + typeName : string.Empty;
                    _receiver = Camel(type.Name);
                    // All fluent extensions in a namespace share one public partial class, so handwritten helpers can live
                    // beside the generated ones. Existing methods include inherited instance methods and every extension
                    // method of this assembly (and of the namespace's fluent class elsewhere) that applies to the type.
                    _fluentClass = type.ContainingNamespace.Name + "Fluent";
                    foreach (INamedTypeSymbol current in BaseTypes(type)) { _existing.AddRange(current.GetMembers().OfType<IMethodSymbol>()); }
                    INamedTypeSymbol? shared = emitter._compilation.GetTypeByMetadataName(type.ContainingNamespace.ToDisplayString() + "." + _fluentClass);
                    if (shared != null) { _existing.AddRange(shared.GetMembers().OfType<IMethodSymbol>()); }
                    foreach (INamedTypeSymbol holder in AllTypes(emitter._compilation.Assembly.GlobalNamespace).Where(t => t.IsStatic && t.MightContainExtensionMethods && !SymbolEqualityComparer.Default.Equals(t, shared))) {
                        _existing.AddRange(holder.GetMembers().OfType<IMethodSymbol>().Where(m => m.IsExtensionMethod));
                    }
                }

                private bool Prefixed => _emitter._settings.Prefixed;

                private Compilation Compilation => _emitter._compilation;

                internal string? Build() {
                    List<(string Name, IMethodSymbol Target, string Summary, string ValueSummary)> forwards = _emitter.Forwards(_type).ToList();
                    if (Prefixed) {
                        AppendConfigure();
                    }
                    foreach ((string name, IMethodSymbol target, string summary, string valueSummary) in forwards) {
                        IParameterSymbol value = target.Parameters[1];
                        AppendMethod(Doc(summary, valueSummary, value.Name), name, ", " + value.Type.ToDisplayString(TypeFormat) + " " + Escape(value.Name),
                            "            " + target.ContainingType.ToDisplayString(TypeFormat) + "." + target.Name + "(" + _receiver + ", " + Escape(value.Name) + ");\n");
                    }
                    foreach (IPropertySymbol property in Properties(_type)) {
                        if (forwards.Any(forward => forward.Name == property.Name)) { continue; }
                        // An inherited property of a base that can be derived from is covered by the base's generic methods;
                        // generating it again would make every call ambiguous.
                        if (!SymbolEqualityComparer.Default.Equals(property.ContainingType, _type) && !property.ContainingType.IsSealed && IsCandidate(property.ContainingType)
                            && SymbolEqualityComparer.Default.Equals(property.ContainingType.ContainingAssembly, Compilation.Assembly)) {
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
                    StringBuilder source = new StringBuilder();
                    source.Append("// <auto-generated>\n// Generated by PowerForge.Generators from the public properties of ")
                        .Append(_type.ToDisplayString()).Append(".\n// Do not edit: change the properties instead.\n// </auto-generated>\n")
                        .Append("#nullable enable\n\n")
                        .Append("namespace ").Append(_type.ContainingNamespace.ToDisplayString()).Append(" {\n")
                        .Append("    /// <summary>The fluent methods of <see cref=\"").Append(docId).Append("\"/>, generated from its public properties: A property-named method sets a property and ")
                        .Append(adds).Append(", both returning the same instance.</summary>\n")
                        .Append("    public static partial class ").Append(_fluentClass).Append(" {\n")
                        .Append(_body.ToString().TrimEnd('\n')).Append('\n')
                        .Append("    }\n}\n");
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
                    ITypeSymbol? item = CollectionItem(property.Type);
                    if (IsSettable(property) && !Taken(with, property.Type) && _generated.Add(with + "(" + propertyType + ")")) {
                        AppendMethod(Doc("Sets " + cref + summary, "The value.", "value") + Example(property), with, ", " + propertyType + " value", Assign(property, "value"));
                        if (property.Type.SpecialType == SpecialType.System_Boolean && !Taken(with) && _generated.Add(with + "()")) {
                            AppendMethod("        /// <summary>Sets " + cref + " to true.</summary>\n"
                                + "        /// <param name=\"" + _receiver.TrimStart('@') + "\">The instance to configure.</param>\n"
                                + "        /// <returns>The same instance.</returns>\n", with, string.Empty, Assign(property, "true"));
                        }
                    }
                    if (property.Type is INamedTypeSymbol option && item == null && _emitter.IsOptionType(option) && !Taken(with, Action(option)) && _generated.Add(with + "(Action)")) {
                        AppendOption(property, option, with, cref, summary);
                    }
                    if (item != null) {
                        string itemType = item.ToDisplayString(TypeFormat);
                        string add = "Add" + Singular(property);
                        string addAll = "Add" + property.Name;
                        if (!Taken(add, item) && _generated.Add(add + "(" + itemType + ")")) {
                            AppendMethod(Doc("Adds an item to " + cref + summary, "The item.", "item"), add, ", " + itemType + " item", Ensure(property, itemType) + Add(property, "item"));
                        }
                        if (!Taken(addAll, Compilation.CreateArrayTypeSymbol(item)) && _generated.Add(addAll + "(params " + itemType + ")")) {
                            AppendMethod(Doc("Adds items to " + cref + summary, "The items, in order.", "items"), addAll, ", params " + itemType + "[] items",
                                NullCheck("items") + Ensure(property, itemType) + "            foreach (var item in items) { " + _receiver + "." + property.Name + ".Add(item); }\n");
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
                        if (Claim(property, verb, property.Type)) {
                            AppendMethod(Doc("Sets " + cref + summary, "The callback.", "value") + Example(property), verb, ", " + propertyType + " value", Assign(property, "value"));
                        }
                        return;
                    }
                    string name = MethodName(property);
                    ITypeSymbol? item = CollectionItem(property.Type);
                    if (item != null) {
                        string itemType = item.ToDisplayString(TypeFormat);
                        string singular = NamedArgument(Attribute(property, FluentAttributes.Child)) ?? Singular(property);
                        if (Claim(property, singular, item)) {
                            AppendMethod(Doc("Adds an item to " + cref + summary, "The item.", "item"), singular, ", " + itemType + " item", Ensure(property, itemType) + Add(property, "item"));
                        }
                        if (item is INamedTypeSymbol created && Creatable(created) && Claim(property, singular, Action(created))) {
                            AppendMethod(Doc("Creates an item, configures it and adds it to " + cref + summary, "Configures the new item before it is added.", "configure"), singular,
                                ", global::System.Action<" + itemType + "> configure",
                                NullCheck("configure") + "            " + itemType + " item = new " + itemType + "();\n            configure(item);\n" + Ensure(property, itemType) + Add(property, "item"));
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
                    if (property.Type is INamedTypeSymbol option && _emitter.IsOptionType(option) && Claim(property, name, Action(option))) {
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
                        + "            foreach (" + itemType + " item in items) {\n                " + _receiver + "." + property.Name + ".Add(item);\n            }\n");
                }

                // X(Action<T>): configures the settings object in place, creating it first when it is not set and can be.
                private void AppendOption(IPropertySymbol property, INamedTypeSymbol option, string name, string cref, string summary) {
                    string optionType = option.WithNullableAnnotation(NullableAnnotation.NotAnnotated).ToDisplayString(TypeFormat);
                    bool canCreate = IsSettable(property) && Creatable(option);
                    string statements = NullCheck("configure")
                        + "            " + optionType + " value = " + _receiver + "." + property.Name + " ?? "
                        + (canCreate ? "new " + optionType + "()" : "throw new global::System.InvalidOperationException(\"" + property.Name + " is not set.\")") + ";\n"
                        + "            configure(value);\n"
                        + (IsSettable(property) ? Assign(property, "value") : string.Empty);
                    AppendMethod(Doc("Configures " + cref + " in place" + (canCreate ? " (creating it when not set)" : string.Empty) + summary, "Configures the value.", "configure"),
                        name, ", global::System.Action<" + optionType + "> configure", statements);
                }

                private void AppendMethod(string doc, string name, string parameters, string statements) {
                    _body.Append(doc).Append("        public static ").Append(_self).Append(' ').Append(name).Append(_generic).Append("(this ").Append(_self).Append(' ').Append(_receiver)
                        .Append(parameters).Append(')').Append(_constraint).Append(" {\n")
                        .Append(statements)
                        .Append("            return ").Append(_receiver).Append(";\n        }\n\n");
                    _methods++;
                }

                // Convention mode: a name taken by an existing or already generated method is a build error.
                private bool Claim(IPropertySymbol property, string name, ITypeSymbol parameter) {
                    if (Taken(name, parameter)) {
                        Report(FluentDiagnostics.Collision, property, name, _type.Name, property.Name);
                        return false;
                    }
                    if (!_generated.Add(name + "(" + parameter.ToDisplayString(TypeFormat) + ")")) {
                        Report(FluentDiagnostics.Duplicate, property, name, _type.Name, property.Name);
                        return false;
                    }
                    return true;
                }

                private void Report(DiagnosticDescriptor descriptor, IPropertySymbol property, params object[] arguments) =>
                    _emitter._output.ReportDiagnostic(Diagnostic.Create(descriptor, property.Locations.FirstOrDefault() ?? Location.None, arguments));

                // Compares complete value-parameter signatures. A handwritten extension for another receiver leaves the name free.
                private bool Taken(string name, params ITypeSymbol[] parameters) => _existing.Any(m => {
                    int offset = m.IsExtensionMethod ? 1 : 0;
                    if (m.IsExtensionMethod && m.Parameters.Length > 0 && !Receives(m.Parameters[0].Type, _type)) { return false; }
                    return m.Name == name && m.Parameters.Length == parameters.Length + offset
                        && parameters.Select((parameter, index) => SymbolEqualityComparer.Default.Equals(parameter, m.Parameters[index + offset].Type)).All(equal => equal);
                });

                private INamedTypeSymbol Action(ITypeSymbol argument) => Compilation.GetTypeByMetadataName("System.Action`1")!.Construct(argument);

                private static bool Creatable(INamedTypeSymbol type) =>
                    type.TypeKind == TypeKind.Class && !type.IsAbstract && type.InstanceConstructors.Any(c => c.Parameters.Length == 0 && c.DeclaredAccessibility == Accessibility.Public);

                private string Assign(IPropertySymbol property, string value) => "            " + _receiver + "." + property.Name + " = " + value + ";\n";

                private string Add(IPropertySymbol property, string item) => "            " + _receiver + "." + property.Name + ".Add(" + item + ");\n";

                private static string NullCheck(string parameter) => "            if (" + parameter + " == null) { throw new global::System.ArgumentNullException(nameof(" + parameter + ")); }\n";

                // A nullable collection property is created on the first Add when a List<T> fits it; otherwise Add needs it set.
                private string Ensure(IPropertySymbol property, string itemType) {
                    if (property.Type.NullableAnnotation != NullableAnnotation.Annotated) {
                        return string.Empty;
                    }
                    string definition = property.Type.OriginalDefinition.ToDisplayString();
                    bool fits = definition == "System.Collections.Generic.IList<T>" || definition == "System.Collections.Generic.ICollection<T>" || definition == "System.Collections.Generic.List<T>";
                    if (IsSettable(property) && fits) {
                        return "            " + _receiver + "." + property.Name + " ??= new global::System.Collections.Generic.List<" + itemType + ">();\n";
                    }
                    return "            if (" + _receiver + "." + property.Name + " == null) { throw new global::System.InvalidOperationException(\"" + property.Name + " is not set.\"); }\n";
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
                    IMethodSymbol? factory = _emitter._factory?.GetMembers().OfType<IMethodSymbol>().FirstOrDefault(method => method.IsStatic && !method.IsGenericMethod
                        && method.Parameters.All(parameter => parameter.IsOptional) && SymbolEqualityComparer.Default.Equals(method.ReturnType, _type));
                    string entry = factory == null ? _receiver : _emitter._factory!.Name + "." + factory.Name + "()";
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

                private static string Escape(string name) =>
                    SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None ? "@" + name : name;

                private static string Camel(string name) {
                    string camel = char.ToLowerInvariant(name[0]) + name.Substring(1);
                    return SyntaxFacts.GetKeywordKind(camel) != SyntaxKind.None || SyntaxFacts.GetContextualKeywordKind(camel) != SyntaxKind.None ? "@" + camel : camel;
                }
            }

            // ": " and the property's own summary (crefs are already resolved to IDs; parameter references become code), or "."
            private static string Summary(IPropertySymbol property) {
                string? xml = property.GetDocumentationCommentXml();
                if (string.IsNullOrWhiteSpace(xml)) {
                    return ".";
                }
                try {
                    XElement? summary = XDocument.Parse(xml).Descendants("summary").FirstOrDefault();
                    if (summary == null) {
                        return ".";
                    }
                    foreach (XElement reference in summary.Descendants().Where(e => e.Name.LocalName == "paramref" || e.Name.LocalName == "typeparamref").ToList()) {
                        reference.ReplaceWith(new XElement("c", (string?)reference.Attribute("name") ?? string.Empty));
                    }
                    string inner = string.Concat(summary.Nodes().Select(n => n.ToString(SaveOptions.DisableFormatting)));
                    inner = string.Join(" ", inner.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim())).Trim();
                    return inner.Length == 0 ? "." : ": " + inner;
                } catch (System.Xml.XmlException) {
                    return ".";
                }
            }

            // [Fluent("Name")] on the property, otherwise its name, without the Is prefix of a bool.
            private static string MethodName(IPropertySymbol property) {
                string? declared = NamedArgument(Attribute(property, FluentAttributes.Fluent));
                if (declared != null) {
                    return declared;
                }
                return property.Type.SpecialType == SpecialType.System_Boolean && property.Name.Length > 2 && property.Name.StartsWith("Is", StringComparison.Ordinal) && char.IsUpper(property.Name[2])
                    ? property.Name.Substring(2) : property.Name;
            }

            /// <summary>The singular of a collection property name: Items -> Item, Children -> Child, Series -> Series.</summary>
            internal static string Singular(IPropertySymbol property) => Singular(property.Name);

            internal static string Singular(string name) {
                if (name.EndsWith("Children", StringComparison.Ordinal)) {
                    return name.Substring(0, name.Length - 3);
                }
                if (name.EndsWith("Series", StringComparison.Ordinal) || name.EndsWith("ss", StringComparison.Ordinal) || !name.EndsWith("s", StringComparison.Ordinal)) {
                    return name;
                }
                if (name.EndsWith("ies", StringComparison.Ordinal)) {
                    return name.Substring(0, name.Length - 3) + "y";
                }
                if (name.EndsWith("ches", StringComparison.Ordinal) || name.EndsWith("shes", StringComparison.Ordinal) || name.EndsWith("sses", StringComparison.Ordinal) || name.EndsWith("xes", StringComparison.Ordinal)) {
                    return name.Substring(0, name.Length - 2);
                }
                return name.Substring(0, name.Length - 1);
            }
        }
    }
}
