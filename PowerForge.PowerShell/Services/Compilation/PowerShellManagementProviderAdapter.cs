using System.Collections;
using System.Globalization;
using System.Management.Automation;
using System.Text.Json.Serialization;
using Microsoft.Management.Infrastructure;
using Microsoft.Management.Infrastructure.Options;

namespace PowerForge;

/// <summary>One runtime management-provider operation. Live session and credential values are excluded from serialization.</summary>
public sealed class PowerShellManagementRequest
{
    /// <summary>Requested generic operation.</summary>
    public PowerShellManagementOperation Operation { get; set; }

    /// <summary>Target computer, or empty for the local computer.</summary>
    public string ComputerName { get; set; } = string.Empty;

    /// <summary>Management namespace.</summary>
    public string Namespace { get; set; } = "root/cimv2";

    /// <summary>Management class.</summary>
    public string ClassName { get; set; } = string.Empty;

    /// <summary>Query dialect.</summary>
    public string QueryDialect { get; set; } = "WQL";

    /// <summary>Query expression or subscription expression.</summary>
    public string Query { get; set; } = string.Empty;

    /// <summary>Method name.</summary>
    public string MethodName { get; set; } = string.Empty;

    /// <summary>Association class name.</summary>
    public string AssociationClassName { get; set; } = string.Empty;

    /// <summary>Source role for association traversal.</summary>
    public string SourceRole { get; set; } = string.Empty;

    /// <summary>Result role for association traversal.</summary>
    public string ResultRole { get; set; } = string.Empty;

    /// <summary>Runtime transport.</summary>
    public PowerShellManagementTransport Transport { get; set; }

    /// <summary>Runtime authentication mechanism.</summary>
    public PowerShellManagementAuthentication Authentication { get; set; }

    /// <summary>Operation timeout in seconds.</summary>
    public int TimeoutSeconds { get; set; } = 120;

    /// <summary>Maximum subscription results returned by one bounded operation.</summary>
    public int MaximumResults { get; set; } = 1;

    /// <summary>Property values used by create and modify. Excluded from portable serialization.</summary>
    [JsonIgnore]
    public IDictionary<string, object?> Properties { get; set; } = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Method input values. Excluded from portable serialization.</summary>
    [JsonIgnore]
    public IDictionary<string, object?> MethodParameters { get; set; } = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);

    /// <summary>Existing live CIM instance for modify, delete, method, or association operations.</summary>
    [JsonIgnore]
    public CimInstance? Instance { get; set; }

    /// <summary>Existing live CIM session. The adapter does not dispose caller-owned sessions.</summary>
    [JsonIgnore]
    public CimSession? Session { get; set; }

    /// <summary>Runtime-only credential. It is never copied into results, locks, or diagnostic messages.</summary>
    [JsonIgnore]
    public PSCredential? Credential { get; set; }
}

/// <summary>One portable management property observation.</summary>
public sealed class PowerShellManagementProperty
{
    /// <summary>Property name.</summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>Normalized value text.</summary>
    public string Value { get; set; } = string.Empty;

    /// <summary>CIM or CLR type identity.</summary>
    public string TypeName { get; set; } = string.Empty;

    /// <summary>Whether the property is null.</summary>
    public bool IsNull { get; set; }
}

/// <summary>Portable management instance observation plus a runtime-only instance handle.</summary>
public sealed class PowerShellManagementInstance
{
    /// <summary>Management class name.</summary>
    public string ClassName { get; set; } = string.Empty;

    /// <summary>Management namespace.</summary>
    public string Namespace { get; set; } = string.Empty;

    /// <summary>Server name reported by the provider.</summary>
    public string ServerName { get; set; } = string.Empty;

    /// <summary>Stable, sorted property observations.</summary>
    public PowerShellManagementProperty[] Properties { get; set; } = Array.Empty<PowerShellManagementProperty>();

    /// <summary>Runtime-only CIM instance used for a subsequent typed operation.</summary>
    [JsonIgnore]
    public CimInstance? RuntimeInstance { get; set; }
}

/// <summary>Portable result from one typed management-provider operation.</summary>
public sealed class PowerShellManagementResult
{
    /// <summary>Completed operation.</summary>
    public PowerShellManagementOperation Operation { get; set; }

    /// <summary>Returned instances.</summary>
    public PowerShellManagementInstance[] Instances { get; set; } = Array.Empty<PowerShellManagementInstance>();

    /// <summary>Method return value.</summary>
    public PowerShellManagementProperty? ReturnValue { get; set; }

    /// <summary>Method output parameters.</summary>
    public PowerShellManagementProperty[] OutputParameters { get; set; } = Array.Empty<PowerShellManagementProperty>();

    /// <summary>Whether the adapter created and deterministically disposed its session.</summary>
    public bool OwnedSessionDisposed { get; set; }
}

/// <summary>Typed CIM/MI management adapter with explicit transport, authentication, timeout, cancellation, and cleanup.</summary>
public sealed class PowerShellManagementProviderAdapter
{
    /// <summary>Canonical adapter contract implemented by this runtime owner.</summary>
    public static PowerShellManagementProviderContract Contract { get; } = new()
    {
        ProviderId = "powerforge.management.cim",
        ProviderVersion = "1.0",
        Operations = Enum.GetValues(typeof(PowerShellManagementOperation)).Cast<PowerShellManagementOperation>().ToArray(),
        Transports = Enum.GetValues(typeof(PowerShellManagementTransport)).Cast<PowerShellManagementTransport>().ToArray(),
        Authentication = Enum.GetValues(typeof(PowerShellManagementAuthentication)).Cast<PowerShellManagementAuthentication>().ToArray(),
        AcceptsRuntimeSession = true,
        SupportsCancellation = true,
        DeterministicCleanup = true
    };

    /// <summary>Executes one management operation and disposes only sessions created by this adapter.</summary>
    public PowerShellManagementResult Execute(PowerShellManagementRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        if (request.TimeoutSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(request.TimeoutSeconds));
        if (request.MaximumResults <= 0) throw new ArgumentOutOfRangeException(nameof(request.MaximumResults));
        cancellationToken.ThrowIfCancellationRequested();
        var ownedSession = request.Session is null;
        CimSession? session = request.Session;
        try
        {
            session ??= CreateSession(request);
            using var options = new CimOperationOptions
            {
                Timeout = TimeSpan.FromSeconds(request.TimeoutSeconds),
                CancellationToken = cancellationToken
            };
            var result = Execute(session, request, options, cancellationToken);
            result.OwnedSessionDisposed = ownedSession;
            return result;
        }
        finally
        {
            if (ownedSession) session?.Dispose();
        }
    }

    private static PowerShellManagementResult Execute(
        CimSession session,
        PowerShellManagementRequest request,
        CimOperationOptions options,
        CancellationToken cancellationToken)
    {
        var result = new PowerShellManagementResult { Operation = request.Operation };
        switch (request.Operation)
        {
            case PowerShellManagementOperation.Query:
                Require(request.Query, nameof(request.Query));
                result.Instances = session.QueryInstances(request.Namespace, request.QueryDialect, request.Query, options)
                    .Select(ToPortable).ToArray();
                break;
            case PowerShellManagementOperation.Enumerate:
                Require(request.ClassName, nameof(request.ClassName));
                result.Instances = session.EnumerateInstances(request.Namespace, request.ClassName, options)
                    .Select(ToPortable).ToArray();
                break;
            case PowerShellManagementOperation.Create:
            {
                Require(request.ClassName, nameof(request.ClassName));
                var created = session.CreateInstance(request.Namespace, CreateInstance(request), options);
                result.Instances = new[] { ToPortable(created) };
                break;
            }
            case PowerShellManagementOperation.Modify:
            {
                var modified = session.ModifyInstance(request.Namespace, request.Instance ?? CreateInstance(request), options);
                result.Instances = new[] { ToPortable(modified) };
                break;
            }
            case PowerShellManagementOperation.Delete:
                session.DeleteInstance(request.Namespace, RequireInstance(request), options);
                break;
            case PowerShellManagementOperation.InvokeMethod:
            {
                Require(request.MethodName, nameof(request.MethodName));
                var parameters = new CimMethodParametersCollection();
                foreach (var pair in request.MethodParameters.OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase))
                    parameters.Add(CimMethodParameter.Create(pair.Key, pair.Value, CimFlags.In));
                var method = request.Instance is null
                    ? session.InvokeMethod(request.Namespace, Require(request.ClassName, nameof(request.ClassName)), request.MethodName, parameters, options)
                    : session.InvokeMethod(request.Namespace, request.Instance, request.MethodName, parameters, options);
                result.ReturnValue = method.ReturnValue is null ? null : ToPortable(method.ReturnValue.Name, method.ReturnValue.Value, method.ReturnValue.CimType.ToString());
                result.OutputParameters = method.OutParameters
                    .Select(static parameter => ToPortable(parameter.Name, parameter.Value, parameter.CimType.ToString()))
                    .OrderBy(static property => property.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                break;
            }
            case PowerShellManagementOperation.Association:
                result.Instances = session.EnumerateAssociatedInstances(
                        request.Namespace,
                        RequireInstance(request),
                        request.AssociationClassName,
                        request.ClassName,
                        request.SourceRole,
                        request.ResultRole,
                        options)
                    .Select(ToPortable).ToArray();
                break;
            case PowerShellManagementOperation.Subscription:
                Require(request.Query, nameof(request.Query));
                result.Instances = session.Subscribe(request.Namespace, request.QueryDialect, request.Query, options)
                    .Take(request.MaximumResults)
                    .Select(static subscription => ToPortable(subscription.Instance))
                    .ToArray();
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(request.Operation));
        }
        cancellationToken.ThrowIfCancellationRequested();
        return result;
    }

    private static CimSession CreateSession(PowerShellManagementRequest request)
    {
        CimSessionOptions? options = request.Transport switch
        {
            PowerShellManagementTransport.Dcom => new DComSessionOptions(),
            PowerShellManagementTransport.WsMan => new WSManSessionOptions(),
            _ => null
        };
        if (request.Credential is not null)
        {
            options ??= new WSManSessionOptions();
            options.AddDestinationCredentials(new CimCredential(
                ToPasswordAuthentication(request.Authentication),
                request.Credential.GetNetworkCredential().Domain,
                request.Credential.UserName,
                request.Credential.Password));
        }
        return options is null
            ? CimSession.Create(string.IsNullOrWhiteSpace(request.ComputerName) ? null : request.ComputerName)
            : CimSession.Create(string.IsNullOrWhiteSpace(request.ComputerName) ? null : request.ComputerName, options);
    }

    private static PasswordAuthenticationMechanism ToPasswordAuthentication(PowerShellManagementAuthentication authentication)
        => authentication switch
        {
            PowerShellManagementAuthentication.Basic => PasswordAuthenticationMechanism.Basic,
            PowerShellManagementAuthentication.Kerberos => PasswordAuthenticationMechanism.Kerberos,
            PowerShellManagementAuthentication.NtlmDomain => PasswordAuthenticationMechanism.NtlmDomain,
            PowerShellManagementAuthentication.CredSsp => PasswordAuthenticationMechanism.CredSsp,
            PowerShellManagementAuthentication.Negotiate => PasswordAuthenticationMechanism.Negotiate,
            _ => PasswordAuthenticationMechanism.Default
        };

    private static CimInstance CreateInstance(PowerShellManagementRequest request)
    {
        var instance = new CimInstance(Require(request.ClassName, nameof(request.ClassName)), request.Namespace);
        foreach (var pair in request.Properties.OrderBy(static pair => pair.Key, StringComparer.OrdinalIgnoreCase))
            instance.CimInstanceProperties.Add(CimProperty.Create(pair.Key, pair.Value, CimFlags.Property));
        return instance;
    }

    private static CimInstance RequireInstance(PowerShellManagementRequest request)
        => request.Instance ?? throw new ArgumentException($"Operation '{request.Operation}' requires a runtime CimInstance.", nameof(request));

    private static string Require(string value, string property)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"A non-empty {property} is required.", property);
        return value.Trim();
    }

    private static PowerShellManagementInstance ToPortable(CimInstance instance)
        => new()
        {
            ClassName = instance.CimSystemProperties.ClassName ?? string.Empty,
            Namespace = instance.CimSystemProperties.Namespace ?? string.Empty,
            ServerName = instance.CimSystemProperties.ServerName ?? string.Empty,
            Properties = instance.CimInstanceProperties
                .Select(static property => ToPortable(property.Name, property.Value, property.CimType.ToString()))
                .OrderBy(static property => property.Name, StringComparer.OrdinalIgnoreCase)
                .ToArray(),
            RuntimeInstance = instance
        };

    private static PowerShellManagementProperty ToPortable(string name, object? value, string typeName)
        => new()
        {
            Name = name ?? string.Empty,
            IsNull = value is null,
            TypeName = typeName ?? string.Empty,
            Value = NormalizeValue(value)
        };

    private static string NormalizeValue(object? value)
    {
        if (value is null) return string.Empty;
        if (value is string text) return text;
        if (value is IEnumerable sequence)
            return string.Join(",", sequence.Cast<object?>().Select(NormalizeValue));
        return Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty;
    }
}
