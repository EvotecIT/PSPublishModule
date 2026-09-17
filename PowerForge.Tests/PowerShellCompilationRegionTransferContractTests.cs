using System.Collections;
using System.Collections.Specialized;

namespace PowerForge.Tests;

public sealed class PowerShellCompilationRegionTransferContractTests
{
    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [InlineData(typeof(int), PowerShellRegionTransferShape.StableScalar, PowerShellRegionTransferElementContract.StableScalar, PowerShellRegionTransferOutputBehavior.Atomic)]
    [InlineData(typeof(string), PowerShellRegionTransferShape.StableScalar, PowerShellRegionTransferElementContract.StableScalar, PowerShellRegionTransferOutputBehavior.Atomic)]
    [InlineData(typeof(Hashtable), PowerShellRegionTransferShape.AtomicMap, PowerShellRegionTransferElementContract.OpaqueReference, PowerShellRegionTransferOutputBehavior.Atomic)]
    [InlineData(typeof(OrderedDictionary), PowerShellRegionTransferShape.AtomicMap, PowerShellRegionTransferElementContract.OpaqueReference, PowerShellRegionTransferOutputBehavior.Atomic)]
    [InlineData(typeof(string[]), PowerShellRegionTransferShape.StableScalarVector, PowerShellRegionTransferElementContract.StableScalar, PowerShellRegionTransferOutputBehavior.EnumerateOneLevel)]
    [InlineData(typeof(Guid[]), PowerShellRegionTransferShape.StableScalarVector, PowerShellRegionTransferElementContract.StableScalar, PowerShellRegionTransferOutputBehavior.EnumerateOneLevel)]
    [InlineData(typeof(Array), PowerShellRegionTransferShape.ListSequence, PowerShellRegionTransferElementContract.OpaqueReference, PowerShellRegionTransferOutputBehavior.EnumerateOneLevel)]
    [InlineData(typeof(ArrayList), PowerShellRegionTransferShape.ListSequence, PowerShellRegionTransferElementContract.OpaqueReference, PowerShellRegionTransferOutputBehavior.EnumerateOneLevel)]
    [InlineData(typeof(List<int>), PowerShellRegionTransferShape.ListSequence, PowerShellRegionTransferElementContract.StableScalar, PowerShellRegionTransferOutputBehavior.EnumerateOneLevel)]
    [InlineData(typeof(List<object>), PowerShellRegionTransferShape.ListSequence, PowerShellRegionTransferElementContract.OpaqueReference, PowerShellRegionTransferOutputBehavior.EnumerateOneLevel)]
    public void Describe_AdmitsClosedTransferFamilies(
        Type type,
        PowerShellRegionTransferShape shape,
        PowerShellRegionTransferElementContract element,
        PowerShellRegionTransferOutputBehavior output)
    {
        var contract = PowerShellRegionTransferTypePolicy.Describe(
            type,
            PowerShellRegionTransferDirection.LiveIn,
            PowerShellRegionTransferOwnership.ParameterBorrowed,
            PowerShellRegionTransferMutation.None);

        Assert.True(contract.Supported);
        Assert.Equal(shape, contract.Shape);
        Assert.Equal(element, contract.ElementContract);
        Assert.Equal(output, contract.OutputBehavior);
        Assert.Equal(PowerShellRegionTransferDirection.LiveIn, contract.Direction);
        Assert.Equal(PowerShellRegionTransferOwnership.ParameterBorrowed, contract.Ownership);
    }

    [Theory]
    [Trait("Category", "PowerShellCompilerGate")]
    [InlineData(typeof(object))]
    [InlineData(typeof(System.Management.Automation.PSObject))]
    [InlineData(typeof(IEnumerable))]
    [InlineData(typeof(Dictionary<string, object>))]
    [InlineData(typeof(object[]))]
    [InlineData(typeof(string[,]))]
    public void Describe_RejectsDynamicAndOpenCollectionShapes(Type type)
    {
        var contract = PowerShellRegionTransferTypePolicy.Describe(type);

        Assert.False(contract.Supported);
        Assert.Equal(PowerShellRegionTransferShape.Unsupported, contract.Shape);
    }

    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void Describe_RejectsCompiledMutationForOtherwiseSupportedCollection()
    {
        var contract = PowerShellRegionTransferTypePolicy.Describe(
            typeof(OrderedDictionary),
            PowerShellRegionTransferDirection.LiveInOut,
            PowerShellRegionTransferOwnership.EarlierRegion,
            PowerShellRegionTransferMutation.CompiledOwned);

        Assert.False(contract.Supported);
        Assert.Equal(PowerShellRegionTransferShape.AtomicMap, contract.Shape);
        Assert.Equal(PowerShellRegionTransferMutation.CompiledOwned, contract.Mutation);
    }
}
