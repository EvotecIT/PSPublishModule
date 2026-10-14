namespace PowerForge.Generated.Runtime
{
    using System;
    using System.Globalization;
    using System.Management.Automation.Host;

    /// <summary>Provides synchronous console output and formatting geometry without interactive input.</summary>
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
        internal static NotSupportedException NonInteractive()
            => new NotSupportedException("Packaged executables do not support interactive console input.");
    }
}
