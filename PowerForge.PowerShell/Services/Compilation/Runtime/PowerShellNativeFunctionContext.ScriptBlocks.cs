namespace PowerForge.Generated.Runtime
{
    using System;
    using System.Management.Automation;

    public sealed partial class PowerShellNativeFunctionContext
    {
        /// <summary>Creates a compiled script block in the current defining module without capturing an invocation context.</summary>
        /// <remarks>Each invocation receives its own native parameter and local storage. Free variable reads use PowerShell's dynamic scope.</remarks>
        public ScriptBlock CreateScriptBlock(string sourceDocument, int startOffset, int endOffset,
            Action<PowerShellNativeFunctionContext>? begin,
            Action<PowerShellNativeFunctionContext>? process,
            Action<PowerShellNativeFunctionContext>? end,
            Action<PowerShellNativeFunctionContext>? clean = null)
        {
            EnsureActive();
            var module = _session.Module
                ?? throw new NotSupportedException("Compiled script blocks require a defining PowerShell module.");
            var file = _contract.DefiningFile.GetValue(FunctionContext) as string ?? string.Empty;
            return PowerShellNativeFunctionHost.CreateCompiledScriptBlock(module, sourceDocument, file, startOffset, endOffset,
                begin, process, end, clean);
        }
    }
}
