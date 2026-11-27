using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace PowerForge.Generators.Fluent {
    /// <summary>
    /// The model rules every PowerForge generator shares: which types and properties count, how they are named, and what
    /// their documentation says, so fluent methods and cmdlet parameters always describe the same surface.
    /// </summary>
    internal static partial class FluentModel {
        internal static IEnumerable<INamedTypeSymbol> AllTypes(INamespaceSymbol ns) {
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

        internal static bool IsCandidate(INamedTypeSymbol type) =>
            type.TypeKind == TypeKind.Class && !type.IsAbstract && !type.IsStatic && (!type.IsGenericType || type.IsSealed)
            && type.DeclaredAccessibility == Accessibility.Public && type.ContainingType == null && !HasNoFluent(type);

        /// <summary>A public concrete nested class marked [Fluent] itself, whose containing types are public and not generic.</summary>
        internal static bool IsNestedFluent(INamedTypeSymbol type) =>
            type.ContainingType != null && type.TypeKind == TypeKind.Class && !type.IsAbstract && !type.IsStatic && (!type.IsGenericType || type.IsSealed)
            && type.DeclaredAccessibility == Accessibility.Public && HasFluent(type) && !HasNoFluent(type) && NestingProblem(type) == null;

        /// <summary>Why a nested type cannot get extension methods (they live in a top-level class), or null when it can.</summary>
        internal static string? NestingProblem(INamedTypeSymbol type) {
            for (INamedTypeSymbol? container = type.ContainingType; container != null; container = container.ContainingType) {
                if (container.IsGenericType) {
                    return "is nested in the generic type '" + container.Name + "'";
                }
                if (container.DeclaredAccessibility != Accessibility.Public) {
                    return "is nested in '" + container.Name + "', which is not public";
                }
            }
            return null;
        }

        /// <summary>
        /// The name stem of a type's generated <c>&lt;Stem&gt;Fluent</c> class: the type's name, prefixed by its containing types'
        /// names for a nested type (<c>TocBlock.Entry</c> becomes <c>TocBlockEntry</c>).
        /// </summary>
        internal static string FluentStem(INamedTypeSymbol type) {
            string stem = type.Name;
            for (INamedTypeSymbol? container = type.ContainingType; container != null; container = container.ContainingType) {
                stem = container.Name + stem;
            }
            return stem;
        }

        /// <summary>bool or bool?.</summary>
        internal static bool IsBool(ITypeSymbol type) =>
            type.SpecialType == SpecialType.System_Boolean
            || type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable && nullable.TypeArguments[0].SpecialType == SpecialType.System_Boolean;

        /// <summary>Public instance properties, declared or inherited, without indexers, obsolete or [NoFluent] members.</summary>
        internal static IEnumerable<IPropertySymbol> Properties(INamedTypeSymbol type) {
            HashSet<string> names = new HashSet<string>(StringComparer.Ordinal);
            for (INamedTypeSymbol? current = type; current != null && current.SpecialType != SpecialType.System_Object; current = current.BaseType) {
                foreach (IPropertySymbol property in current.GetMembers().OfType<IPropertySymbol>()) {
                    // Only a public derived property hides a base one for callers; it does so even when it is excluded.
                    if (property.IsStatic || property.IsIndexer || property.DeclaredAccessibility != Accessibility.Public || !names.Add(property.Name)) {
                        continue;
                    }
                    if (HasNoFluent(property) || property.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == "System.ObsoleteAttribute")) {
                        continue;
                    }
                    yield return property;
                }
            }
        }

        internal static bool IsSettable(IPropertySymbol property) =>
            property.SetMethod != null && property.SetMethod.DeclaredAccessibility == Accessibility.Public && !property.SetMethod.IsInitOnly;

        // Methods that read the property (configure in place, add to a collection) need a public getter.
        internal static bool IsReadable(IPropertySymbol property) =>
            property.GetMethod != null && property.GetMethod.DeclaredAccessibility == Accessibility.Public;

        /// <summary>The item type of a mutable collection (ICollection&lt;T&gt;, not a dictionary, array or string), or null.</summary>
        internal static ITypeSymbol? CollectionItem(ITypeSymbol type) {
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

        internal static AttributeData? Attribute(ISymbol symbol, string name) => symbol.GetAttributes().FirstOrDefault(a => a.AttributeClass?.ToDisplayString() == name);

        internal static bool HasFluent(ISymbol symbol) => Attribute(symbol, FluentAttributes.Fluent) != null;

        internal static bool HasNoFluent(ISymbol symbol) => Attribute(symbol, FluentAttributes.NoFluent) != null;

        internal static string? NamedArgument(AttributeData? attribute) =>
            attribute != null && attribute.ConstructorArguments.Length == 1 ? attribute.ConstructorArguments[0].Value as string : null;

        internal static IEnumerable<INamedTypeSymbol> BaseTypes(INamedTypeSymbol type) {
            for (INamedTypeSymbol? current = type; current != null; current = current.BaseType) {
                yield return current;
            }
        }

        // ": " and the property's own summary (crefs are already resolved to IDs; parameter references become code), or "."
        internal static string Summary(ISymbol property) {
            string? inner = DocText(property, "summary", null);
            return inner == null ? "." : ": " + inner;
        }

        /// <summary>
        /// The text of a documentation element (<c>summary</c>, or <c>param</c> with its name) as one line, crefs resolved and
        /// parameter references turned into code; null when the symbol has none.
        /// </summary>
        internal static string? DocText(ISymbol symbol, string element, string? name) {
            string? xml = symbol.GetDocumentationCommentXml();
            if (string.IsNullOrWhiteSpace(xml)) {
                return null;
            }
            try {
                XElement? found = XDocument.Parse(xml).Descendants(element).FirstOrDefault(e => name == null || (string?)e.Attribute("name") == name);
                if (found == null) {
                    return null;
                }
                foreach (XElement reference in found.Descendants().Where(e => e.Name.LocalName == "paramref" || e.Name.LocalName == "typeparamref").ToList()) {
                    reference.ReplaceWith(new XElement("c", (string?)reference.Attribute("name") ?? string.Empty));
                }
                string inner = string.Concat(found.Nodes().Select(n => n.ToString(SaveOptions.DisableFormatting)));
                inner = string.Join(" ", inner.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries).Select(l => l.Trim())).Trim();
                return inner.Length == 0 ? null : inner;
            } catch (System.Xml.XmlException) {
                return null;
            }
        }

        // [Fluent("Name")] on the property, otherwise its name, without the Is prefix of a bool or bool?.
        internal static string MethodName(IPropertySymbol property) {
            string? declared = NamedArgument(Attribute(property, FluentAttributes.Fluent));
            if (declared != null) {
                return declared;
            }
            return IsBool(property.Type) && property.Name.Length > 2 && property.Name.StartsWith("Is", StringComparison.Ordinal) && char.IsUpper(property.Name[2])
                ? property.Name.Substring(2) : property.Name;
        }

        /// <summary>The singular of a collection property name: Items -> Item, Children -> Child, Series -> Series.</summary>
        // A name as a C# identifier in generated code: keywords get an @ prefix (@event).
        // A public parameterless constructor that leaves no required member unset.
        internal static bool Creatable(INamedTypeSymbol type) =>
            type.TypeKind == TypeKind.Class && !type.IsAbstract && type.InstanceConstructors.Any(c => c.Parameters.Length == 0 && c.DeclaredAccessibility == Accessibility.Public
                && (c.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == "System.Diagnostics.CodeAnalysis.SetsRequiredMembersAttribute")
                    || !BaseTypes(type).SelectMany(t => t.GetMembers()).Any(m => m is IPropertySymbol { IsRequired: true } || m is IFieldSymbol { IsRequired: true })));

        // An MSBuild list setting. The package's targets turn ';' into ',' because analyzer config treats ';' as a comment.
        internal static List<string> ListSetting(string? value) =>
            (value ?? string.Empty).Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries).Select(name => name.Trim()).Where(name => name.Length > 0).ToList();

        internal static string Escape(string name) => SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None ? "@" + name : name;

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

        /// <summary>
        /// The key a generated method's value parameters are compared by: C# signatures ignore nullable annotations, and
        /// <c>dynamic</c> is <c>object</c> to overload resolution.
        /// </summary>
        internal static string SignatureKey(string name, IEnumerable<ITypeSymbol> parameters) =>
            name + "(" + string.Join(", ", parameters.Select(parameter => DynamicName.Replace(parameter.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat), "object"))) + ")";

        // dynamic as a type of its own, not a namespace-qualified type that happens to be called dynamic.
        private static readonly System.Text.RegularExpressions.Regex DynamicName = new System.Text.RegularExpressions.Regex(@"(?<![\w.:@])dynamic(?!\w)");
    }
}
