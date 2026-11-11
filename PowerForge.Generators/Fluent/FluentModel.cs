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
    internal static class FluentModel {
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
        internal static string Summary(IPropertySymbol property) {
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
        internal static string MethodName(IPropertySymbol property) {
            string? declared = NamedArgument(Attribute(property, FluentAttributes.Fluent));
            if (declared != null) {
                return declared;
            }
            return property.Type.SpecialType == SpecialType.System_Boolean && property.Name.Length > 2 && property.Name.StartsWith("Is", StringComparison.Ordinal) && char.IsUpper(property.Name[2])
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
    }
}
