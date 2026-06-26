using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PowerForge;

internal sealed class ModuleStateJsonService
{
    private readonly JsonSerializerOptions _options = new()
    {
        PropertyNameCaseInsensitive = true,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
        Converters = { new JsonStringEnumConverter() }
    };

    internal ModuleStateInventory LoadInventory(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Inventory path is required.", nameof(path));

        return ReadInventory(File.ReadAllText(path));
    }

    internal ModuleStateDesiredState LoadDesiredState(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Desired-state path is required.", nameof(path));

        return ReadDesiredState(File.ReadAllText(path));
    }

    internal ModuleStateMaintenanceReceipt LoadMaintenanceReceipt(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            throw new ArgumentException("Maintenance receipt path is required.", nameof(path));

        return ReadMaintenanceReceipt(File.ReadAllText(path));
    }

    internal ModuleStateInventory ReadInventory(string json)
    {
        var dto = JsonSerializer.Deserialize<InventoryDto>(json, _options)
            ?? throw new InvalidOperationException("Module state inventory JSON is empty.");

        var modules = new List<ModuleStateInstalledModule>();
        foreach (var module in dto.InstalledModules ?? Array.Empty<InstalledModuleDto>())
        {
            modules.Add(new ModuleStateInstalledModule(
                module.Name ?? string.Empty,
                module.Version ?? string.Empty,
                module.PowerShellEdition,
                module.Scope,
                module.Path,
                module.SourceRepository,
                module.IsLoaded,
                module.IsEffectiveImportCandidate));
        }

        return new ModuleStateInventory(modules);
    }

    internal ModuleStateDesiredState ReadDesiredState(string json)
    {
        var dto = JsonSerializer.Deserialize<DesiredStateDto>(json, _options)
            ?? throw new InvalidOperationException("Module state desired-state JSON is empty.");

        var modules = new List<ModuleStateDesiredModule>();
        foreach (var module in dto.Modules ?? Array.Empty<DesiredModuleDto>())
        {
            modules.Add(new ModuleStateDesiredModule(
                module.Name ?? string.Empty,
                module.VersionPolicy ?? module.Version ?? module.RequiredVersion,
                module.AllowedSources ?? module.Repositories ?? ToArray(module.Repository),
                module.Scope));
        }

        var families = new List<ModuleStateFamilyPolicy>();
        foreach (var family in dto.FamilyPolicies ?? dto.Families ?? Array.Empty<FamilyPolicyDto>())
        {
            families.Add(new ModuleStateFamilyPolicy(
                family.Name ?? string.Empty,
                family.Modules ?? Array.Empty<string>(),
                family.CoherenceRule));
        }

        return new ModuleStateDesiredState(modules, families);
    }

    internal ModuleStateMaintenanceReceipt ReadMaintenanceReceipt(string json)
    {
        var dto = JsonSerializer.Deserialize<MaintenanceReceiptDto>(json, _options)
            ?? throw new InvalidOperationException("Module state maintenance receipt JSON is empty.");

        var modules = new List<ModuleStateMaintenanceReceiptModule>();
        foreach (var module in dto.Modules ?? dto.MaintainedModules ?? dto.InstalledModules ?? Array.Empty<MaintenanceReceiptModuleDto>())
        {
            modules.Add(new ModuleStateMaintenanceReceiptModule(
                module.Name ?? string.Empty,
                module.Version ?? string.Empty,
                module.SourceRepository,
                module.Scope));
        }

        return new ModuleStateMaintenanceReceipt(dto.Source ?? dto.Profile ?? dto.Name, modules);
    }

    private sealed class InventoryDto
    {
        public InstalledModuleDto[]? InstalledModules { get; set; }
    }

    private sealed class InstalledModuleDto
    {
        public string? Name { get; set; }

        public string? Version { get; set; }

        public string? PowerShellEdition { get; set; }

        public string? Scope { get; set; }

        public string? Path { get; set; }

        public string? SourceRepository { get; set; }

        public bool IsLoaded { get; set; }

        public bool IsEffectiveImportCandidate { get; set; }
    }

    private sealed class DesiredStateDto
    {
        public DesiredModuleDto[]? Modules { get; set; }

        public FamilyPolicyDto[]? Families { get; set; }

        public FamilyPolicyDto[]? FamilyPolicies { get; set; }
    }

    private sealed class DesiredModuleDto
    {
        public string? Name { get; set; }

        public string? VersionPolicy { get; set; }

        public string? Version { get; set; }

        public string? RequiredVersion { get; set; }

        public string[]? AllowedSources { get; set; }

        public string[]? Repositories { get; set; }

        public string? Repository { get; set; }

        public string? Scope { get; set; }
    }

    private static string[]? ToArray(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : new[] { value!.Trim() };

    private sealed class FamilyPolicyDto
    {
        public string? Name { get; set; }

        public string[]? Modules { get; set; }

        public ModuleStateFamilyCoherenceRule CoherenceRule { get; set; } = ModuleStateFamilyCoherenceRule.SameVersion;
    }

    private sealed class MaintenanceReceiptDto
    {
        public string? Name { get; set; }

        public string? Source { get; set; }

        public string? Profile { get; set; }

        public MaintenanceReceiptModuleDto[]? Modules { get; set; }

        public MaintenanceReceiptModuleDto[]? MaintainedModules { get; set; }

        public MaintenanceReceiptModuleDto[]? InstalledModules { get; set; }
    }

    private sealed class MaintenanceReceiptModuleDto
    {
        public string? Name { get; set; }

        public string? Version { get; set; }

        public string? SourceRepository { get; set; }

        public string? Scope { get; set; }
    }
}
