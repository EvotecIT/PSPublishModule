namespace PowerForge.Generated.Runtime
{
    using System;
    using System.Globalization;
    using System.Management.Automation.Host;

    /// <summary>Provides ordinary line input, synchronous console output and formatting geometry.</summary>
    internal sealed class PowerShellPackagedConsoleHost : PSHost
    {
        private readonly Guid _id = Guid.NewGuid();
        private readonly PowerShellPackagedConsoleUI _ui = new PowerShellPackagedConsoleUI();
        internal int? RequestedExitCode { get; private set; }
        public override Guid InstanceId => _id;
        public override string Name => "PowerForge";
        public override Version Version => new Version(1, 0);
        public override PSHostUserInterface UI => _ui;
        public override CultureInfo CurrentCulture => CultureInfo.CurrentCulture;
        public override CultureInfo CurrentUICulture => CultureInfo.CurrentUICulture;
        public override void SetShouldExit(int exitCode) => RequestedExitCode = exitCode;
        public override void NotifyBeginApplication() { }
        public override void NotifyEndApplication() { }
        public override void EnterNestedPrompt() => throw NonInteractive();
        public override void ExitNestedPrompt() => throw NonInteractive();
        internal const string NonInteractiveMessage = "Packaged executables do not support this console operation.";
        internal static NotSupportedException NonInteractive()
            => new NotSupportedException(NonInteractiveMessage);
    }
}
