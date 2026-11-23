namespace PowerForge;

/// <summary>Identifies one trailing native CLR reference and its invocation-owned storage slot.</summary>
internal readonly record struct PowerShellNativeReferenceArgument(int Index, string VariableName);
