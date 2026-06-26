using System;
using System.Collections.Generic;
using System.Reflection;
using PowerForge;
using PSPublishModule;
using Xunit;

namespace PowerForge.Tests;

public sealed class ModuleStatePrivateDeliveryServiceTests
{
    [Fact]
    public void CreateRequest_PreservesBareExactAndInclusiveRangePolicies()
    {
        var request = InvokeCreateRequest(new[]
        {
            new ModuleStatePlanAction(ModuleStatePlanActionKind.Install, "Company.Exact", null, "1.2.0", "missing"),
            new ModuleStatePlanAction(ModuleStatePlanActionKind.Install, "Company.Range", null, ">=2.0.0 <=2.5.0", "missing")
        });

        Assert.Equal("1.2.0", request.RequiredVersions["Company.Exact"]);
        Assert.False(request.RequiredVersions.ContainsKey("Company.Range"));
        Assert.Equal("2.0.0", request.MinimumVersions["Company.Range"]);
        Assert.Equal("2.5.0", request.MaximumVersions["Company.Range"]);
    }

    [Fact]
    public void CreateRequest_PreservesExclusiveRangePolicies()
    {
        var request = InvokeCreateRequest(new[]
        {
            new ModuleStatePlanAction(ModuleStatePlanActionKind.Install, "Company.Range", null, ">2.0.0 <3.0.0", "missing")
        });

        Assert.Equal("2.0.0", request.MinimumVersions["Company.Range"]);
        Assert.False(request.MinimumVersionInclusivity["Company.Range"]);
        Assert.Equal("3.0.0", request.MaximumVersions["Company.Range"]);
        Assert.False(request.MaximumVersionInclusivity["Company.Range"]);
    }

    private static PrivateModuleWorkflowRequest InvokeCreateRequest(IReadOnlyList<ModuleStatePlanAction> actions)
    {
        var method = typeof(ModuleStatePrivateDeliveryService).GetMethod(
            "CreateRequest",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var result = method!.Invoke(
            null,
            new object?[]
            {
                ModuleStatePlanActionKind.Install,
                "Company",
                actions,
                new ModuleStatePrivateDeliveryOptions()
            });

        return Assert.IsType<PrivateModuleWorkflowRequest>(result);
    }
}
