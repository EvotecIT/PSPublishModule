using System.DirectoryServices.Protocols;
using System.Net;
using System.Text;

namespace PowerForge;

/// <summary>Executes a closed, typed LDAP operation family without a PowerShell runtime.</summary>
public sealed class PowerShellDirectoryProviderAdapter
{
    /// <summary>Portable directory-provider capability contract.</summary>
    public static PowerShellDirectoryProviderContract Contract { get; } = new()
    {
        Operations = (PowerShellDirectoryOperation[])Enum.GetValues(typeof(PowerShellDirectoryOperation)),
        Transports = new[] { PowerShellDirectoryTransport.Ldap },
        Authentication = new[] { PowerShellDirectoryAuthentication.Negotiate },
        AcceptsRuntimeSession = true,
        SupportsCancellationAfterInitialization = true,
        DeterministicCleanup = true
    };

    /// <summary>Executes one LDAP request and deterministically releases adapter-owned state.</summary>
    public PowerShellDirectoryResult Execute(PowerShellDirectoryRequest request, CancellationToken cancellationToken = default)
    {
        if (request is null) throw new ArgumentNullException(nameof(request));
        Validate(request);
        cancellationToken.ThrowIfCancellationRequested();
        var ownsConnection = request.Session is null;
        var connection = request.Session?.Connection ?? CreateConnection(request, cancellationToken);
        try
        {
            var result = request.Operation == PowerShellDirectoryOperation.Search || request.Operation == PowerShellDirectoryOperation.Read
                ? ExecuteSearch(connection, request, cancellationToken)
                : ToPortableResult(request.Operation, Send(connection, CreateRequest(request), request.TimeoutSeconds, cancellationToken));
            result.OwnedConnectionDisposed = ownsConnection;
            return result;
        }
        catch (OperationCanceledException) when (request.Session is not null)
        {
            request.Session.Dispose();
            throw;
        }
        finally
        {
            if (ownsConnection) connection.Dispose();
        }
    }

    /// <summary>Opens a reusable session bound to the qualified LDAP/Negotiate provider profile.</summary>
    public PowerShellDirectorySession OpenSession(
        string hostName,
        int port = 389,
        int timeoutSeconds = 30,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(hostName))
            throw new ArgumentException("An LDAP host name is required.", nameof(hostName));
        var request = new PowerShellDirectoryRequest
        {
            Operation = PowerShellDirectoryOperation.Search,
            HostName = hostName,
            Port = port,
            Transport = PowerShellDirectoryTransport.Ldap,
            Authentication = PowerShellDirectoryAuthentication.Negotiate,
            TimeoutSeconds = timeoutSeconds
        };
        ValidateConnection(request);
        cancellationToken.ThrowIfCancellationRequested();
        return new PowerShellDirectorySession(CreateConnection(request, cancellationToken));
    }

    internal static DirectoryRequest CreateRequest(PowerShellDirectoryRequest request)
    {
        switch (request.Operation)
        {
            case PowerShellDirectoryOperation.Search:
                return CreateSearchRequest(request, request.BaseDistinguishedName, request.Scope);
            case PowerShellDirectoryOperation.Read:
                return CreateSearchRequest(request, request.DistinguishedName, PowerShellDirectorySearchScope.Base);
            case PowerShellDirectoryOperation.Add:
            {
                var add = new AddRequest(request.DistinguishedName);
                foreach (var attribute in request.Attributes ?? Array.Empty<PowerShellDirectoryAttribute>())
                    add.Attributes.Add(new DirectoryAttribute(attribute.Name, ToRequestValues(attribute.Values)));
                return add;
            }
            case PowerShellDirectoryOperation.Modify:
            {
                var modify = new ModifyRequest(request.DistinguishedName);
                foreach (var item in request.Modifications ?? Array.Empty<PowerShellDirectoryModification>())
                {
                    var modification = new DirectoryAttributeModification
                    {
                        Name = item.Name,
                        Operation = ToDirectoryOperation(item.Operation)
                    };
                    foreach (var value in ToRequestValues(item.Values)) AddValue(modification, value);
                    modify.Modifications.Add(modification);
                }
                return modify;
            }
            case PowerShellDirectoryOperation.Delete:
                return new DeleteRequest(request.DistinguishedName);
            case PowerShellDirectoryOperation.ModifyDistinguishedName:
                return new ModifyDNRequest(
                    request.DistinguishedName,
                    string.IsNullOrWhiteSpace(request.NewParentDistinguishedName) ? null : request.NewParentDistinguishedName,
                    request.NewRelativeDistinguishedName)
                {
                    DeleteOldRdn = request.DeleteOldRelativeDistinguishedName
                };
            case PowerShellDirectoryOperation.Compare:
                return CreateCompareRequest(request);
            default:
                throw new ArgumentOutOfRangeException(nameof(request.Operation), request.Operation, "Unsupported directory operation.");
        }
    }

    private static PowerShellDirectoryResult ExecuteSearch(
        LdapConnection connection,
        PowerShellDirectoryRequest request,
        CancellationToken cancellationToken)
    {
        var entries = new List<PowerShellDirectoryEntry>();
        var pageSize = request.Operation == PowerShellDirectoryOperation.Read ? 0 : request.PageSize;
        byte[] cookie = Array.Empty<byte>();
        DirectoryResponse? lastResponse = null;
        PowerShellDirectoryResult? result = null;
        var completed = false;
        try
        {
            do
            {
                cancellationToken.ThrowIfCancellationRequested();
                var search = (SearchRequest)CreateRequest(request);
                if (pageSize > 0)
                    search.Controls.Add(new PageResultRequestControl(pageSize) { Cookie = cookie, IsCritical = false });
                var response = (SearchResponse)Send(connection, search, request.TimeoutSeconds, cancellationToken);
                lastResponse = response;
                cookie = response.Controls.OfType<PageResultResponseControl>().FirstOrDefault()?.Cookie ?? Array.Empty<byte>();
                foreach (SearchResultEntry entry in response.Entries)
                {
                    entries.Add(ToPortableEntry(entry));
                    if (entries.Count >= request.ResultLimit) break;
                }
                if (entries.Count >= request.ResultLimit) break;
                completed = pageSize == 0 || cookie.Length == 0;
            } while (!completed);

            result = ToPortableResult(request.Operation, lastResponse!);
            result.Entries = entries.ToArray();
            return result;
        }
        finally
        {
            if (!completed && cookie.Length > 0)
            {
                var abandoned = TryAbandonPage(connection, request, cookie, out var connectionDisposed);
                if (result is not null)
                {
                    result.PagingCookieAbandoned = abandoned;
                    result.PagingConnectionDisposed = connectionDisposed;
                }
            }
        }
    }

    private static SearchRequest CreateSearchRequest(
        PowerShellDirectoryRequest request,
        string distinguishedName,
        PowerShellDirectorySearchScope scope)
    {
        var search = new SearchRequest(
            distinguishedName,
            request.Filter,
            ToSearchScope(scope),
            request.AttributeNames ?? Array.Empty<string>());
        if (request.PageSize == 0 || scope == PowerShellDirectorySearchScope.Base)
            search.SizeLimit = request.ResultLimit;
        return search;
    }

    private static LdapConnection CreateConnection(PowerShellDirectoryRequest request, CancellationToken cancellationToken)
    {
        var port = request.Port > 0
            ? request.Port
            : request.Transport == PowerShellDirectoryTransport.Ldaps ? 636 : 389;
        var identifier = new LdapDirectoryIdentifier(request.HostName, port, fullyQualifiedDnsHostName: false, connectionless: false);
        var authentication = ToAuthentication(request.Authentication);
        var connection = request.Credential is null
            ? new LdapConnection(identifier) { AuthType = authentication }
            : new LdapConnection(
                identifier,
                new NetworkCredential(request.Credential.UserName, request.Credential.Password, request.Credential.Domain),
                authentication);
        try
        {
            connection.AutoBind = true;
            connection.Timeout = TimeSpan.FromSeconds(request.TimeoutSeconds);
            connection.SessionOptions.ProtocolVersion = 3;
            connection.SessionOptions.SendTimeout = TimeSpan.FromSeconds(request.TimeoutSeconds);
            connection.SessionOptions.ReferralChasing = request.FollowReferrals
                ? ReferralChasingOptions.All
                : ReferralChasingOptions.None;
            connection.SessionOptions.SecureSocketLayer = request.Transport == PowerShellDirectoryTransport.Ldaps;
            cancellationToken.ThrowIfCancellationRequested();
            return connection;
        }
        catch
        {
            connection.Dispose();
            throw;
        }
    }

    private static bool TryAbandonPage(
        LdapConnection connection,
        PowerShellDirectoryRequest request,
        byte[] cookie,
        out bool connectionDisposed)
    {
        connectionDisposed = false;
        try
        {
            var abandon = (SearchRequest)CreateRequest(request);
            abandon.Controls.Add(new DirectoryControl(
                "1.2.840.113556.1.4.319",
                BerConverter.Encode("{io}", 0, cookie),
                isCritical: false,
                serverSide: true));
            _ = Send(connection, abandon, Math.Min(request.TimeoutSeconds, 5), CancellationToken.None);
            return true;
        }
        catch (Exception exception) when (
            exception is LdapException ||
            exception is DirectoryOperationException ||
            exception is InvalidOperationException)
        {
            try { connection.Dispose(); }
            catch (ObjectDisposedException) { }
            connectionDisposed = true;
            return false;
        }
    }

    private static DirectoryResponse Send(
        LdapConnection connection,
        DirectoryRequest request,
        int timeoutSeconds,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        var asyncResult = connection.BeginSendRequest(
            request,
            TimeSpan.FromSeconds(timeoutSeconds),
            PartialResultProcessing.NoPartialResultSupport,
            callback: null,
            state: null);
        using (cancellationToken.Register(() =>
               {
                   try { connection.Abort(asyncResult); }
                   catch (LdapException) { }
                   catch (ObjectDisposedException) { }
               }))
        {
            try
            {
                var response = connection.EndSendRequest(asyncResult);
                cancellationToken.ThrowIfCancellationRequested();
                return response;
            }
            catch (Exception exception) when (
                cancellationToken.IsCancellationRequested &&
                (exception is LdapException ||
                 exception is DirectoryOperationException ||
                 exception is InvalidOperationException ||
                 exception is ObjectDisposedException))
            {
                throw new OperationCanceledException(cancellationToken);
            }
        }
    }

    private static PowerShellDirectoryResult ToPortableResult(PowerShellDirectoryOperation operation, DirectoryResponse response)
        => new()
        {
            Operation = operation,
            ResultCode = response.ResultCode.ToString(),
            MatchedDistinguishedName = response.MatchedDN ?? string.Empty,
            DiagnosticMessage = response.ErrorMessage ?? string.Empty,
            Compared = operation == PowerShellDirectoryOperation.Compare
                ? response.ResultCode == ResultCode.CompareTrue
                : null
        };

    private static PowerShellDirectoryEntry ToPortableEntry(SearchResultEntry entry)
    {
        var attributes = new List<PowerShellDirectoryAttribute>();
        foreach (string name in entry.Attributes.AttributeNames)
        {
            var values = entry.Attributes[name].GetValues(typeof(byte[]))
                .Cast<byte[]>()
                .Select(ToPortableValue)
                .ToArray();
            attributes.Add(new PowerShellDirectoryAttribute { Name = name, Values = values });
        }
        return new PowerShellDirectoryEntry
        {
            DistinguishedName = entry.DistinguishedName ?? string.Empty,
            Attributes = attributes.OrderBy(static attribute => attribute.Name, StringComparer.OrdinalIgnoreCase).ToArray()
        };
    }

    private static PowerShellDirectoryValue ToPortableValue(byte[] bytes)
    {
        var text = Encoding.UTF8.GetString(bytes);
        var roundTrips = Encoding.UTF8.GetBytes(text).SequenceEqual(bytes);
        return new PowerShellDirectoryValue
        {
            Text = roundTrips ? text : string.Empty,
            Base64 = Convert.ToBase64String(bytes),
            IsBinary = !roundTrips
        };
    }

    private static object[] ToRequestValues(IEnumerable<PowerShellDirectoryValue> values)
        => (values ?? Array.Empty<PowerShellDirectoryValue>()).Select(ToRequestValue).ToArray();

    private static object ToRequestValue(PowerShellDirectoryValue value)
    {
        if (value is null) throw new ArgumentException("Directory values cannot contain null entries.");
        if (!value.IsBinary) return value.Text ?? string.Empty;
        if (string.IsNullOrWhiteSpace(value.Base64)) throw new ArgumentException("A binary directory value requires Base64 data.");
        try { return Convert.FromBase64String(value.Base64); }
        catch (FormatException exception) { throw new ArgumentException("A binary directory value contains invalid Base64 data.", exception); }
    }

    private static void AddValue(DirectoryAttribute attribute, object value)
    {
        if (value is byte[] bytes) attribute.Add(bytes);
        else attribute.Add((string)value);
    }

    private static CompareRequest CreateCompareRequest(PowerShellDirectoryRequest request)
    {
        var value = ToRequestValue(request.CompareValue!);
        return value is byte[] bytes
            ? new CompareRequest(request.DistinguishedName, request.CompareAttributeName, bytes)
            : new CompareRequest(request.DistinguishedName, request.CompareAttributeName, (string)value);
    }

    private static SearchScope ToSearchScope(PowerShellDirectorySearchScope scope)
        => scope switch
        {
            PowerShellDirectorySearchScope.Base => SearchScope.Base,
            PowerShellDirectorySearchScope.OneLevel => SearchScope.OneLevel,
            PowerShellDirectorySearchScope.Subtree => SearchScope.Subtree,
            _ => throw new ArgumentOutOfRangeException(nameof(scope), scope, "Unsupported search scope.")
        };

    private static DirectoryAttributeOperation ToDirectoryOperation(PowerShellDirectoryModificationOperation operation)
        => operation switch
        {
            PowerShellDirectoryModificationOperation.Add => DirectoryAttributeOperation.Add,
            PowerShellDirectoryModificationOperation.Delete => DirectoryAttributeOperation.Delete,
            PowerShellDirectoryModificationOperation.Replace => DirectoryAttributeOperation.Replace,
            _ => throw new ArgumentOutOfRangeException(nameof(operation), operation, "Unsupported modification operation.")
        };

    private static AuthType ToAuthentication(PowerShellDirectoryAuthentication authentication)
        => authentication switch
        {
            PowerShellDirectoryAuthentication.Negotiate => AuthType.Negotiate,
            PowerShellDirectoryAuthentication.Kerberos => AuthType.Kerberos,
            PowerShellDirectoryAuthentication.Ntlm => AuthType.Ntlm,
            PowerShellDirectoryAuthentication.Basic => AuthType.Basic,
            PowerShellDirectoryAuthentication.Anonymous => AuthType.Anonymous,
            _ => throw new ArgumentOutOfRangeException(nameof(authentication), authentication, "Unsupported directory authentication.")
        };

    private static void Validate(PowerShellDirectoryRequest request)
    {
        if (!Enum.IsDefined(typeof(PowerShellDirectoryOperation), request.Operation))
            throw new ArgumentOutOfRangeException(nameof(request.Operation));
        if (!Enum.IsDefined(typeof(PowerShellDirectoryTransport), request.Transport))
            throw new ArgumentOutOfRangeException(nameof(request.Transport));
        if (!Enum.IsDefined(typeof(PowerShellDirectoryAuthentication), request.Authentication))
            throw new ArgumentOutOfRangeException(nameof(request.Authentication));
        if (request.Session is not null &&
            (!string.IsNullOrWhiteSpace(request.HostName) ||
             request.Port != 0 ||
             request.Credential is not null))
            throw new InvalidOperationException("A provider-created LDAP session is mutually exclusive with host, port, and credential options.");
        ValidateConnection(request);
        if (request.Session is null && string.IsNullOrWhiteSpace(request.HostName))
            throw new ArgumentException("An LDAP host name or provider-created session is required.", nameof(request));

        switch (request.Operation)
        {
            case PowerShellDirectoryOperation.Search:
                Require(request.Filter, nameof(request.Filter));
                break;
            case PowerShellDirectoryOperation.Read:
                Require(request.Filter, nameof(request.Filter));
                break;
            case PowerShellDirectoryOperation.Add:
                Require(request.DistinguishedName, nameof(request.DistinguishedName));
                if (request.Attributes is null || request.Attributes.Length == 0) throw new ArgumentException("Add requires at least one attribute.", nameof(request));
                ValidateAttributes(request.Attributes);
                break;
            case PowerShellDirectoryOperation.Modify:
                Require(request.DistinguishedName, nameof(request.DistinguishedName));
                if (request.Modifications is null || request.Modifications.Length == 0) throw new ArgumentException("Modify requires at least one modification.", nameof(request));
                foreach (var modification in request.Modifications)
                {
                    if (modification is null) throw new ArgumentException("Modify cannot contain null modifications.", nameof(request));
                    Require(modification.Name, nameof(modification.Name));
                    _ = ToDirectoryOperation(modification.Operation);
                    foreach (var value in modification.Values ?? Array.Empty<PowerShellDirectoryValue>()) _ = ToRequestValue(value);
                }
                break;
            case PowerShellDirectoryOperation.Delete:
                Require(request.DistinguishedName, nameof(request.DistinguishedName));
                break;
            case PowerShellDirectoryOperation.ModifyDistinguishedName:
                Require(request.DistinguishedName, nameof(request.DistinguishedName));
                Require(request.NewRelativeDistinguishedName, nameof(request.NewRelativeDistinguishedName));
                break;
            case PowerShellDirectoryOperation.Compare:
                Require(request.DistinguishedName, nameof(request.DistinguishedName));
                Require(request.CompareAttributeName, nameof(request.CompareAttributeName));
                if (request.CompareValue is null) throw new ArgumentException("Compare requires one value.", nameof(request));
                break;
        }
        foreach (var name in request.AttributeNames ?? Array.Empty<string>()) Require(name, nameof(request.AttributeNames));
    }

    private static void ValidateConnection(PowerShellDirectoryRequest request)
    {
        if (!Contract.Transports.Contains(request.Transport))
            throw new NotSupportedException($"Directory transport '{request.Transport}' is not target-qualified by this provider version.");
        if (!Contract.Authentication.Contains(request.Authentication))
            throw new NotSupportedException($"Directory authentication '{request.Authentication}' is not target-qualified by this provider version.");
        if (request.Port < 0 || request.Port > 65535) throw new ArgumentOutOfRangeException(nameof(request.Port));
        if (request.Port != 0 && request.Port != 389)
            throw new NotSupportedException($"Directory port '{request.Port}' is not target-qualified by this provider version.");
        if (request.FollowReferrals)
            throw new NotSupportedException("LDAP referral chasing is not target-qualified by this provider version.");
        if (request.Credential is not null)
            throw new NotSupportedException("Explicit LDAP credentials are not target-qualified by this provider version.");
        if (request.TimeoutSeconds < 1 || request.TimeoutSeconds > 3600) throw new ArgumentOutOfRangeException(nameof(request.TimeoutSeconds));
        if (request.PageSize < 0 || request.PageSize > 1000) throw new ArgumentOutOfRangeException(nameof(request.PageSize));
        if (request.ResultLimit < 1 || request.ResultLimit > 100000) throw new ArgumentOutOfRangeException(nameof(request.ResultLimit));
        if (request.Authentication == PowerShellDirectoryAuthentication.Basic && request.Transport == PowerShellDirectoryTransport.Ldap)
            throw new InvalidOperationException("Basic LDAP authentication requires LDAPS or StartTLS.");
        if (request.Authentication == PowerShellDirectoryAuthentication.Basic && request.Credential is null && request.Session is null)
            throw new InvalidOperationException("Basic LDAP authentication requires a runtime credential.");
        if (request.Authentication == PowerShellDirectoryAuthentication.Anonymous && request.Credential is not null)
            throw new InvalidOperationException("Anonymous LDAP authentication cannot carry a credential.");

    }

    private static void ValidateAttributes(IEnumerable<PowerShellDirectoryAttribute> attributes)
    {
        foreach (var attribute in attributes)
        {
            if (attribute is null) throw new ArgumentException("Add cannot contain null attributes.");
            Require(attribute.Name, nameof(attribute.Name));
            if (attribute.Values is null || attribute.Values.Length == 0) throw new ArgumentException("Add attributes require at least one value.");
            foreach (var value in attribute.Values) _ = ToRequestValue(value);
        }
    }

    private static void Require(string value, string property)
    {
        if (string.IsNullOrWhiteSpace(value)) throw new ArgumentException($"Directory property '{property}' is required.", property);
    }
}
