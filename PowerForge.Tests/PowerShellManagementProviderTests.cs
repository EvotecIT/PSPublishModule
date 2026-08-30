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

    [Fact]
    public void TypedCimAdapterQueriesLocalProviderAndReturnsPortableProperties()
    {
        var result = new PowerShellManagementProviderAdapter().Execute(new PowerShellManagementRequest
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
        Assert.Equal("PowerForge.Management/1", contract.Serialization);
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
