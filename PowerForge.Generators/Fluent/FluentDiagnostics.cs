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
            "[FluentForward] on '{0}' names '{1}.{2}', which has no static overload taking the owner and a value",
            Category, DiagnosticSeverity.Error, isEnabledByDefault: true);
    }
}
