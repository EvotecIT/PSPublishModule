namespace PowerForge.Tests.CSharpAliases;

public class String { }
public struct Int32 { }
public class AliasBase<T> { }
public class CustomDerived : String { }

public class Fixture : AliasBase<Dictionary<string, List<int?[]>>>
{
    public Fixture(int count, string text) { }

    public void Primitives(bool boolean, byte octet, sbyte signedOctet, char character,
        decimal money, double precise, float single, int integer, uint unsignedInteger,
        long large, ulong unsignedLarge, short small, ushort unsignedSmall,
        nint native, nuint unsignedNative, string text, object value) { }

    public Dictionary<string, List<int?[]?>?>? Transform(string?[]? text, int? count, string?[,]? matrix) => null;
    public String? String(Int32? value, string text = "String System.Int32") => null;
    public object? Object(object? value) => value;
    public string? Name { get; set; }
    public int? Count;
}
