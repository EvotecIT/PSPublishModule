namespace PowerForge.Generated.Runtime
{
    using System;
    using System.Collections.Generic;
    using System.Collections.ObjectModel;
    using System.Management.Automation;
    using System.Management.Automation.Host;
    using System.Security;

    /// <summary>Writes formatter output directly; existing stream subscriptions own informational diagnostics.</summary>
    internal sealed class PowerShellPackagedConsoleUI : PSHostUserInterface
    {
        private readonly PowerShellPackagedConsoleRawUI _raw = new PowerShellPackagedConsoleRawUI();
        public override PSHostRawUserInterface RawUI => _raw;
        public override void Write(string value) => Console.Out.Write(RenderOutput(value));
        public override void Write(ConsoleColor foregroundColor, ConsoleColor backgroundColor, string value) => Write(value);
        public override void WriteLine(string value) => Console.Out.WriteLine(RenderOutput(value));
        public override void WriteLine(ConsoleColor foregroundColor, ConsoleColor backgroundColor, string value) => WriteLine(value);
        public override void WriteErrorLine(string value)
        {
            // Native ConsoleHost ignores the formatter's empty error-line sentinel.
            if (!string.IsNullOrEmpty(value)) Console.Error.WriteLine(RenderOutput(value));
        }
        private static string RenderOutput(string value)
        {
#if NETFRAMEWORK
            return value;
#else
            return GetOutputString(value, supportsVirtualTerminal: false);
#endif
        }
        // These records already have immediate, ordered DataAdded subscribers in the launcher.
        public override void WriteDebugLine(string message) { }
        public override void WriteVerboseLine(string message) { }
        public override void WriteWarningLine(string message) { }
        public override void WriteProgress(long sourceId, ProgressRecord record) { }
        public override string ReadLine() => throw PowerShellPackagedConsoleHost.NonInteractive();
        public override SecureString ReadLineAsSecureString() => throw PowerShellPackagedConsoleHost.NonInteractive();
        public override Dictionary<string, PSObject> Prompt(string caption, string message, Collection<FieldDescription> descriptions)
            => throw PowerShellPackagedConsoleHost.NonInteractive();
        public override int PromptForChoice(string caption, string message, Collection<ChoiceDescription> choices, int defaultChoice)
            => throw PowerShellPackagedConsoleHost.NonInteractive();
        public override PSCredential PromptForCredential(string caption, string message, string userName, string targetName)
            => throw PowerShellPackagedConsoleHost.NonInteractive();
        public override PSCredential PromptForCredential(string caption, string message, string userName, string targetName,
            PSCredentialTypes allowedCredentialTypes, PSCredentialUIOptions options)
            => throw PowerShellPackagedConsoleHost.NonInteractive();
    }

    /// <summary>Supplies native formatter dimensions without changing the terminal or exposing input operations.</summary>
    internal sealed class PowerShellPackagedConsoleRawUI : PSHostRawUserInterface
    {
        private readonly Size _size = GetSize();
        private static Size GetSize()
        {
            // ConsoleHost's redirected default is 120 columns. Out-Default owns its margin and wrapping.
            try
            {
                if (!Console.IsOutputRedirected && Console.WindowWidth > 0)
                    return new Size(Console.WindowWidth, Math.Max(Console.WindowHeight, 1));
            }
            catch (System.IO.IOException) { }
            return new Size(120, 40);
        }
        public override ConsoleColor ForegroundColor { get; set; } = ConsoleColor.Gray;
        public override ConsoleColor BackgroundColor { get; set; } = ConsoleColor.Black;
        public override Coordinates CursorPosition { get => new Coordinates(0, 0); set => throw PowerShellPackagedConsoleHost.NonInteractive(); }
        public override Coordinates WindowPosition { get => new Coordinates(0, 0); set => throw PowerShellPackagedConsoleHost.NonInteractive(); }
        public override int CursorSize { get => 1; set => throw PowerShellPackagedConsoleHost.NonInteractive(); }
        public override Size BufferSize { get => _size; set => throw PowerShellPackagedConsoleHost.NonInteractive(); }
        public override Size WindowSize { get => _size; set => throw PowerShellPackagedConsoleHost.NonInteractive(); }
        public override Size MaxWindowSize => _size;
        public override Size MaxPhysicalWindowSize => _size;
        public override bool KeyAvailable => false;
        public override string WindowTitle { get => "PowerForge"; set => throw PowerShellPackagedConsoleHost.NonInteractive(); }
        public override void FlushInputBuffer() => throw PowerShellPackagedConsoleHost.NonInteractive();
        public override KeyInfo ReadKey(ReadKeyOptions options) => throw PowerShellPackagedConsoleHost.NonInteractive();
        public override BufferCell[,] GetBufferContents(Rectangle rectangle) => throw PowerShellPackagedConsoleHost.NonInteractive();
        public override void SetBufferContents(Coordinates origin, BufferCell[,] contents) => throw PowerShellPackagedConsoleHost.NonInteractive();
        public override void SetBufferContents(Rectangle rectangle, BufferCell fill) => throw PowerShellPackagedConsoleHost.NonInteractive();
        public override void ScrollBufferContents(Rectangle source, Coordinates destination, Rectangle clip, BufferCell fill)
            => throw PowerShellPackagedConsoleHost.NonInteractive();
    }
}
