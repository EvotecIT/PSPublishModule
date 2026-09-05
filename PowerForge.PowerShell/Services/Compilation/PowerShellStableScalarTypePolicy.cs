namespace PowerForge;

/// <summary>Defines CLR values whose scalar identity is stable at runtime-free PowerShell boundaries.</summary>
internal static class PowerShellStableScalarTypePolicy
{
    internal static bool IsSupported(Type type)
    {
        var scalar = Nullable.GetUnderlyingType(type) ?? type;
        return scalar.IsEnum ||
               scalar == typeof(bool) || scalar == typeof(byte) || scalar == typeof(sbyte) ||
               scalar == typeof(short) || scalar == typeof(ushort) || scalar == typeof(int) || scalar == typeof(uint) ||
               scalar == typeof(long) || scalar == typeof(ulong) || scalar == typeof(float) || scalar == typeof(double) ||
               scalar == typeof(decimal) || scalar == typeof(char) || scalar == typeof(string) ||
               scalar == typeof(DateTime) || scalar == typeof(DateTimeOffset) || scalar == typeof(TimeSpan) ||
               scalar == typeof(Guid) || scalar == typeof(Uri) || scalar == typeof(Version);
    }
}
