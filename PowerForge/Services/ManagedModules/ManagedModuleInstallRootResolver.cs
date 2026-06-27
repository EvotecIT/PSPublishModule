namespace PowerForge;

/// <summary>
/// Resolves default PowerShell module roots for managed installs.
/// </summary>
public static class ManagedModuleInstallRootResolver
{
    /// <summary>
    /// Resolves a module root for a scope and shell edition.
    /// </summary>
    /// <param name="scope">Install scope.</param>
    /// <param name="shellEdition">PowerShell path family.</param>
    /// <param name="customRoot">Custom root used when scope is custom.</param>
    /// <returns>Module root path.</returns>
    public static string Resolve(
        ManagedModuleInstallScope scope,
        ManagedModuleShellEdition shellEdition = ManagedModuleShellEdition.Auto,
        string? customRoot = null)
    {
        if (scope == ManagedModuleInstallScope.Custom)
        {
            if (string.IsNullOrWhiteSpace(customRoot))
                throw new ArgumentException("A custom module root is required when scope is Custom.", nameof(customRoot));

            return Path.GetFullPath(customRoot!.Trim().Trim('"'));
        }

        var edition = ResolveEdition(shellEdition);
        var isWindows = Path.DirectorySeparatorChar == '\\';
        if (scope == ManagedModuleInstallScope.CurrentUser)
        {
            if (isWindows)
            {
                var documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                var folder = edition == ManagedModuleShellEdition.Desktop ? "WindowsPowerShell" : "PowerShell";
                return Path.Combine(documents, folder, "Modules");
            }

            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (string.IsNullOrWhiteSpace(home))
                home = Environment.GetEnvironmentVariable("HOME") ?? string.Empty;
            if (string.IsNullOrWhiteSpace(home))
                throw new InvalidOperationException("Unable to resolve the current user's home directory.");

            return Path.Combine(home, ".local", "share", "powershell", "Modules");
        }

        if (!isWindows)
            return Path.Combine(Path.DirectorySeparatorChar.ToString(), "usr", "local", "share", "powershell", "Modules");

        var programFiles = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles);
        if (string.IsNullOrWhiteSpace(programFiles))
            throw new InvalidOperationException("Unable to resolve Program Files for AllUsers module installation.");

        var allUsersFolder = edition == ManagedModuleShellEdition.Desktop ? "WindowsPowerShell" : "PowerShell";
        return Path.Combine(programFiles, allUsersFolder, "Modules");
    }

    private static ManagedModuleShellEdition ResolveEdition(ManagedModuleShellEdition shellEdition)
    {
        if (shellEdition != ManagedModuleShellEdition.Auto)
            return shellEdition;

#if NET472
        return ManagedModuleShellEdition.Desktop;
#else
        return ManagedModuleShellEdition.Core;
#endif
    }
}
