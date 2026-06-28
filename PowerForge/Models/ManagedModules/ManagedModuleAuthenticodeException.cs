namespace PowerForge;

/// <summary>
/// Exception thrown when managed module Authenticode validation fails.
/// </summary>
public sealed class ManagedModuleAuthenticodeException : InvalidOperationException
{
    /// <summary>
    /// Creates an Authenticode validation exception.
    /// </summary>
    public ManagedModuleAuthenticodeException(string message, string? filePath, int statusCode)
        : base(message)
    {
        FilePath = filePath;
        StatusCode = statusCode;
    }

    /// <summary>
    /// File path that failed Authenticode validation, when available.
    /// </summary>
    public string? FilePath { get; }

    /// <summary>
    /// Native verification status code returned by the platform validator.
    /// </summary>
    public int StatusCode { get; }
}
