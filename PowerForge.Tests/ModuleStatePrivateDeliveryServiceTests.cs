using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
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

    [Fact]
    public void CreateRequest_ForcesActionRequestedRepair()
    {
        var request = InvokeCreateRequest(new[]
        {
            new ModuleStatePlanAction(
                ModuleStatePlanActionKind.Install,
                "Company.Tools",
                "1.2.0",
                "=1.2.0",
                "source repair",
                isRepair: true,
                force: true,
                targetRepository: "CompanyModules")
        });

        Assert.True(request.Force);
    }

    [Fact]
    public void CreateRequest_PreservesRequestedAutoTransport()
    {
        var request = InvokeCreateRequest(
            new[]
            {
                new ModuleStatePlanAction(ModuleStatePlanActionKind.Install, "Company.Tools", null, "1.2.0", "missing")
            },
            new ModuleStatePrivateDeliveryOptions
            {
                DeliveryTransport = ModuleStateDeliveryTransport.Auto
            });

        Assert.Equal(ModuleStateDeliveryTransport.Auto, request.DeliveryTransport);
    }

    [Fact]
    public void ResolveActionRepository_PreservesActionTargetOverGlobalRepository()
    {
        var action = new ModuleStatePlanAction(
            ModuleStatePlanActionKind.Install,
            "Company.Tools",
            installedVersion: null,
            ">=1.2.0",
            "missing",
            targetRepository: "CompanyModules");
        var options = new ModuleStatePrivateDeliveryOptions
        {
            Repository = "FallbackGallery"
        };

        var repository = InvokeResolveActionRepository(action, options);

        Assert.Equal("CompanyModules", repository);
    }

    [Fact]
    public void CreateRequest_RejectsConflictingDuplicateModulePolicies()
    {
        var exception = Assert.Throws<TargetInvocationException>(() => InvokeCreateRequest(new[]
        {
            new ModuleStatePlanAction(ModuleStatePlanActionKind.Install, "Company.Tools", null, "=1.1.0", "repair", targetScope: "CurrentUser"),
            new ModuleStatePlanAction(ModuleStatePlanActionKind.Install, "Company.Tools", null, "=1.2.0", "repair", targetScope: "CurrentUser")
        }));

        Assert.IsType<InvalidOperationException>(exception.InnerException);
        Assert.Contains("conflicting version policies", exception.InnerException!.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void DeliveryGroupKey_DistinguishesSameModuleAcrossScopes()
    {
        var comparer = DeliveryGroupKeyComparer.Instance;
        var currentUser = new DeliveryGroupKey(ModuleStatePlanActionKind.Install, "Company", false, "Company.Tools", "CurrentUser");
        var allUsers = new DeliveryGroupKey(ModuleStatePlanActionKind.Install, "Company", false, "Company.Tools", "AllUsers");

        Assert.False(comparer.Equals(currentUser, allUsers));
    }

    [Fact]
    public void ManagedCreateUpdateRequest_PreservesLoadedModuleEvidence()
    {
        var loaded = new[]
        {
            new ManagedModuleLoadedModule
            {
                Name = "Company.Tools",
                Version = "1.0.0",
                ModuleBase = @"C:\Modules\Company.Tools\1.0.0"
            }
        };
        var action = new ModuleStatePlanAction(
            ModuleStatePlanActionKind.Update,
            "Company.Tools",
            "1.0.0",
            ">=2.0.0",
            "stale version",
            isRepair: true);
        var options = new ModuleStateManagedDeliveryOptions
        {
            LoadedModules = loaded
        };

        var request = InvokeManagedCreateUpdateRequest(action, options);

        Assert.Same(loaded, request.LoadedModules);
        Assert.NotNull(request.SourcePolicy);
    }

    [Fact]
    public void ManagedResolveRepository_PreservesActionTargetOverGlobalRepository()
    {
        var action = new ModuleStatePlanAction(
            ModuleStatePlanActionKind.Install,
            "Company.Tools",
            installedVersion: null,
            ">=1.2.0",
            "missing",
            targetRepository: "https://first.example.test/v3/index.json");
        var options = new ModuleStateManagedDeliveryOptions
        {
            Repository = "https://fallback.example.test/v3/index.json"
        };

        var repository = InvokeManagedResolveRepository(action, options);

        Assert.Equal("https://first.example.test/v3/index.json", repository.Source);
    }

    [Fact]
    public void ManagedResolveRepository_PreservesActionTargetOverFallbackProfile()
    {
        var action = new ModuleStatePlanAction(
            ModuleStatePlanActionKind.Install,
            "Company.Tools",
            installedVersion: null,
            ">=1.2.0",
            "missing",
            targetRepository: "https://selected.example.test/v3/index.json");
        var options = new ModuleStateManagedDeliveryOptions
        {
            ProfileName = "FallbackProfile"
        };

        var repository = InvokeManagedResolveRepository(action, options);

        Assert.Equal("https://selected.example.test/v3/index.json", repository.Source);
    }

    private static PrivateModuleWorkflowRequest InvokeCreateRequest(
        IReadOnlyList<ModuleStatePlanAction> actions,
        ModuleStatePrivateDeliveryOptions? options = null)
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
                actions.Any(static action => action.Force),
                actions,
                options ?? new ModuleStatePrivateDeliveryOptions()
            });

        return Assert.IsType<PrivateModuleWorkflowRequest>(result);
    }

    private static string? InvokeResolveActionRepository(ModuleStatePlanAction action, ModuleStatePrivateDeliveryOptions options)
    {
        var method = typeof(ModuleStatePrivateDeliveryService).GetMethod(
            "ResolveActionRepository",
            BindingFlags.Static | BindingFlags.NonPublic);
        Assert.NotNull(method);

        return Assert.IsType<string?>(method!.Invoke(null, new object?[] { action, options }));
    }

    private static ManagedModuleUpdateRequest InvokeManagedCreateUpdateRequest(
        ModuleStatePlanAction action,
        ModuleStateManagedDeliveryOptions options)
    {
        var method = typeof(ModuleStateManagedDeliveryService).GetMethod(
            "CreateUpdateRequest",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var service = (ModuleStateManagedDeliveryService)RuntimeHelpers.GetUninitializedObject(typeof(ModuleStateManagedDeliveryService));
        var result = method!.Invoke(
            service,
            new object?[]
            {
                new ManagedModuleRepository("Local", "C:\\Feed"),
                action,
                options
            });

        return Assert.IsType<ManagedModuleUpdateRequest>(result);
    }

    private static ManagedModuleRepository InvokeManagedResolveRepository(
        ModuleStatePlanAction action,
        ModuleStateManagedDeliveryOptions options)
    {
        var method = typeof(ModuleStateManagedDeliveryService).GetMethod(
            "ResolveRepository",
            BindingFlags.Instance | BindingFlags.NonPublic);
        Assert.NotNull(method);

        var service = (ModuleStateManagedDeliveryService)RuntimeHelpers.GetUninitializedObject(typeof(ModuleStateManagedDeliveryService));
        var result = method!.Invoke(service, new object?[] { action, options });

        return Assert.IsType<ManagedModuleRepository>(result);
    }
}
