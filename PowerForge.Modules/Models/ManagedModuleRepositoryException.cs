using System;

namespace PowerForge;

/// <summary>
/// Describes a managed module repository failure with provider-neutral context and remediation guidance.
/// </summary>
public sealed class ManagedModuleRepositoryException : InvalidOperationException
{
    /// <summary>
    /// Creates a repository exception.
    /// </summary>
    public ManagedModuleRepositoryException(
        string operation,
        string repositoryName,
        string repositorySource,
        string message,
        string remediation,
        int? statusCode = null,
        Exception? innerException = null)
        : base(FormatMessage(operation, repositoryName, message, remediation, statusCode), innerException)
    {
        Operation = operation;
        RepositoryName = repositoryName;
        RepositorySource = repositorySource;
        Remediation = remediation;
        StatusCode = statusCode;
    }

    /// <summary>Repository operation that failed.</summary>
    public string Operation { get; }

    /// <summary>Friendly repository name.</summary>
    public string RepositoryName { get; }

    /// <summary>Repository source URI or local path.</summary>
    public string RepositorySource { get; }

    /// <summary>HTTP status code when the failure came from a repository response.</summary>
    public int? StatusCode { get; }

    /// <summary>Actionable remediation guidance suitable for cmdlet errors and logs.</summary>
    public string Remediation { get; }

    private static string FormatMessage(
        string operation,
        string repositoryName,
        string message,
        string remediation,
        int? statusCode)
    {
        var status = statusCode.HasValue ? $" StatusCode={statusCode.Value}." : string.Empty;
        return $"Managed module repository operation '{operation}' failed for repository '{repositoryName}'. {message}{status} Remediation: {remediation}";
    }
}
