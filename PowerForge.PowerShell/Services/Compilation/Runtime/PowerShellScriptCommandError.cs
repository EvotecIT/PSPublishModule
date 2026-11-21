namespace PowerForge.Generated.Runtime
{
    /// <summary>Represents a statement error from a command lowered into a standalone executable.</summary>
    internal sealed class PowerShellScriptCommandError : Exception
    {
        private readonly string _diagnostic;

        internal PowerShellScriptCommandError(string message, string diagnostic, int sourceLine, Exception inner)
            : base(message, inner)
        {
            _diagnostic = diagnostic;
            SourceLine = sourceLine;
        }

        internal int SourceLine { get; }

        internal void Report() => Console.Error.Write(_diagnostic);
    }
}
