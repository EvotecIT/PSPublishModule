using System.Collections;
using System.Globalization;
using Microsoft.Management.Infrastructure;
using Microsoft.Management.Infrastructure.Options;

namespace PowerForge;

/// <summary>Typed CIM/MI adapter with explicit transport, authentication, timeout, cancellation, and cleanup.</summary>
public sealed class PowerShellManagementProviderAdapter
{
    /// <summary>Canonical adapter contract implemented by this runtime owner.</summary>
    public static PowerShellManagementProviderContract Contract { get; } = new()
    {
        ProviderId = "powerforge.management.cim",
        ProviderVersion = "2.0",
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
        ValidateRequest(request);
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

    private static void ValidateRequest(PowerShellManagementRequest request)
    {
        if (!Enum.IsDefined(typeof(PowerShellManagementOperation), request.Operation))
            throw new ArgumentOutOfRangeException(nameof(request.Operation));
        if (!Enum.IsDefined(typeof(PowerShellManagementTransport), request.Transport))
            throw new ArgumentOutOfRangeException(nameof(request.Transport));
        if (!Enum.IsDefined(typeof(PowerShellManagementAuthentication), request.Authentication))
            throw new ArgumentOutOfRangeException(nameof(request.Authentication));
        if (request.TimeoutSeconds <= 0) throw new ArgumentOutOfRangeException(nameof(request.TimeoutSeconds));
        if (request.MaximumResults <= 0) throw new ArgumentOutOfRangeException(nameof(request.MaximumResults));
        if (request.ResultLimit < 0) throw new ArgumentOutOfRangeException(nameof(request.ResultLimit));
        if (request.Session is not null &&
            (request.Credential is not null ||
             request.Transport != PowerShellManagementTransport.Default ||
             request.Authentication != PowerShellManagementAuthentication.Default ||
             !string.IsNullOrWhiteSpace(request.ComputerName)))
            throw new ArgumentException("A caller-owned CIM session cannot be combined with session-creation transport, authentication, credential, or computer options.", nameof(request));
        if (request.Authentication != PowerShellManagementAuthentication.Default && request.Credential is null)
            throw new ArgumentException("An explicit management authentication mechanism requires a runtime credential.", nameof(request));
        if (request.Transport == PowerShellManagementTransport.Dcom &&
            request.Authentication is PowerShellManagementAuthentication.Basic or PowerShellManagementAuthentication.CredSsp)
            throw new ArgumentException($"Authentication '{request.Authentication}' is not supported with the DCOM management transport.", nameof(request));
        EnsureUnique(request.Properties, nameof(request.Properties));
        EnsureUnique(request.MethodParameters, nameof(request.MethodParameters));
        if (request.InstanceReference is not null) EnsureUnique(request.InstanceReference.Keys, nameof(request.InstanceReference.Keys));
    }

    private static PowerShellManagementResult Execute(
        CimSession session,
        PowerShellManagementRequest request,
        CimOperationOptions options,
        CancellationToken cancellationToken)
    {
        var result = new PowerShellManagementResult { Operation = request.Operation };
        try
        {
            switch (request.Operation)
            {
            case PowerShellManagementOperation.Query:
                Require(request.Query, nameof(request.Query));
                result.Instances = ToPortableInstances(
                    Limit(session.QueryInstances(request.Namespace, request.QueryDialect, request.Query, options), request.ResultLimit));
                break;
            case PowerShellManagementOperation.Enumerate:
                Require(request.ClassName, nameof(request.ClassName));
                result.Instances = ToPortableInstances(
                    Limit(session.EnumerateInstances(request.Namespace, request.ClassName, options), request.ResultLimit));
                break;
            case PowerShellManagementOperation.Get:
            {
                using var target = RequireTargetInstance(request, includeProperties: false);
                result.Instances = new[]
                {
                    ToPortableOwned(session.GetInstance(
                        request.Namespace,
                        target.Instance,
                        options))
                };
                break;
            }
            case PowerShellManagementOperation.Create:
            {
                Require(request.ClassName, nameof(request.ClassName));
                using var input = CreateInstance(request, includeProperties: true);
                var created = session.CreateInstance(request.Namespace, input, options);
                result.Instances = new[] { ToPortableOwned(created) };
                break;
            }
            case PowerShellManagementOperation.Modify:
            {
                using var target = RequireTargetInstance(request, includeProperties: true);
                var modified = session.ModifyInstance(request.Namespace, target.Instance, options);
                result.Instances = new[] { ToPortableOwned(modified) };
                break;
            }
            case PowerShellManagementOperation.Delete:
            {
                using var target = RequireTargetInstance(request, includeProperties: false);
                session.DeleteInstance(request.Namespace, target.Instance, options);
                break;
            }
            case PowerShellManagementOperation.InvokeMethod:
            {
                Require(request.MethodName, nameof(request.MethodName));
                using var parameters = new CimMethodParametersCollection();
                foreach (var property in request.MethodParameters.OrderBy(static property => property.Name, StringComparer.OrdinalIgnoreCase))
                {
                    var cimType = ParseCimType(property.TypeName);
                    parameters.Add(CimMethodParameter.Create(property.Name, FromPortable(property, cimType), cimType, CimFlags.In));
                }
                using var target = request.Instance is null && request.InstanceReference is null
                    ? null
                    : RequireTargetInstance(request, includeProperties: false);
                using var method = target is null
                    ? session.InvokeMethod(request.Namespace, Require(request.ClassName, nameof(request.ClassName)), request.MethodName, parameters, options)
                    : session.InvokeMethod(request.Namespace, target.Instance, request.MethodName, parameters, options);
                result.ReturnValue = method.ReturnValue is null ? null : ToPortable(method.ReturnValue.Name, method.ReturnValue.Value, method.ReturnValue.CimType);
                result.OutputParameters = method.OutParameters
                    .Select(static parameter => ToPortable(parameter.Name, parameter.Value, parameter.CimType))
                    .OrderBy(static property => property.Name, StringComparer.OrdinalIgnoreCase)
                    .ToArray();
                break;
            }
            case PowerShellManagementOperation.Association:
            {
                using var target = RequireTargetInstance(request, includeProperties: false);
                result.Instances = ToPortableInstances(Limit(session.EnumerateAssociatedInstances(
                        request.Namespace,
                        target.Instance,
                        request.AssociationClassName,
                        request.ClassName,
                        request.SourceRole,
                        request.ResultRole,
                        options), request.ResultLimit));
                break;
            }
            case PowerShellManagementOperation.Subscription:
                Require(request.Query, nameof(request.Query));
                result.Instances = ToPortableInstances(session.Subscribe(request.Namespace, request.QueryDialect, request.Query, options)
                    .Take(request.MaximumResults)
                    .Select(static subscription => subscription.Instance));
                break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(request.Operation));
            }
            cancellationToken.ThrowIfCancellationRequested();
            return result;
        }
        catch
        {
            result.Dispose();
            throw;
        }
    }

    private static IEnumerable<T> Limit<T>(IEnumerable<T> source, int resultLimit)
        => resultLimit == 0 ? source : source.Take(resultLimit);

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
                request.Credential.Domain,
                request.Credential.UserName,
                request.Credential.Password));
        }
        try
        {
            return options is null
                ? CimSession.Create(string.IsNullOrWhiteSpace(request.ComputerName) ? null : request.ComputerName)
                : CimSession.Create(string.IsNullOrWhiteSpace(request.ComputerName) ? null : request.ComputerName, options);
        }
        finally
        {
            options?.Dispose();
        }
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

    private static CimInstanceLease RequireTargetInstance(PowerShellManagementRequest request, bool includeProperties)
        => request.Instance is null
            ? new CimInstanceLease(CreateInstance(request, includeProperties), ownsInstance: true)
            : new CimInstanceLease(request.Instance, ownsInstance: false);

    private static CimInstance CreateInstance(PowerShellManagementRequest request, bool includeProperties)
    {
        var reference = request.InstanceReference;
        var className = Require(reference?.ClassName ?? request.ClassName, nameof(request.ClassName));
        var namespaceName = string.IsNullOrWhiteSpace(reference?.Namespace) ? request.Namespace : reference!.Namespace;
        var instance = new CimInstance(className, namespaceName);
        try
        {
            foreach (var property in reference?.Keys ?? Array.Empty<PowerShellManagementProperty>())
                AddProperty(instance, property, CimFlags.Key);
            if (includeProperties)
            {
                foreach (var property in request.Properties.OrderBy(static property => property.Name, StringComparer.OrdinalIgnoreCase))
                    if (!instance.CimInstanceProperties.Any(existing => existing.Name.Equals(property.Name, StringComparison.OrdinalIgnoreCase)))
                        AddProperty(instance, property, CimFlags.Property);
            }
            return instance;
        }
        catch
        {
            instance.Dispose();
            throw;
        }
    }

    private static void AddProperty(CimInstance instance, PowerShellManagementProperty property, CimFlags flags)
    {
        Require(property.Name, nameof(property.Name));
        var cimType = ParseCimType(property.TypeName);
        instance.CimInstanceProperties.Add(CimProperty.Create(property.Name, FromPortable(property, cimType), cimType, flags));
    }

    private static object? FromPortable(PowerShellManagementProperty property, CimType cimType)
    {
        if (property.IsNull) return null;
        var isArray = property.IsArray || cimType.ToString().EndsWith("Array", StringComparison.Ordinal);
        if (!isArray) return ParseScalar(property.Value, cimType);
        var cimTypeName = cimType.ToString();
        var scalarTypeName = cimTypeName.EndsWith("Array", StringComparison.Ordinal)
            ? cimTypeName.Substring(0, cimTypeName.Length - "Array".Length)
            : cimTypeName;
        if (!Enum.TryParse(scalarTypeName, ignoreCase: false, out CimType scalarType))
            throw new ArgumentException($"Unsupported CIM array type '{cimType}'.", nameof(property));
        var values = property.Values ?? Array.Empty<string>();
        var elementType = GetClrType(scalarType);
        var array = Array.CreateInstance(elementType, values.Length);
        for (var index = 0; index < values.Length; index++) array.SetValue(ParseScalar(values[index], scalarType), index);
        return array;
    }

    private static object ParseScalar(string value, CimType cimType)
        => cimType switch
        {
            CimType.Boolean => bool.Parse(value),
            CimType.UInt8 => byte.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture),
            CimType.SInt8 => sbyte.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture),
            CimType.UInt16 => ushort.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture),
            CimType.SInt16 => short.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture),
            CimType.UInt32 => uint.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture),
            CimType.SInt32 => int.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture),
            CimType.UInt64 => ulong.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture),
            CimType.SInt64 => long.Parse(value, NumberStyles.Integer, CultureInfo.InvariantCulture),
            CimType.Real32 => float.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture),
            CimType.Real64 => double.Parse(value, NumberStyles.Float, CultureInfo.InvariantCulture),
            CimType.Char16 => string.IsNullOrEmpty(value) ? '\0' : value[0],
            CimType.DateTime => DateTime.Parse(value, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind),
            CimType.String => value,
            _ => throw new ArgumentException($"Portable input type '{cimType}' is not supported.")
        };

    private static Type GetClrType(CimType cimType)
        => cimType switch
        {
            CimType.Boolean => typeof(bool),
            CimType.UInt8 => typeof(byte),
            CimType.SInt8 => typeof(sbyte),
            CimType.UInt16 => typeof(ushort),
            CimType.SInt16 => typeof(short),
            CimType.UInt32 => typeof(uint),
            CimType.SInt32 => typeof(int),
            CimType.UInt64 => typeof(ulong),
            CimType.SInt64 => typeof(long),
            CimType.Real32 => typeof(float),
            CimType.Real64 => typeof(double),
            CimType.Char16 => typeof(char),
            CimType.DateTime => typeof(DateTime),
            CimType.String => typeof(string),
            _ => throw new ArgumentException($"Portable input type '{cimType}' is not supported.")
        };

    private static CimType ParseCimType(string value)
        => Enum.TryParse(value, ignoreCase: false, out CimType result) && Enum.IsDefined(typeof(CimType), result)
            ? result
            : throw new ArgumentException($"Unknown CIM type '{value}'.", nameof(value));

    private static PowerShellManagementInstance ToPortable(CimInstance instance)
    {
        var properties = instance.CimInstanceProperties
            .Select(static property => ToPortable(property.Name, property.Value, property.CimType))
            .OrderBy(static property => property.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var classKeyNames = instance.CimClass.CimClassProperties
            .Where(static property => property.Flags.HasFlag(CimFlags.Key) ||
                                      property.Qualifiers.Any(static qualifier =>
                                          qualifier.Name.Equals("Key", StringComparison.OrdinalIgnoreCase) &&
                                          qualifier.Value is true))
            .Select(static property => property.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);
        var keys = instance.CimInstanceProperties
            .Where(property => property.Flags.HasFlag(CimFlags.Key) || classKeyNames.Contains(property.Name))
            .Select(static property => ToPortable(property.Name, property.Value, property.CimType))
            .OrderBy(static property => property.Name, StringComparer.OrdinalIgnoreCase)
            .ToArray();
        var className = instance.CimSystemProperties.ClassName ?? string.Empty;
        var namespaceName = instance.CimSystemProperties.Namespace ?? string.Empty;
        return new PowerShellManagementInstance
        {
            ClassName = className,
            Namespace = namespaceName,
            ServerName = instance.CimSystemProperties.ServerName ?? string.Empty,
            Properties = properties,
            Reference = keys.Length == 0 ? null : new PowerShellManagementInstanceReference { ClassName = className, Namespace = namespaceName, Keys = keys },
            RuntimeInstance = instance
        };
    }

    private static PowerShellManagementInstance ToPortableOwned(CimInstance instance)
    {
        try
        {
            return ToPortable(instance);
        }
        catch
        {
            instance.Dispose();
            throw;
        }
    }

    internal static PowerShellManagementInstance[] ToPortableInstances(IEnumerable<CimInstance> instances)
    {
        var result = new List<PowerShellManagementInstance>();
        try
        {
            foreach (var instance in instances) result.Add(ToPortableOwned(instance));
            return result.ToArray();
        }
        catch
        {
            foreach (var instance in result) instance.Dispose();
            throw;
        }
    }

    private static PowerShellManagementProperty ToPortable(string name, object? value, CimType cimType)
    {
        var values = value is string || value is not IEnumerable sequence
            ? Array.Empty<string>()
            : sequence.Cast<object?>().Select(NormalizeValue).ToArray();
        return new PowerShellManagementProperty
        {
            Name = name ?? string.Empty,
            IsNull = value is null,
            IsArray = values.Length > 0 || cimType.ToString().EndsWith("Array", StringComparison.Ordinal),
            TypeName = cimType.ToString(),
            Value = value is IEnumerable && value is not string ? string.Empty : NormalizeValue(value),
            Values = values
        };
    }

    private static string NormalizeValue(object? value)
        => value switch
        {
            null => string.Empty,
            DateTime date => date.ToString("O", CultureInfo.InvariantCulture),
            _ => Convert.ToString(value, CultureInfo.InvariantCulture) ?? string.Empty
        };

    private static void EnsureUnique(IEnumerable<PowerShellManagementProperty>? properties, string name)
    {
        var duplicate = (properties ?? Array.Empty<PowerShellManagementProperty>())
            .GroupBy(static property => property.Name, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(static group => group.Count() > 1);
        if (duplicate is not null) throw new ArgumentException($"Management property '{duplicate.Key}' is declared more than once.", name);
    }

    private static string Require(string value, string property)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"A non-empty {property} is required.", property);
        return value.Trim();
    }

    private sealed class CimInstanceLease : IDisposable
    {
        internal CimInstanceLease(CimInstance instance, bool ownsInstance)
        {
            Instance = instance;
            _ownsInstance = ownsInstance;
        }

        private readonly bool _ownsInstance;
        internal CimInstance Instance { get; }

        public void Dispose()
        {
            if (_ownsInstance) Instance.Dispose();
        }
    }
}
