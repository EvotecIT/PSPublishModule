using System;
using System.IO;
using System.Management.Automation;
using PowerForge;

namespace PSPublishModule;

internal static class ModuleStateManagedRepositoryResolver
{
    internal static string CreateRepositoryKey(string? repository)
        => string.IsNullOrWhiteSpace(repository) ? string.Empty : repository!.Trim();

    internal static string? ResolveRepositoryIdentity(PSCmdlet cmdlet, string? repository)
    {
        if (string.IsNullOrWhiteSpace(repository))
            return null;

        return IsRepositorySource(repository!)
            ? ManagedModuleCommandSupport.CreateRepository(
                cmdlet,
                ManagedModuleCommandSupport.DefaultRepositoryName,
                repository!).Name
            : repository;
    }

    internal static ManagedModuleRepository ResolveRepositoryForAction(
        PSCmdlet cmdlet,
        string? targetRepository,
        ModuleStateManagedDeliveryOptions options,
        string missingRepositoryMessage)
    {
        if (options.ResolvedRepositories.TryGetValue(CreateRepositoryKey(targetRepository), out var resolved))
            return resolved;

        if (!string.IsNullOrWhiteSpace(options.ProfileName))
        {
            var profile = ModuleRepositoryProfileCommandSupport.TryResolve(options.ProfileName);
            if (profile is not null &&
                (string.IsNullOrWhiteSpace(targetRepository) ||
                 string.Equals(targetRepository, profile.RepositoryName, StringComparison.OrdinalIgnoreCase)))
            {
                return ManagedModuleCommandSupport.CreateRepository(
                    cmdlet,
                    ManagedModuleCommandSupport.DefaultRepositoryName,
                    ManagedModuleCommandSupport.DefaultRepositorySource,
                    options.ProfileName,
                    repositoryWasBound: false);
            }
        }

        if (!string.IsNullOrWhiteSpace(targetRepository))
        {
            var optionsRepository = TryResolveOptionsRepositoryForActionTarget(cmdlet, targetRepository, options);
            if (optionsRepository is not null)
                return optionsRepository;

            return ManagedModuleCommandSupport.CreateRepository(
                cmdlet,
                ManagedModuleCommandSupport.DefaultRepositoryName,
                targetRepository!);
        }

        if (!string.IsNullOrWhiteSpace(options.Repository))
            return ManagedModuleCommandSupport.CreateRepository(
                cmdlet,
                ManagedModuleCommandSupport.DefaultRepositoryName,
                options.Repository!);

        if (!string.IsNullOrWhiteSpace(options.ProfileName))
            return ManagedModuleCommandSupport.CreateRepository(
                cmdlet,
                ManagedModuleCommandSupport.DefaultRepositoryName,
                ManagedModuleCommandSupport.DefaultRepositorySource,
                options.ProfileName,
                repositoryWasBound: false);

        throw new InvalidOperationException(missingRepositoryMessage);
    }

    internal static ManagedModuleRepository? TryResolveOptionsRepositoryForActionTarget(
        PSCmdlet cmdlet,
        string? targetRepository,
        ModuleStateManagedDeliveryOptions options)
    {
        if (string.IsNullOrWhiteSpace(targetRepository) || string.IsNullOrWhiteSpace(options.Repository))
            return null;
        if (!IsRepositorySource(options.Repository!))
            return null;

        var repository = ManagedModuleCommandSupport.CreateRepository(
            cmdlet,
            ManagedModuleCommandSupport.DefaultRepositoryName,
            options.Repository!);
        return string.Equals(repository.Name, targetRepository, StringComparison.OrdinalIgnoreCase)
            ? repository
            : null;
    }

    private static bool IsRepositorySource(string repository)
    {
        var value = repository.Trim().Trim('"');
        if (Uri.TryCreate(value, UriKind.Absolute, out var uri) && !string.IsNullOrWhiteSpace(uri.Scheme))
            return true;

        return Path.IsPathRooted(value) ||
               value.StartsWith(".", StringComparison.Ordinal) ||
               value.IndexOf('\\') >= 0 ||
               value.IndexOf('/') >= 0;
    }
}
