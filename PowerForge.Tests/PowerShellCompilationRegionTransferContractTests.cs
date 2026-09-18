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
        Assert.Equal(4, contract.SchemaVersion);
        if (output == PowerShellRegionTransferOutputBehavior.EnumerateOneLevel)
        {
            Assert.Equal(PowerShellRegionEnumerationOwner.RetainedPowerShell, contract.EnumerationOwner);
            Assert.Equal(PowerShellRegionEnumerationFailureBehavior.PreservePartialSuccessAndStatementContinuation,
                contract.EnumerationFailureBehavior);
            Assert.Equal(PowerShellRegionEnumeratorLifetime.RetainedPowerShell, contract.EnumeratorLifetime);
        }
        else
        {
            Assert.Equal(PowerShellRegionEnumerationOwner.None, contract.EnumerationOwner);
            Assert.Equal(PowerShellRegionEnumerationFailureBehavior.None, contract.EnumerationFailureBehavior);
            Assert.Equal(PowerShellRegionEnumeratorLifetime.None, contract.EnumeratorLifetime);
        }
    }

    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void DescribeNoValue_AdmitsOnlyTerminalZeroRecordTransfer()
    {
        var contract = PowerShellRegionTransferTypePolicy.DescribeNoValue();

        Assert.True(contract.Supported);
        Assert.Equal(PowerShellRegionTransferShape.NoValue, contract.Shape);
        Assert.Equal(PowerShellRegionTransferElementContract.None, contract.ElementContract);
        Assert.Equal(PowerShellRegionTransferDirection.TerminalSuccess, contract.Direction);
        Assert.Equal(PowerShellRegionTransferOwnership.Unspecified, contract.Ownership);
        Assert.Equal(PowerShellRegionTransferOutputBehavior.None, contract.OutputBehavior);
        Assert.Equal(PowerShellRegionTransferMutation.None, contract.Mutation);
        Assert.Equal(PowerShellRegionEnumerationOwner.None, contract.EnumerationOwner);
        Assert.Equal(PowerShellRegionEnumerationFailureBehavior.None, contract.EnumerationFailureBehavior);
        Assert.Equal(PowerShellRegionEnumeratorLifetime.None, contract.EnumeratorLifetime);
        Assert.Equal(4, contract.SchemaVersion);
        Assert.Equal(0, (int)PowerShellRegionTransferShape.Unsupported);
        Assert.Equal(1, (int)PowerShellRegionTransferShape.StableScalar);
        Assert.Equal(2, (int)PowerShellRegionTransferShape.AtomicMap);
        Assert.Equal(3, (int)PowerShellRegionTransferShape.StableScalarVector);
        Assert.Equal(4, (int)PowerShellRegionTransferShape.ListSequence);
        Assert.Equal(5, (int)PowerShellRegionTransferShape.NoValue);
        Assert.Equal(6, (int)PowerShellRegionTransferShape.NullValue);
        Assert.Equal(7, (int)PowerShellRegionTransferShape.ClosedValueAlternative);
        Assert.Equal(0, (int)PowerShellRegionTransferElementContract.Unsupported);
        Assert.Equal(1, (int)PowerShellRegionTransferElementContract.StableScalar);
        Assert.Equal(2, (int)PowerShellRegionTransferElementContract.OpaqueReference);
        Assert.Equal(3, (int)PowerShellRegionTransferElementContract.None);
        Assert.False(PowerShellRegionTransferTypePolicy.IsSupported(typeof(void)));
    }

    [Fact]
    [Trait("Category", "PowerShellCompilerGate")]
    public void DescribeNullValue_AdmitsOneNullRecordWithoutOpeningObjectTransfer()
    {
        var contract = PowerShellRegionTransferTypePolicy.DescribeNullValue();

        Assert.True(contract.Supported);
        Assert.Equal(PowerShellRegionTransferShape.NullValue, contract.Shape);
        Assert.Equal(PowerShellRegionTransferElementContract.None, contract.ElementContract);
        Assert.Equal(PowerShellRegionTransferDirection.TerminalSuccess, contract.Direction);
        Assert.Equal(PowerShellRegionTransferOwnership.Unspecified, contract.Ownership);
        Assert.Equal(PowerShellRegionTransferOutputBehavior.Atomic, contract.OutputBehavior);
        Assert.Equal(PowerShellRegionTransferMutation.None, contract.Mutation);
        Assert.Equal(PowerShellRegionEnumerationOwner.None, contract.EnumerationOwner);
        Assert.Equal(PowerShellRegionEnumerationFailureBehavior.None, contract.EnumerationFailureBehavior);
        Assert.Equal(PowerShellRegionEnumeratorLifetime.None, contract.EnumeratorLifetime);
        Assert.Equal(4, contract.SchemaVersion);
        Assert.False(PowerShellRegionTransferTypePolicy.IsSupported(typeof(object)));
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
