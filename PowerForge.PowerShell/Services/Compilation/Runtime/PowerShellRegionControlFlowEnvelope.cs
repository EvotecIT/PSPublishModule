namespace PowerForge.Generated.Runtime
{
    /// <summary>One compiler-owned return-or-fallthrough decision consumed by retained PowerShell.</summary>
    public sealed class PowerShellRegionControlFlowEnvelope
    {
        private static readonly PowerShellRegionControlFlowEnvelope FallThroughValue = new(false, null);

        private PowerShellRegionControlFlowEnvelope(bool shouldReturn, object? value)
        {
            ShouldReturn = shouldReturn;
            Value = value;
        }

        /// <summary>Whether the retained function must execute its authored return boundary.</summary>
        public bool ShouldReturn { get; }

        /// <summary>The value emitted by the retained return boundary when <see cref="ShouldReturn"/> is true.</summary>
        public object? Value { get; }

        /// <summary>Creates the shared fallthrough result.</summary>
        public static PowerShellRegionControlFlowEnvelope FallThrough() => FallThroughValue;

        /// <summary>Creates a return result without enumerating or otherwise observing the value.</summary>
        public static PowerShellRegionControlFlowEnvelope Return(object? value) => new(true, value);
    }
}
