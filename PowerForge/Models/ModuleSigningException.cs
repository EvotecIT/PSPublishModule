namespace PowerForge;

/// <summary>
/// Exception thrown when module signing fails.
/// </summary>
public sealed class ModuleSigningException : Exception
{
    /// <summary>Optional signing result captured from the signing process.</summary>
    public ModuleSigningResult? Result { get; }

    /// <summary>
    /// Creates a new instance.
    /// </summary>
    public ModuleSigningException(string message, ModuleSigningResult? result = null, Exception? innerException = null)
        : base(message, innerException)
    {
        Result = result;
    }
}
