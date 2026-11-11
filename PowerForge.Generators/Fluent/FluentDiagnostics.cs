using Microsoft.CodeAnalysis;

namespace PowerForge.Generators.Fluent {
    internal static class FluentDiagnostics {
        private const string Category = "PowerForge.Fluent";

        internal static readonly DiagnosticDescriptor DelegateWithoutVerb = new DiagnosticDescriptor(
            "PFF001", "Delegate property needs a fluent verb",
            "'{0}.{1}' holds a delegate, so a method named after it would never be called; name its method with [Fluent(\"Verb\")] or mark it [NoFluent]",
            Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

        internal static readonly DiagnosticDescriptor Collision = new DiagnosticDescriptor(
            "PFF002", "Fluent method collides with an existing method",
            "The generated '{0}' for '{1}.{2}' has the same signature as an existing method; rename it with [Fluent(\"Name\")] or [Child(\"Name\")], or mark the property [NoFluent]",
            Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

        internal static readonly DiagnosticDescriptor Duplicate = new DiagnosticDescriptor(
            "PFF003", "Two members map to one fluent method",
            "'{1}.{2}' maps to '{0}', which another member of '{1}' already generated; rename one with [Fluent(\"Name\")] or [Child(\"Name\")]",
            Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

        internal static readonly DiagnosticDescriptor ForwardMethodMissing = new DiagnosticDescriptor(
            "PFF004", "Forward target has no matching method",
            "[FluentForward] on '{0}' names '{1}.{2}', which has no accessible, non-generic static overload taking a {3} and a value by value",
            Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

        internal static readonly DiagnosticDescriptor InvalidName = new DiagnosticDescriptor(
            "PFF005", "Fluent name is not a valid identifier",
            "'{0}' on '{1}.{2}' is not a valid C# method name",
            Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

        internal static readonly DiagnosticDescriptor ReservedParameter = new DiagnosticDescriptor(
            "PFF006", "Cmdlet parameter name is reserved",
            "'{0}.{1}' would become parameter -{2}, which PowerShell or the cmdlet base class already uses; rename it with [Fluent(\"Name\")] or mark it [NoFluent]",
            Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

        internal static readonly DiagnosticDescriptor InvalidCmdlet = new DiagnosticDescriptor(
            "PFF007", "[FluentCmdlet] needs a partial PowerShell cmdlet",
            "'{0}' must be a partial class deriving from System.Management.Automation.Cmdlet, in a project that references PowerShell",
            Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

        internal static readonly DiagnosticDescriptor InvalidVocabulary = new DiagnosticDescriptor(
            "PFF008", "Unknown vocabulary setting",
            "'{0}' is not a valid value for {1}",
            Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

        internal static readonly DiagnosticDescriptor UnknownCmdletProperty = new DiagnosticDescriptor(
            "PFF009", "[FluentCmdlet] names an unknown property",
            "'{0}' in {1} on '{2}' is not a property of '{3}'",
            Category, DiagnosticSeverity.Error, isEnabledByDefault: true);
    }
}
