namespace PowerForge;

/// <summary>An inclusive interval proved for a bound Int32 value at its evaluation site.</summary>
internal sealed class PowerShellInt32Range
{
    internal PowerShellInt32Range(int minimum, int maximum)
    {
        if (minimum > maximum) throw new ArgumentOutOfRangeException(nameof(minimum));
        Minimum = minimum;
        Maximum = maximum;
    }

    internal int Minimum { get; }
    internal int Maximum { get; }
}
