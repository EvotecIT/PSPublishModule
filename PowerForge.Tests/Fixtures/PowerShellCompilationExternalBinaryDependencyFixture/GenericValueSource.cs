namespace Generic.External.Binary.Dependency;

/// <summary>Independent managed dependency consumed by the external binary-module fixture.</summary>
public static class GenericValueSource
{
    /// <summary>Returns a stable value proving that the transitive assembly executed.</summary>
    public static string Resolve(int value) => $"dependency:{value}";
}
