using System.Text.Json;
using System.Text.Json.Serialization;

namespace PowerForge;

/// <summary>Closed provider-ABI entry points for the typed CIM/MI operation family.</summary>
public static class PowerShellManagementProviderEntryPoints
{
    private static readonly JsonSerializerOptions JsonOptions = CreateJsonOptions();

    /// <summary>Executes a typed query request.</summary>
    public static string Query(string requestJson, CancellationToken cancellationToken) => Execute(requestJson, PowerShellManagementOperation.Query, cancellationToken);
    /// <summary>Executes a typed enumeration request.</summary>
    public static string Enumerate(string requestJson, CancellationToken cancellationToken) => Execute(requestJson, PowerShellManagementOperation.Enumerate, cancellationToken);
    /// <summary>Executes an exact typed instance lookup.</summary>
    public static string Get(string requestJson, CancellationToken cancellationToken) => Execute(requestJson, PowerShellManagementOperation.Get, cancellationToken);
    /// <summary>Executes a typed create request.</summary>
    public static string Create(string requestJson, CancellationToken cancellationToken) => Execute(requestJson, PowerShellManagementOperation.Create, cancellationToken);
    /// <summary>Executes a typed modify request.</summary>
    public static string Modify(string requestJson, CancellationToken cancellationToken) => Execute(requestJson, PowerShellManagementOperation.Modify, cancellationToken);
    /// <summary>Executes a typed delete request.</summary>
    public static string Delete(string requestJson, CancellationToken cancellationToken) => Execute(requestJson, PowerShellManagementOperation.Delete, cancellationToken);
    /// <summary>Executes a typed method-invocation request.</summary>
    public static string InvokeMethod(string requestJson, CancellationToken cancellationToken) => Execute(requestJson, PowerShellManagementOperation.InvokeMethod, cancellationToken);
    /// <summary>Executes a typed association request.</summary>
    public static string Association(string requestJson, CancellationToken cancellationToken) => Execute(requestJson, PowerShellManagementOperation.Association, cancellationToken);
    /// <summary>Executes a typed bounded subscription request.</summary>
    public static string Subscription(string requestJson, CancellationToken cancellationToken) => Execute(requestJson, PowerShellManagementOperation.Subscription, cancellationToken);

    private static string Execute(string requestJson, PowerShellManagementOperation expectedOperation, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(requestJson)) throw new ArgumentException("A management request JSON document is required.", nameof(requestJson));
        var request = JsonSerializer.Deserialize<PowerShellManagementRequest>(requestJson, JsonOptions)
            ?? throw new InvalidDataException("The management request JSON document was empty.");
        if (request.Operation != expectedOperation)
            throw new InvalidOperationException($"Management entry point '{expectedOperation}' cannot execute request operation '{request.Operation}'.");
        if (request.Session is not null || request.Credential is not null || request.Instance is not null)
            throw new InvalidOperationException("Portable provider entry points cannot receive a live session, credential, or CIM instance.");
        using var result = new PowerShellManagementProviderAdapter().Execute(request, cancellationToken);
        return JsonSerializer.Serialize(result, JsonOptions);
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
