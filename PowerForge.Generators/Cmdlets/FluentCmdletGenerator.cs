using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;
using PowerForge.Generators.Fluent;
using static PowerForge.Generators.Fluent.FluentModel;

namespace PowerForge.Generators.Cmdlets {
    /// <summary>
    /// Generates PowerShell parameters from a model for a cmdlet marked <c>[FluentCmdlet(typeof(Model))]</c>: one parameter
    /// per public property, named like its fluent method, with the property's documentation. Bools become switches,
    /// settings objects take a hashtable whose keys are their properties (unknown keys are rejected), and collections take
    /// arrays. <c>ApplyParameters(model)</c> copies only the parameters the caller bound, converting hashtable values with
    /// PowerShell's own rules. A member the cmdlet declares itself replaces the generated parameter.
    /// </summary>
    [Generator(LanguageNames.CSharp)]
    public sealed class FluentCmdletGenerator : IIncrementalGenerator {
        private const string Automation = "global::System.Management.Automation.";

        // PowerShell's common and risk-mitigation parameters.
        private static readonly HashSet<string> CommonParameters = new HashSet<string>(StringComparer.OrdinalIgnoreCase) {
            "Verbose", "Debug", "ErrorAction", "WarningAction", "InformationAction", "ProgressAction", "ErrorVariable", "WarningVariable",
            "InformationVariable", "OutVariable", "OutBuffer", "PipelineVariable", "WhatIf", "Confirm"
        };

        /// <inheritdoc />
        public void Initialize(IncrementalGeneratorInitializationContext context) {
            IncrementalValuesProvider<INamedTypeSymbol> cmdlets = context.SyntaxProvider.ForAttributeWithMetadataName(
                FluentAttributes.Cmdlet, (node, _) => node is ClassDeclarationSyntax, (attributed, _) => (INamedTypeSymbol)attributed.TargetSymbol);
            context.RegisterSourceOutput(cmdlets.Combine(context.CompilationProvider), (output, input) => Generate(output, input.Left, input.Right));
        }

        private static void Generate(SourceProductionContext output, INamedTypeSymbol cmdlet, Compilation compilation) {
            AttributeData? attribute = Attribute(cmdlet, FluentAttributes.Cmdlet);
            Location location = cmdlet.Locations.FirstOrDefault() ?? Location.None;
            INamedTypeSymbol? cmdletBase = compilation.GetTypeByMetadataName("System.Management.Automation.Cmdlet");
            bool partial = cmdlet.DeclaringSyntaxReferences.All(reference => reference.GetSyntax() is ClassDeclarationSyntax declaration
                && declaration.Modifiers.Any(modifier => modifier.IsKind(SyntaxKind.PartialKeyword)));
            if (cmdletBase == null || !partial || !BaseTypes(cmdlet).Any(type => SymbolEqualityComparer.Default.Equals(type, cmdletBase)) || cmdlet.ContainingType != null) {
                output.ReportDiagnostic(Diagnostic.Create(FluentDiagnostics.InvalidCmdlet, location, cmdlet.Name));
                return;
            }
            if (attribute?.ConstructorArguments.FirstOrDefault().Value is not INamedTypeSymbol model) {
                return;
            }
            new Writer(output, cmdlet, model, compilation, attribute!).Write();
        }

        private sealed class Writer {
            private static readonly SymbolDisplayFormat Format = SymbolDisplayFormat.FullyQualifiedFormat;

            private readonly SourceProductionContext _output;
            private readonly INamedTypeSymbol _cmdlet;
            private readonly INamedTypeSymbol _model;
            private readonly Compilation _compilation;
            private readonly StringBuilder _parameters = new StringBuilder();
            private readonly StringBuilder _apply = new StringBuilder();
            private readonly StringBuilder _helpers = new StringBuilder();
            private readonly HashSet<string> _helperTypes = new HashSet<string>(StringComparer.Ordinal);
            private readonly AttributeData _attribute;

            internal Writer(SourceProductionContext output, INamedTypeSymbol cmdlet, INamedTypeSymbol model, Compilation compilation, AttributeData attribute) {
                _output = output;
                _cmdlet = cmdlet;
                _model = model;
                _compilation = compilation;
                _attribute = attribute;
            }

            internal void Write() {
                List<IPropertySymbol> properties = Properties(_model).ToList();
                HashSet<string>? include = Names("Include", properties);
                HashSet<string>? exclude = Names("Exclude", properties);
                foreach (IPropertySymbol property in properties) {
                    bool listed(HashSet<string> names) => names.Contains(property.Name) || names.Contains(MethodName(property));
                    if (include != null && !listed(include) || exclude != null && listed(exclude)) {
                        continue;
                    }
                    AppendParameter(property);
                }
                string modelType = _model.ToDisplayString(Format);
                StringBuilder source = new StringBuilder();
                source.Append("// <auto-generated>\n// Generated by PowerForge.Generators from the public properties of ").Append(_model.ToDisplayString())
                    .Append(".\n// Do not edit: change the properties, or declare the parameter in the cmdlet to replace it.\n// </auto-generated>\n\n");
                bool global = _cmdlet.ContainingNamespace.IsGlobalNamespace;
                if (!global) { source.Append("namespace ").Append(_cmdlet.ContainingNamespace.ToDisplayString()).Append(" {\n"); }
                source.Append("    partial class ").Append(_cmdlet.Name).Append(" {\n")
                    .Append(_parameters)
                    .Append("        /// <summary>Copies the parameters the caller bound onto <paramref name=\"model\"/>.</summary>\n")
                    .Append("        /// <param name=\"model\">The model to configure.</param>\n")
                    .Append("        internal void ApplyParameters(").Append(modelType).Append(" model) {\n")
                    .Append("            ApplyParameters(model, MyInvocation.BoundParameters);\n")
                    .Append("        }\n\n")
                    .Append("        /// <summary>Copies the parameters named in <paramref name=\"bound\"/> onto <paramref name=\"model\"/>.</summary>\n")
                    .Append("        /// <param name=\"model\">The model to configure.</param>\n")
                    .Append("        /// <param name=\"bound\">The bound parameters, by name.</param>\n")
                    .Append("        internal void ApplyParameters(").Append(modelType).Append(" model, global::System.Collections.Generic.IDictionary<string, object> bound) {\n")
                    .Append("            if (model == null) { throw new global::System.ArgumentNullException(nameof(model)); }\n")
                    .Append("            if (bound == null) { throw new global::System.ArgumentNullException(nameof(bound)); }\n")
                    .Append(_apply)
                    .Append("        }\n");
                if (_helpers.Length > 0) { source.Append('\n').Append(_helpers.ToString().TrimEnd('\n')).Append('\n'); }
                source.Append("    }\n");
                if (!global) { source.Append("}\n"); }
                _output.AddSource(_cmdlet.ToDisplayString().Replace('<', '_').Replace('>', '_') + ".FluentCmdlet.g.cs", source.ToString());
            }

            private void AppendParameter(IPropertySymbol property) {
                if (property.Type.TypeKind == TypeKind.Delegate) {
                    return;
                }
                string name = MethodName(property);
                ITypeSymbol? item = IsReadable(property) ? CollectionItem(property.Type) : null;
                INamedTypeSymbol? settings = item == null && IsReadable(property) && property.Type is INamedTypeSymbol named && IsSettings(named) ? named : null;
                if (item == null && settings == null && !IsSettable(property)) {
                    return;
                }
                // A parameter the cmdlet declares itself keeps its validation, completers and help; when its type fits the
                // property, ApplyParameters still copies it. Any other member with the name leaves the property alone.
                IPropertySymbol? declared = DeclaredParameter(name);
                if (declared != null) {
                    string? reused = Reuse(declared, property, item, settings, name);
                    if (reused != null) {
                        _apply.Append("            if (bound.ContainsKey(\"").Append(declared.Name).Append("\")) {\n").Append(reused).Append("            }\n");
                    }
                    return;
                }
                if (Declared(name)) {
                    return;
                }
                if (Reserved(name)) {
                    _output.ReportDiagnostic(Diagnostic.Create(FluentDiagnostics.ReservedParameter, property.Locations.FirstOrDefault() ?? _cmdlet.Locations.FirstOrDefault() ?? Location.None,
                        _model.Name, property.Name, name));
                    return;
                }
                string member = "model." + Escape(property.Name);
                string parameterName = Escape(name);
                string parameterType;
                string apply;
                if (item != null) {
                    string itemType = item.ToDisplayString(Format);
                    parameterType = itemType + "[]";
                    apply = Ensure(property, member, itemType)
                        + "                foreach (" + itemType + " item in " + parameterName + ") { ((global::System.Collections.Generic.ICollection<" + itemType + ">)" + member + ").Add(item); }\n";
                } else if (settings != null) {
                    parameterType = "global::System.Collections.IDictionary";
                    apply = "                " + member + " = " + Configure(settings) + "(" + member + Create(property, settings) + ", " + parameterName + ", \"" + name + "\");\n";
                    if (!IsSettable(property)) {
                        apply = "                " + Configure(settings) + "(" + member + " ?? throw new global::System.InvalidOperationException(\"" + property.Name + " is not set.\"), " + parameterName + ", \"" + name + "\");\n";
                    }
                } else if (IsBool(property.Type)) {
                    parameterType = Automation + "SwitchParameter";
                    apply = "                " + member + " = " + parameterName + ".IsPresent;\n";
                } else {
                    parameterType = Underlying(property.Type).ToDisplayString(Format);
                    apply = "                " + member + " = " + parameterName + ";\n";
                }
                _parameters.Append("        /// <summary>").Append(Sentence(property)).Append("</summary>\n")
                    .Append("        [").Append(Automation).Append("Parameter]\n")
                    .Append("        public ").Append(parameterType).Append(' ').Append(parameterName).Append(" { get; set; }\n\n");
                _apply.Append("            if (bound.ContainsKey(\"").Append(name).Append("\")) {\n").Append(apply).Append("            }\n");
            }

            // How ApplyParameters copies a parameter the cmdlet declares, or null when its type does not fit the property.
            private string? Reuse(IPropertySymbol declared, IPropertySymbol property, ITypeSymbol? item, INamedTypeSymbol? settings, string name) {
                string member = "model." + Escape(property.Name);
                string value = Escape(declared.Name);
                if (item != null) {
                    if (declared.Type is IArrayTypeSymbol array && _compilation.ClassifyConversion(array.ElementType, item).IsImplicit) {
                        string itemType = item.ToDisplayString(Format);
                        return Ensure(property, member, itemType)
                            + "                foreach (" + itemType + " item in " + value + ") { ((global::System.Collections.Generic.ICollection<" + itemType + ">)" + member + ").Add(item); }\n";
                    }
                    return null;
                }
                INamedTypeSymbol? dictionary = _compilation.GetTypeByMetadataName("System.Collections.IDictionary");
                if (settings != null && dictionary != null && _compilation.ClassifyConversion(declared.Type, dictionary).IsImplicit) {
                    return IsSettable(property)
                        ? "                " + member + " = " + Configure(settings) + "(" + member + Create(property, settings) + ", " + value + ", \"" + name + "\");\n"
                        : "                " + Configure(settings) + "(" + member + " ?? throw new global::System.InvalidOperationException(\"" + property.Name + " is not set.\"), " + value + ", \"" + name + "\");\n";
                }
                if (!IsSettable(property)) {
                    return null;
                }
                if (declared.Type.ToDisplayString() == "System.Management.Automation.SwitchParameter") {
                    return IsBool(property.Type) ? "                " + member + " = " + value + ".IsPresent;\n" : null;
                }
                return _compilation.ClassifyConversion(declared.Type, property.Type).IsImplicit ? "                " + member + " = " + value + ";\n" : null;
            }

            // The names listed in [FluentCmdlet(Include/Exclude = ...)], or null when the list is not set; unknown names are errors.
            private HashSet<string>? Names(string argument, List<IPropertySymbol> properties) {
                TypedConstant list = _attribute.NamedArguments.FirstOrDefault(pair => pair.Key == argument).Value;
                if (list.Kind != TypedConstantKind.Array || list.IsNull) {
                    return null;
                }
                HashSet<string> names = new HashSet<string>(list.Values.Select(value => value.Value as string).Where(value => value != null).Select(value => value!), StringComparer.OrdinalIgnoreCase);
                foreach (string name in names.Where(name => !properties.Any(property => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)
                    || string.Equals(MethodName(property), name, StringComparison.OrdinalIgnoreCase)))) {
                    _output.ReportDiagnostic(Diagnostic.Create(FluentDiagnostics.UnknownCmdletProperty, _attribute.ApplicationSyntaxReference?.GetSyntax().GetLocation() ?? Location.None,
                        name, argument, _cmdlet.Name, _model.Name));
                }
                return names;
            }

            // A [Parameter] property the cmdlet itself declares with this name.
            private IPropertySymbol? DeclaredParameter(string name) => BaseTypes(_cmdlet).Where(type => !InPowerShell(type))
                .SelectMany(type => type.GetMembers().OfType<IPropertySymbol>())
                .FirstOrDefault(property => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)
                    && property.GetAttributes().Any(attribute => attribute.AttributeClass?.ToDisplayString() == "System.Management.Automation.ParameterAttribute"));

            // Generates (once per type) the method that applies a hashtable to a settings object, and returns its name.
            private string Configure(INamedTypeSymbol settings) {
                settings = (INamedTypeSymbol)settings.WithNullableAnnotation(NullableAnnotation.NotAnnotated);
                string method = "Configure" + string.Concat(settings.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat).Replace("global::", string.Empty).Split('.', '<', '>', ',', ' ', '?').Select(part => part.Length == 0 ? part : char.ToUpperInvariant(part[0]) + part.Substring(1)));
                if (!_helperTypes.Add(method)) {
                    return method;
                }
                string type = settings.ToDisplayString(Format);
                StringBuilder cases = new StringBuilder();
                List<string> keys = new List<string>();
                HashSet<string> labels = new HashSet<string>(StringComparer.Ordinal);
                foreach (IPropertySymbol property in Properties(settings).Where(p => p.Type.TypeKind != TypeKind.Delegate)) {
                    string key = MethodName(property);
                    string member = "target." + Escape(property.Name);
                    ITypeSymbol? item = IsReadable(property) ? CollectionItem(property.Type) : null;
                    INamedTypeSymbol? nested = item == null && IsReadable(property) && property.Type is INamedTypeSymbol named && IsSettings(named) ? named : null;
                    string statement;
                    if (item != null) {
                        string itemType = item.ToDisplayString(Format);
                        statement = Ensure(property, member, itemType).Replace("                ", "                        ")
                            + "                        foreach (" + itemType + " item in " + Automation + "LanguagePrimitives.ConvertTo<" + itemType + "[]>(entry.Value)) { ((global::System.Collections.Generic.ICollection<" + itemType + ">)" + member + ").Add(item); }\n";
                    } else if (nested != null) {
                        string inner = "(" + Automation + "LanguagePrimitives.ConvertTo<global::System.Collections.IDictionary>(entry.Value), path + \"." + key + "\")";
                        statement = IsSettable(property)
                            ? "                        " + member + " = " + Configure(nested) + "(" + member + Create(property, nested) + ", " + inner.Substring(1) + ";\n"
                            : "                        " + Configure(nested) + "(" + member + " ?? throw new global::System.InvalidOperationException(\"" + property.Name + " is not set.\"), " + inner.Substring(1) + ";\n";
                    } else if (IsSettable(property)) {
                        statement = "                        " + member + " = " + Automation + "LanguagePrimitives.ConvertTo<" + property.Type.ToDisplayString(Format) + ">(entry.Value);\n";
                    } else {
                        continue;
                    }
                    // The key is the parameter-style name (Visible); the property name (IsVisible) is accepted too.
                    List<string> accepted = new[] { key, property.Name }.Select(label => label.ToLowerInvariant()).Where(labels.Add).ToList();
                    if (accepted.Count == 0) {
                        continue;
                    }
                    keys.Add(key);
                    foreach (string label in accepted) {
                        cases.Append("                    case \"").Append(label).Append("\":\n");
                    }
                    cases.Append(statement).Append("                        break;\n");
                }
                _helpers.Append("        // Applies a hashtable to a ").Append(settings.Name).Append("; keys are its property names, in any case.\n")
                    .Append("        private static ").Append(type).Append(' ').Append(method).Append('(').Append(type).Append(" target, global::System.Collections.IDictionary values, string path) {\n")
                    .Append("            foreach (global::System.Collections.DictionaryEntry entry in values) {\n")
                    .Append("                string key = global::System.Convert.ToString(entry.Key, global::System.Globalization.CultureInfo.InvariantCulture) ?? string.Empty;\n")
                    .Append("                switch (key.ToLowerInvariant()) {\n")
                    .Append(cases)
                    .Append("                    default:\n")
                    .Append("                        throw new global::System.ArgumentException(\"-\" + path + \" has no key '\" + key + \"'. Valid keys: ")
                    .Append(string.Join(", ", keys)).Append(".\");\n")
                    .Append("                }\n            }\n            return target;\n        }\n\n");
                return method;
            }

            // A settings object: a public class of the model's assembly with something to set or add.
            private bool IsSettings(INamedTypeSymbol type) =>
                type.TypeKind == TypeKind.Class && type.SpecialType == SpecialType.None && !type.IsAbstract && type.DeclaredAccessibility == Accessibility.Public
                && SymbolEqualityComparer.Default.Equals(type.ContainingAssembly, _model.ContainingAssembly)
                && Properties(type).Any(p => p.Type.TypeKind != TypeKind.Delegate && (IsSettable(p) || IsReadable(p) && CollectionItem(p.Type) != null));

            // ", new T()" when an unset settings object can be created; otherwise "" (and a null target fails in the helper).
            private static string Create(IPropertySymbol property, INamedTypeSymbol settings) =>
                IsSettable(property) && settings.InstanceConstructors.Any(c => c.Parameters.Length == 0 && c.DeclaredAccessibility == Accessibility.Public)
                    ? " ?? new " + settings.ToDisplayString(Format) + "()"
                    : " ?? throw new global::System.InvalidOperationException(\"" + property.Name + " is not set.\")";

            private static string Ensure(IPropertySymbol property, string member, string itemType) {
                string definition = property.Type.OriginalDefinition.ToDisplayString();
                bool fits = definition == "System.Collections.Generic.IList<T>" || definition == "System.Collections.Generic.ICollection<T>" || definition == "System.Collections.Generic.List<T>";
                return IsSettable(property) && fits
                    ? "                if (" + member + " == null) { " + member + " = new global::System.Collections.Generic.List<" + itemType + ">(); }\n"
                    : string.Empty;
            }

            // Whether the cmdlet declares this member itself (outside PowerShell's own base classes).
            private bool Declared(string name) => BaseTypes(_cmdlet).Where(type => !InPowerShell(type))
                .Any(type => type.GetMembers().Any(member => string.Equals(member.Name, name, StringComparison.OrdinalIgnoreCase)));

            private bool Reserved(string name) => CommonParameters.Contains(name)
                || BaseTypes(_cmdlet).Where(InPowerShell).Any(type => type.GetMembers().Any(member => string.Equals(member.Name, name, StringComparison.OrdinalIgnoreCase)));

            private static bool InPowerShell(INamedTypeSymbol type) => type.ContainingNamespace.ToDisplayString() == "System.Management.Automation" || type.SpecialType == SpecialType.System_Object;

            private static bool IsBool(ITypeSymbol type) => Underlying(type).SpecialType == SpecialType.System_Boolean;

            private static ITypeSymbol Underlying(ITypeSymbol type) =>
                type is INamedTypeSymbol { OriginalDefinition.SpecialType: SpecialType.System_Nullable_T } nullable ? nullable.TypeArguments[0] : type.WithNullableAnnotation(NullableAnnotation.None);

            // The property's summary as parameter help, or a plain sentence when it has none.
            private static string Sentence(IPropertySymbol property) {
                string summary = Summary(property);
                return summary == "." ? "Sets " + property.Name + "." : summary.Substring(2);
            }
        }
    }
}
