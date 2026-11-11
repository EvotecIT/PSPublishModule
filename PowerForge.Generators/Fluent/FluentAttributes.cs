using System;
using System.IO;

namespace PowerForge.Generators.Fluent {
    /// <summary>
    /// The attributes a library uses to steer the generator. Their source is <c>FluentAttributes.g.cs</c>, embedded in this
    /// assembly and added to every consuming compilation as internal, embedded types, so libraries that see each other's
    /// internals never get two conflicting copies.
    /// </summary>
    internal static class FluentAttributes {
        internal const string Namespace = "PowerForge.Fluent";
        internal const string Fluent = Namespace + ".FluentAttribute";
        internal const string NoFluent = Namespace + ".NoFluentAttribute";
        internal const string Child = Namespace + ".ChildAttribute";
        internal const string Forward = Namespace + ".FluentForwardAttribute";
        internal const string Cmdlet = Namespace + ".FluentCmdletAttribute";

        private const string ResourceName = "PowerForge.Generators.Fluent.FluentAttributes.g.cs";

        private static readonly Lazy<string> SourceText = new Lazy<string>(() => {
            using Stream stream = typeof(FluentAttributes).Assembly.GetManifestResourceStream(ResourceName)
                ?? throw new InvalidOperationException("The generator assembly is missing its embedded resource " + ResourceName + ".");
            using StreamReader reader = new StreamReader(stream);
            return reader.ReadToEnd();
        });

        internal static string Source => SourceText.Value;
    }
}
