using System.Text.Json;
using System.Text.Json.Serialization;

namespace PowerForge;

/// <summary>Reads and writes the dependency review exchanged between compiler hosts.</summary>
public static class PowerShellCompilationReviewFile
{
    /// <summary>Reads a review file for a later build against the selected source and target.</summary>
    public static PowerShellCompilationReview Read(string path)
    {
        return JsonSerializer.Deserialize(File.ReadAllText(path), PowerShellCompilationReviewJsonContext.Default.PowerShellCompilationReview)
               ?? throw new InvalidDataException("The dependency review was empty.");
    }

    /// <summary>Verifies a saved review against a newly checked selection without building.</summary>
    public static void EnsureMatches(PowerShellCompilationReview saved, PowerShellCompilationReview current)
    {
        if (saved is null) throw new ArgumentNullException(nameof(saved));
        if (current is null) throw new ArgumentNullException(nameof(current));
        if (saved.SchemaVersion != 1 || saved.Target is null || saved.DependencyLock is null ||
            current.SchemaVersion != 1 || current.Target is null || current.DependencyLock is null)
            throw new InvalidDataException("The dependency review must contain a supported target and dependency lock.");

        var savedTarget = PowerShellCompilationTargetContractService.Normalize(saved.Target.Copy());
        var currentTarget = PowerShellCompilationTargetContractService.Normalize(current.Target.Copy());
        if (!string.Equals(savedTarget.ContractSha256, currentTarget.ContractSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The saved review is for a different target. Select the reviewed target or create a new review.");
        PowerShellCompilationDependencyLockHasher.EnsureValid(saved.DependencyLock, nameof(saved));
        if (!string.Equals(saved.DependencyLock.LockSha256, current.DependencyLock.LockSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("The dependencies differ from the saved review. Review the current dependencies before building.");
    }

    /// <summary>Creates a review file without overwriting an existing file or changing module inputs.</summary>
    public static void Write(string path, PowerShellCompilationReview review, string? moduleSourceDirectory)
    {
        if (review is null) throw new ArgumentNullException(nameof(review));
        var fullPath = Path.GetFullPath(path);
        if (moduleSourceDirectory is not null)
        {
#if NET472
            const string linkError = "Save the review through a path without symbolic links or junctions.";
            PowerShellCompilationPathSafety.EnsureNoLinksInExistingAncestors(moduleSourceDirectory, linkError);
            PowerShellCompilationPathSafety.EnsureNoLinksInExistingAncestors(fullPath, linkError);
#endif
            var sourceDirectory = ComparablePath(FrameworkCompatibility.ResolvePhysicalPath(moduleSourceDirectory))
                .TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            var physicalDirectory = FrameworkCompatibility.ResolvePhysicalPath(Path.GetDirectoryName(fullPath)!);
            fullPath = Path.Combine(physicalDirectory, Path.GetFileName(fullPath));
            var comparablePath = ComparablePath(fullPath);
            if (PowerShellCompilationPathSafety.PathEquals(comparablePath, sourceDirectory) ||
                PowerShellCompilationPathSafety.PathStartsWith(comparablePath, sourceDirectory + Path.DirectorySeparatorChar))
                throw new ArgumentException("Save the review outside the module source directory so it does not change the reviewed dependency inventory.", nameof(path));
        }

        var content = JsonSerializer.SerializeToUtf8Bytes(review, PowerShellCompilationReviewJsonContext.Default.PowerShellCompilationReview);
        var created = false;
        try
        {
            using var stream = new FileStream(fullPath, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            created = true;
            stream.Write(content, 0, content.Length);
        }
        catch
        {
            if (created) File.Delete(fullPath);
            throw;
        }
    }

    private static string ComparablePath(string path)
    {
        if (!FrameworkCompatibility.IsWindows()) return path;
        // Win32 device and extended DOS/UNC paths can name the same directory.
        if (path.StartsWith(@"\\.\", StringComparison.Ordinal))
            path = @"\\?\" + path.Substring(4);
        return WindowsNativePath.GetExtendedPath(path);
    }
}

[JsonSourceGenerationOptions(
    PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
    PropertyNameCaseInsensitive = true,
    ReadCommentHandling = JsonCommentHandling.Skip,
    AllowTrailingCommas = true,
    DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
    UseStringEnumConverter = true)]
[JsonSerializable(typeof(PowerShellCompilationReview))]
internal partial class PowerShellCompilationReviewJsonContext : JsonSerializerContext
{
}
