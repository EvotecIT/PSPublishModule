using System.DirectoryServices.Protocols;
using System.Security;
using System.Text.Json;
using System.Text.Json.Serialization;
using PowerForge;
using Xunit;

namespace PowerForge.Tests;

[Trait("Category", "PowerShellCompilation")]
public sealed class PowerShellDirectoryProviderTests
{
    [Fact]
    public void DirectoryContractCoversOperationsTransportsAuthenticationAndCleanup()
    {
        var contract = PowerShellDirectoryProviderAdapter.Contract;

        Assert.Equal(Enum.GetValues<PowerShellDirectoryOperation>(), contract.Operations);
        Assert.Equal(new[] { PowerShellDirectoryTransport.Ldap }, contract.Transports);
        Assert.Equal(new[] { PowerShellDirectoryAuthentication.Negotiate }, contract.Authentication);
        Assert.True(contract.AcceptsRuntimeSession);
        Assert.True(contract.SupportsCancellationAfterInitialization);
        Assert.True(contract.DeterministicCleanup);
        Assert.Equal("PowerForge.Directory/1", contract.Serialization);
    }

    [Fact]
    public void DirectoryRequestFactoryBuildsEveryTypedLdapOperation()
    {
        var search = Assert.IsType<SearchRequest>(PowerShellDirectoryProviderAdapter.CreateRequest(Request(PowerShellDirectoryOperation.Search)));
        Assert.Equal(SearchScope.Subtree, search.Scope);
        Assert.Equal("(objectClass=*)", search.Filter);

        var readRequest = Request(PowerShellDirectoryOperation.Read);
        readRequest.DistinguishedName = "CN=Reader,DC=example,DC=test";
        var read = Assert.IsType<SearchRequest>(PowerShellDirectoryProviderAdapter.CreateRequest(readRequest));
        Assert.Equal(SearchScope.Base, read.Scope);

        var addRequest = Request(PowerShellDirectoryOperation.Add);
        addRequest.DistinguishedName = "CN=Created,DC=example,DC=test";
        addRequest.Attributes = new[]
        {
            new PowerShellDirectoryAttribute
            {
                Name = "objectClass",
                Values = new[] { Text("top"), Text("person") }
            },
            new PowerShellDirectoryAttribute
            {
                Name = "objectSid",
                Values = new[] { Binary(new byte[] { 1, 2, 3 }) }
            }
        };
        var add = Assert.IsType<AddRequest>(PowerShellDirectoryProviderAdapter.CreateRequest(addRequest));
        Assert.Equal(2, add.Attributes.Count);

        var modifyRequest = Request(PowerShellDirectoryOperation.Modify);
        modifyRequest.DistinguishedName = addRequest.DistinguishedName;
        modifyRequest.Modifications = new[]
        {
            new PowerShellDirectoryModification
            {
                Name = "description",
                Operation = PowerShellDirectoryModificationOperation.Replace,
                Values = new[] { Text("updated") }
            },
            new PowerShellDirectoryModification
            {
                Name = "memberOf",
                Operation = PowerShellDirectoryModificationOperation.Delete
            }
        };
        var modify = Assert.IsType<ModifyRequest>(PowerShellDirectoryProviderAdapter.CreateRequest(modifyRequest));
        Assert.Equal(2, modify.Modifications.Count);
        Assert.Equal(DirectoryAttributeOperation.Replace, modify.Modifications[0].Operation);

        var deleteRequest = Request(PowerShellDirectoryOperation.Delete);
        deleteRequest.DistinguishedName = addRequest.DistinguishedName;
        Assert.IsType<DeleteRequest>(PowerShellDirectoryProviderAdapter.CreateRequest(deleteRequest));

        var renameRequest = Request(PowerShellDirectoryOperation.ModifyDistinguishedName);
        renameRequest.DistinguishedName = addRequest.DistinguishedName;
        renameRequest.NewRelativeDistinguishedName = "CN=Renamed";
        renameRequest.NewParentDistinguishedName = "OU=Moved,DC=example,DC=test";
        var rename = Assert.IsType<ModifyDNRequest>(PowerShellDirectoryProviderAdapter.CreateRequest(renameRequest));
        Assert.True(rename.DeleteOldRdn);

        var compareRequest = Request(PowerShellDirectoryOperation.Compare);
        compareRequest.DistinguishedName = addRequest.DistinguishedName;
        compareRequest.CompareAttributeName = "description";
        compareRequest.CompareValue = Text("updated");
        Assert.IsType<CompareRequest>(PowerShellDirectoryProviderAdapter.CreateRequest(compareRequest));
    }

    [Fact]
    public void DirectoryAdapterRejectsUnsafeOrIncompleteRequestsBeforeConnecting()
    {
        var adapter = new PowerShellDirectoryProviderAdapter();
        var password = new SecureString();
        foreach (var character in "runtime-only") password.AppendChar(character);
        password.MakeReadOnly();
        var insecureBasic = Request(PowerShellDirectoryOperation.Search);
        insecureBasic.Authentication = PowerShellDirectoryAuthentication.Basic;
        insecureBasic.Credential = new PowerShellDirectoryCredential("user", password);
        var basic = Assert.Throws<NotSupportedException>(() => adapter.Execute(insecureBasic));
        Assert.Contains("not target-qualified", basic.Message, StringComparison.Ordinal);
        Assert.DoesNotContain("runtime-only", basic.ToString(), StringComparison.Ordinal);

        var unqualifiedPort = Request(PowerShellDirectoryOperation.Search);
        unqualifiedPort.Port = 636;
        Assert.Throws<NotSupportedException>(() => adapter.Execute(unqualifiedPort));
        var unqualifiedReferral = Request(PowerShellDirectoryOperation.Search);
        unqualifiedReferral.FollowReferrals = true;
        Assert.Throws<NotSupportedException>(() => adapter.Execute(unqualifiedReferral));

        Assert.Null(typeof(PowerShellDirectoryRequest).GetProperty("Connection"));
        Assert.Empty(typeof(PowerShellDirectorySession).GetConstructors());
        Assert.Null(typeof(PowerShellDirectorySession).GetProperty("Connection"));

        var missingModify = Request(PowerShellDirectoryOperation.Modify);
        missingModify.DistinguishedName = "CN=Missing,DC=example,DC=test";
        Assert.Throws<ArgumentException>(() => adapter.Execute(missingModify));

        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();
        Assert.Throws<OperationCanceledException>(() => adapter.Execute(Request(PowerShellDirectoryOperation.Search), cancellation.Token));
    }

    [Fact]
    public void DirectoryEntryPointsRejectMismatchedOperationsAndPortableCredentials()
    {
        var options = new JsonSerializerOptions();
        options.Converters.Add(new JsonStringEnumConverter());
        var request = Request(PowerShellDirectoryOperation.Read);
        request.DistinguishedName = string.Empty;
        var json = JsonSerializer.Serialize(request, options);

        var mismatch = Assert.Throws<InvalidOperationException>(() =>
            PowerShellDirectoryProviderEntryPoints.Search(json, CancellationToken.None));
        Assert.Contains("cannot execute", mismatch.Message, StringComparison.Ordinal);

        var password = new SecureString();
        password.AppendChar('x');
        request.Credential = new PowerShellDirectoryCredential("user", password);
        Assert.Null(JsonSerializer.Deserialize<PowerShellDirectoryRequest>(JsonSerializer.Serialize(request, options), options)!.Credential);
    }

    private static PowerShellDirectoryRequest Request(PowerShellDirectoryOperation operation)
        => new()
        {
            Operation = operation,
            HostName = "ldap.example.test",
            BaseDistinguishedName = "DC=example,DC=test",
            Filter = "(objectClass=*)",
            Scope = PowerShellDirectorySearchScope.Subtree,
            AttributeNames = new[] { "distinguishedName" },
            PageSize = 100,
            TimeoutSeconds = 10
        };

    private static PowerShellDirectoryValue Text(string value) => new() { Text = value };

    private static PowerShellDirectoryValue Binary(byte[] value)
        => new() { IsBinary = true, Base64 = Convert.ToBase64String(value) };
}
