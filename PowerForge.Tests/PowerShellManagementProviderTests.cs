using PowerForge;
using System.Xml;
using Xunit;

namespace PowerForge.Tests;

[Trait("Category", "PowerShellCompilation")]
public sealed class PowerShellManagementProviderTests
{
    [Fact]
    public void CdxmlReaderBuildsDeterministicCommandMetadataWithoutImportOrTargetAccess()
    {
        using var fixture = CdxmlFixture.Create("""
<?xml version="1.0" encoding="utf-8"?>
<PowerShellMetadata xmlns="http://schemas.microsoft.com/cmdlets-over-objects/2009/11">
  <Class ClassName="root/cimv2/Generic_Widget" ClassVersion="1.0">
    <Version>1.0</Version>
    <DefaultNoun>GenericWidget</DefaultNoun>
    <InstanceCmdlets>
      <GetCmdlet>
        <GetCmdletParameters>
          <QueryableProperties>
            <Property PropertyName="Name"><Type PSType="System.String" /><RegularQuery AllowGlobbing="true"><CmdletParameterMetadata PSName="Name" /></RegularQuery></Property>
          </QueryableProperties>
        </GetCmdletParameters>
      </GetCmdlet>
      <Cmdlet>
        <CmdletMetadata Verb="Set" Noun="GenericWidget" />
        <Method MethodName="Update"><Parameters><Parameter ParameterName="Value" /></Parameters></Method>
      </Cmdlet>
    </InstanceCmdlets>
  </Class>
</PowerShellMetadata>
""");
        var reader = new PowerShellCdxmlMetadataReader();

        var first = reader.Read(fixture.Path);
        var second = reader.Read(fixture.Path);

        Assert.Equal("root/cimv2/Generic_Widget", first.ClassName);
        Assert.Equal("GenericWidget", first.DefaultNoun);
        Assert.Equal(first.SourceSha256, second.SourceSha256);
        Assert.Equal(new[] { "Get-GenericWidget", "Set-GenericWidget" }, first.Commands.Select(static command => command.CommandName));
        Assert.Equal("Name", Assert.Single(first.Commands[0].Parameters));
        Assert.Equal("Update", first.Commands[1].MethodName);
        Assert.Equal("Value", Assert.Single(first.Commands[1].Parameters));
    }

    [Fact]
    public void CdxmlReaderRejectsDtdAndExternalEntityExpansion()
    {
        using var fixture = CdxmlFixture.Create("""
<?xml version="1.0"?>
<!DOCTYPE PowerShellMetadata [<!ENTITY external SYSTEM "file:///forbidden">]>
<PowerShellMetadata><Class ClassName="Generic"><DefaultNoun>Generic</DefaultNoun></Class></PowerShellMetadata>
""");

        Assert.Throws<XmlException>(() => new PowerShellCdxmlMetadataReader().Read(fixture.Path));
    }

    [WindowsFact]
    public void TypedCimAdapterQueriesLocalProviderAndReturnsPortableProperties()
    {
        using var result = new PowerShellManagementProviderAdapter().Execute(new PowerShellManagementRequest
        {
            Operation = PowerShellManagementOperation.Query,
            Namespace = "root/cimv2",
            QueryDialect = "WQL",
            Query = "SELECT Caption, Version FROM Win32_OperatingSystem",
            Transport = PowerShellManagementTransport.Default,
            TimeoutSeconds = 30
        });

        Assert.True(result.OwnedSessionDisposed);
        var instance = Assert.Single(result.Instances);
        Assert.Equal("Win32_OperatingSystem", instance.ClassName);
        Assert.Contains(instance.Properties, static property => property.Name == "Caption" && !string.IsNullOrWhiteSpace(property.Value));
        Assert.Contains(instance.Properties, static property => property.Name == "Version" && !string.IsNullOrWhiteSpace(property.Value));
        Assert.NotNull(instance.RuntimeInstance);
        result.Dispose();
        Assert.Null(instance.RuntimeInstance);
    }

    [Fact]
    public void TypedCimAdapterHonorsCancellationBeforeSessionCreation()
    {
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        Assert.Throws<OperationCanceledException>(() => new PowerShellManagementProviderAdapter().Execute(
            new PowerShellManagementRequest
            {
                Operation = PowerShellManagementOperation.Enumerate,
                ClassName = "Win32_OperatingSystem"
            },
            cancellation.Token));
    }

    [WindowsFact]
    public void TypedCimAdapterReusesCallerSessionWithoutDisposingItAndBoundsEnumeration()
    {
        using var session = Microsoft.Management.Infrastructure.CimSession.Create(null);
        var adapter = new PowerShellManagementProviderAdapter();
        using var bounded = adapter.Execute(new PowerShellManagementRequest
        {
            Operation = PowerShellManagementOperation.Enumerate,
            ClassName = "Win32_Process",
            ResultLimit = 2,
            Session = session,
            TimeoutSeconds = 30
        });

        Assert.False(bounded.OwnedSessionDisposed);
        Assert.Equal(2, bounded.Instances.Length);
        using var reuse = adapter.Execute(new PowerShellManagementRequest
        {
            Operation = PowerShellManagementOperation.Query,
            Query = "SELECT Caption FROM Win32_OperatingSystem",
            Session = session,
            TimeoutSeconds = 30
        });
        Assert.False(reuse.OwnedSessionDisposed);
        Assert.Single(reuse.Instances);
    }

    [WindowsFact]
    public void TypedCimAdapterDisposesTransferredInstancesWhenEnumerationOrInputProjectionFails()
    {
        using var session = Microsoft.Management.Infrastructure.CimSession.Create(null);
        var raw = session.QueryInstances(
                "root/cimv2",
                "WQL",
                "SELECT Caption FROM Win32_OperatingSystem")
            .First();
        var enumerationFailure = Assert.Throws<InvalidOperationException>(() =>
            PowerShellManagementProviderAdapter.ToPortableInstances(FailAfter(raw)));
        Assert.Equal("synthetic-enumeration-failure", enumerationFailure.Message);
        Assert.Throws<ObjectDisposedException>(() => _ = raw.CimInstanceProperties.Count);

        var adapter = new PowerShellManagementProviderAdapter();
        var invalidInput = Assert.Throws<ArgumentException>(() => adapter.Execute(new PowerShellManagementRequest
        {
            Operation = PowerShellManagementOperation.Create,
            ClassName = "Win32_Environment",
            Properties = new[]
            {
                new PowerShellManagementProperty { Name = "Name", TypeName = "String", Value = "unused" },
                new PowerShellManagementProperty { Name = "UserName", TypeName = "NotACimType", Value = "unused" }
            },
            TimeoutSeconds = 10
        }));
        Assert.Contains("CIM type", invalidInput.Message, StringComparison.OrdinalIgnoreCase);
        using var recovery = adapter.Execute(new PowerShellManagementRequest
        {
            Operation = PowerShellManagementOperation.Query,
            Query = "SELECT Caption FROM Win32_OperatingSystem",
            TimeoutSeconds = 30
        });
        Assert.Single(recovery.Instances);
    }

    [Fact]
    public void ManagementContractCoversOperationsTransportsAuthenticationAndCleanup()
    {
        var contract = PowerShellManagementProviderAdapter.Contract;

        Assert.Equal(Enum.GetValues<PowerShellManagementOperation>(), contract.Operations);
        Assert.Equal(Enum.GetValues<PowerShellManagementTransport>(), contract.Transports);
        Assert.Equal(Enum.GetValues<PowerShellManagementAuthentication>(), contract.Authentication);
        Assert.True(contract.AcceptsRuntimeSession);
        Assert.True(contract.SupportsCancellation);
        Assert.True(contract.DeterministicCleanup);
        Assert.Equal("PowerForge.Management/2", contract.Serialization);
        Assert.DoesNotContain(contract.Authentication, authentication =>
            authentication.ToString().Equals("Certificate", StringComparison.OrdinalIgnoreCase));
    }

    private static IEnumerable<Microsoft.Management.Infrastructure.CimInstance> FailAfter(
        Microsoft.Management.Infrastructure.CimInstance instance)
    {
        yield return instance;
        throw new InvalidOperationException("synthetic-enumeration-failure");
    }

    [Fact]
    public void TypedCimAdapterRejectsUnimplementedAuthenticationAndConflictingSessionOptionsBeforeConnecting()
    {
        var adapter = new PowerShellManagementProviderAdapter();
        var invalidAuthentication = new PowerShellManagementRequest
        {
            Operation = PowerShellManagementOperation.Enumerate,
            ClassName = "Ignored",
            Authentication = (PowerShellManagementAuthentication)999
        };
        Assert.Throws<ArgumentOutOfRangeException>(() => adapter.Execute(invalidAuthentication));

        var missingCredential = new PowerShellManagementRequest
        {
            Operation = PowerShellManagementOperation.Enumerate,
            ClassName = "Ignored",
            Authentication = PowerShellManagementAuthentication.Kerberos
        };
        Assert.Throws<ArgumentException>(() => adapter.Execute(missingCredential));

        using var password = new System.Security.SecureString();
        password.AppendChar('x');
        password.MakeReadOnly();
        var unsupportedDcomCombination = new PowerShellManagementRequest
        {
            Operation = PowerShellManagementOperation.Enumerate,
            ClassName = "Ignored",
            Transport = PowerShellManagementTransport.Dcom,
            Authentication = PowerShellManagementAuthentication.Basic,
            Credential = new PowerShellManagementCredential("ignored", password)
        };
        Assert.Throws<ArgumentException>(() => adapter.Execute(unsupportedDcomCombination));
    }

    private sealed class CdxmlFixture : IDisposable
    {
        private CdxmlFixture(string root, string path)
        {
            Root = root;
            Path = path;
        }

        public string Root { get; }
        public string Path { get; }

        public static CdxmlFixture Create(string content)
        {
            var root = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "PowerForgeCdxmlTests", Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(root);
            var path = System.IO.Path.Combine(root, "provider.cdxml");
            File.WriteAllText(path, content);
            return new CdxmlFixture(root, path);
        }

        public void Dispose()
        {
            try { Directory.Delete(Root, recursive: true); }
            catch (IOException) { }
            catch (UnauthorizedAccessException) { }
        }
    }
}
