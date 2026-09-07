using System.Management.Automation;
using System.Reflection;
using System.Reflection.Metadata;
using System.Reflection.Metadata.Ecma335;
using System.Reflection.PortableExecutable;
using System.Runtime.Loader;
using PowerForge.Compilation.Build;

namespace PowerForge.Tests;

public sealed class PowerShellCompilationCommandMetadataTests
{
    private const string CommandName = "Read-ProjectedIdentity";
    private const string StorageField = "__PowerForgeCommandIdentity_ReservedSpaceForReadProjectedIdentity_MetadataStorage";

    [Fact]
    public void CommandMetadata_PreservesTypeResolutionAndRepeatedFinalization()
    {
        var original = File.ReadAllBytes(typeof(CommandMetadataProjectionFixture).Assembly.Location);
        var finalized = PowerShellCommandMetadataNames.Apply(original, new[] { Identity() });
        Assert.NotSame(original, finalized);
        Assert.Equal(original.Length, finalized.Length);
        Assert.Same(finalized, PowerShellCommandMetadataNames.Apply(finalized, new[] { Identity() }));
        var context = new AssemblyLoadContext("command-identity-" + Guid.NewGuid().ToString("N"), isCollectible: true);
        context.Resolving += (_, name) => AssemblyLoadContext.Default.Assemblies.FirstOrDefault(assembly => AssemblyName.ReferenceMatchesDefinition(assembly.GetName(), name));
        try
        {
            using var stream = new MemoryStream(finalized);
            var assembly = context.LoadFromStream(stream);
            var command = assembly.GetType(CommandName, throwOnError: true)!;
            Assert.Equal(CommandName, command.FullName);
            Assert.True(command.IsSubclassOf(typeof(PSCmdlet)));
            var metadata = Assert.Single(command.GetCustomAttributes<CmdletAttribute>());
            Assert.Equal("Read", metadata.VerbName);
            Assert.Equal("ProjectedIdentity", metadata.NounName);
            Assert.Equal(CommandName, command.GetField(CommandName, BindingFlags.Static | BindingFlags.NonPublic)!.GetRawConstantValue());
            Assert.Null(assembly.GetType(typeof(CommandMetadataProjectionFixture).FullName!));
            Assert.NotNull(assembly.GetType(typeof(CommandMetadataUnrelatedFixture).FullName!));
        }
        finally { context.Unload(); }
    }

    [Fact]
    public void CommandMetadata_ValidatesTheWholePlanBeforeChangingAnyBytes()
    {
        var original = File.ReadAllBytes(typeof(CommandMetadataProjectionFixture).Assembly.Location);
        var unchanged = (byte[])original.Clone();
        Assert.Throws<InvalidDataException>(() => PowerShellCommandMetadataNames.Apply(original, new[]
        {
            Identity(), new PowerShellCommandMetadataNames.Identity("Missing.Command", "Read-Missing", StorageField)
        }));
        Assert.Equal(unchanged, original);
    }

    [Fact]
    public void CommandMetadata_RejectsSigningAndSharedStringStorage()
    {
        var original = File.ReadAllBytes(typeof(CommandMetadataProjectionFixture).Assembly.Location);
        using var pe = new PEReader(new MemoryStream(original));
        var reader = pe.GetMetadataReader();
        var signed = (byte[])original.Clone();
        signed[pe.PEHeaders.CorHeaderStartOffset + 16] |= (byte)CorFlags.StrongNameSigned;
        var signing = Assert.Throws<InvalidDataException>(() => PowerShellCommandMetadataNames.Apply(signed, new[] { Identity() }));
        Assert.Contains("before signing", signing.Message, StringComparison.Ordinal);

        var target = reader.TypeDefinitions.Single(handle => reader.GetString(reader.GetTypeDefinition(handle).Name) == nameof(CommandMetadataProjectionFixture));
        var storage = reader.GetTypeDefinition(target).GetFields().Single(handle => reader.GetString(reader.GetFieldDefinition(handle).Name) == StorageField);
        var unrelated = reader.TypeDefinitions.Single(handle => reader.GetString(reader.GetTypeDefinition(handle).Name) == nameof(CommandMetadataUnrelatedFixture));
        var sibling = Assert.Single(reader.GetTypeDefinition(unrelated).GetFields());
        Assert.True(pe.PEHeaders.TryGetDirectoryOffset(pe.PEHeaders.CorHeader!.MetadataDirectory, out var metadataOffset));
        var indexSize = reader.GetHeapSize(HeapIndex.String) > ushort.MaxValue ? 4 : 2;
        var siblingNameOffset = metadataOffset + reader.GetTableMetadataOffset(TableIndex.Field) +
            (MetadataTokens.GetRowNumber(sibling) - 1) * reader.GetTableRowSize(TableIndex.Field) + 2;
        var nameIndex = MetadataTokens.GetHeapOffset(reader.GetFieldDefinition(storage).Name);
        var shared = (byte[])original.Clone();
        for (var index = 0; index < indexSize; index++) shared[siblingNameOffset + index] = (byte)(nameIndex >> (8 * index));
        var collision = Assert.Throws<InvalidDataException>(() => PowerShellCommandMetadataNames.Apply(shared, new[] { Identity() }));
        Assert.Contains("overlaps another metadata name", collision.Message, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Read-Name", true)]
    [InlineData("Read-Name.With.Dots", true)]
    [InlineData("Read-Name+Nested", false)]
    [InlineData("Read-Name[]", false)]
    [InlineData("Read-Name,Assembly", false)]
    public void CommandMetadata_RejectsNamesThatChangeClrDisplayIdentity(string name, bool expected)
        => Assert.Equal(expected, PowerShellCommandMetadataNames.CanRepresent(name));

    [Fact]
    public void CommandMetadata_RejectsStorageThatIsASuffixOfAnotherName()
    {
        const string storage = "__PowerForgeSuffixCollisionReservedStorage";
        var original = File.ReadAllBytes(typeof(CommandMetadataSuffixFixture).Assembly.Location);
        using var pe = new PEReader(new MemoryStream(original));
        var reader = pe.GetMetadataReader();
        var definition = reader.TypeDefinitions.Select(reader.GetTypeDefinition)
            .Single(type => reader.GetString(type.Name) == nameof(CommandMetadataSuffixFixture));
        var fields = definition.GetFields().Select(reader.GetFieldDefinition).ToArray();
        var slot = fields.Single(field => reader.GetString(field.Name) == storage);
        var longer = fields.Single(field => reader.GetString(field.Name) == "Prefix" + storage);
        Assert.Equal(MetadataTokens.GetHeapOffset(longer.Name) + "Prefix".Length, MetadataTokens.GetHeapOffset(slot.Name));
        var error = Assert.Throws<InvalidDataException>(() => PowerShellCommandMetadataNames.Apply(original, new[]
        {
            new PowerShellCommandMetadataNames.Identity(typeof(CommandMetadataSuffixFixture).FullName!, "Read-Suffix", storage)
        }));
        Assert.Contains("overlaps another metadata name", error.Message, StringComparison.Ordinal);
    }

    private static PowerShellCommandMetadataNames.Identity Identity()
        => new(typeof(CommandMetadataProjectionFixture).FullName!, CommandName, StorageField);
}

[Cmdlet("Read", "Suffix")]
public sealed class CommandMetadataSuffixFixture : PSCmdlet
{
    private const string __PowerForgeSuffixCollisionReservedStorage = "Read-Suffix";
    private const string Prefix__PowerForgeSuffixCollisionReservedStorage = "unrelated";
}

[Cmdlet("Read", "ProjectedIdentity")]
public sealed class CommandMetadataProjectionFixture : PSCmdlet
{
    private const string __PowerForgeCommandIdentity_ReservedSpaceForReadProjectedIdentity_MetadataStorage = "Read-ProjectedIdentity";
}

public sealed class CommandMetadataUnrelatedFixture
{
    private const string Untouched = "unrelated metadata";
}
