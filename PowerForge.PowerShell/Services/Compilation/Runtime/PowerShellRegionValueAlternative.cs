namespace PowerForge.Generated.Runtime
{
    /// <summary>
    /// Carries one compiler-proved CLR alternative without exposing its value to PowerShell's
    /// pipeline enumerator before the retained function restores the authored type constraint.
    /// </summary>
    public sealed class PowerShellRegionValueAlternative
    {
        private PowerShellRegionValueAlternative(int alternativeIndex, object? value)
        {
            AlternativeIndex = alternativeIndex;
            Value = value;
        }

        /// <summary>Zero-based alternative selected by the typed conditional initializer.</summary>
        public int AlternativeIndex { get; }

        /// <summary>The exact scalar or one-dimensional stable-scalar vector to restore.</summary>
        public object? Value { get; }

        /// <summary>Creates a value whose index was validated by the compiler-generated helper.</summary>
        public static PowerShellRegionValueAlternative Create(int alternativeIndex, object? value)
        {
            if ((uint)alternativeIndex > 1u)
                throw new System.ArgumentOutOfRangeException(nameof(alternativeIndex));
            return new PowerShellRegionValueAlternative(alternativeIndex, value);
        }
    }
}
