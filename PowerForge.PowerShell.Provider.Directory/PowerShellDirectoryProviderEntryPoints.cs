using System.Text.Json;
using System.Text.Json.Serialization;

namespace PowerForge;

/// <summary>Closed provider-ABI entry points for the typed LDAP operation family.</summary>
public static class PowerShellDirectoryProviderEntryPoints
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    /// <summary>Executes a typed LDAP search.</summary>
    public static string Search(string requestJson, CancellationToken cancellationToken) => Execute(requestJson, PowerShellDirectoryOperation.Search, cancellationToken);
    /// <summary>Reads one exact distinguished name.</summary>
    public static string Read(string requestJson, CancellationToken cancellationToken) => Execute(requestJson, PowerShellDirectoryOperation.Read, cancellationToken);
    /// <summary>Adds one LDAP entry.</summary>
    public static string Add(string requestJson, CancellationToken cancellationToken) => Execute(requestJson, PowerShellDirectoryOperation.Add, cancellationToken);
    /// <summary>Modifies one LDAP entry.</summary>
    public static string Modify(string requestJson, CancellationToken cancellationToken) => Execute(requestJson, PowerShellDirectoryOperation.Modify, cancellationToken);
    /// <summary>Deletes one LDAP entry.</summary>
    public static string Delete(string requestJson, CancellationToken cancellationToken) => Execute(requestJson, PowerShellDirectoryOperation.Delete, cancellationToken);
    /// <summary>Renames or moves one LDAP entry.</summary>
    public static string ModifyDistinguishedName(string requestJson, CancellationToken cancellationToken) => Execute(requestJson, PowerShellDirectoryOperation.ModifyDistinguishedName, cancellationToken);
    /// <summary>Compares one LDAP attribute value.</summary>
    public static string Compare(string requestJson, CancellationToken cancellationToken) => Execute(requestJson, PowerShellDirectoryOperation.Compare, cancellationToken);

    private static string Execute(string requestJson, PowerShellDirectoryOperation expectedOperation, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(requestJson)) throw new ArgumentException("A directory request JSON document is required.", nameof(requestJson));
        var request = JsonSerializer.Deserialize<PowerShellDirectoryRequest>(requestJson, JsonOptions)
            ?? throw new InvalidDataException("The directory request JSON document was empty.");
        if (request.Operation != expectedOperation)
            throw new InvalidOperationException($"Directory entry point '{expectedOperation}' cannot execute request operation '{request.Operation}'.");
        if (request.Session is not null || request.Credential is not null)
            throw new InvalidOperationException("Portable provider entry points cannot receive a live session or credential.");
        return JsonSerializer.Serialize(new PowerShellDirectoryProviderAdapter().Execute(request, cancellationToken), JsonOptions);
    }

    private static JsonSerializerOptions CreateJsonOptions()
    {
        var options = new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = false,
            WriteIndented = false
        };
        options.Converters.Add(new JsonStringEnumConverter());
        return options;
    }
}
